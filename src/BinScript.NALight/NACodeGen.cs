using System.Text;
using BinScript.Core.Compiler;
using BinScript.Core.Compiler.Ast;

namespace BinScript.NALight;

/// <summary>
/// Generates BinScript AST nodes (<see cref="ScriptFile"/>) directly from an <see cref="NASpec"/>,
/// and also generates plain ⎕NA strings by stripping BSX-Light extensions.
/// </summary>
public sealed class NACodeGen
{
    private static readonly SourceSpan S = SourceSpan.None;
    private static readonly FieldModifiers NoMods = new();

    private readonly NASpec _spec;
    private int _structCounter;
    private readonly List<StructDecl> _helperStructs = [];

    public NACodeGen(NASpec spec)
    {
        _spec = spec;
    }

    /// <summary>
    /// Generate a BinScript <see cref="ScriptFile"/> AST describing the argument layout.
    /// </summary>
    public ScriptFile ToScriptFile()
    {
        _structCounter = 0;
        _helperStructs.Clear();

        // Build root struct members from arguments
        var members = new List<MemberDecl>();
        int fieldIndex = 0;

        foreach (var arg in _spec.Arguments)
        {
            string fieldName = $"field_{fieldIndex++}";
            var (typeRef, arraySpec) = TypeToAst(arg.Type, arg.Direction);
            members.Add(new FieldDecl(fieldName, typeRef, null, arraySpec, NoMods, S));
        }

        var rootStruct = new StructDecl("Args", [], members, IsRoot: true, Coverage: null, MaxDepth: null, S);

        // All structs: helpers first, then root
        var allStructs = new List<StructDecl>(_helperStructs) { rootStruct };

        return new ScriptFile(
            FilePath: "<nalight>",
            DefaultEndian: Endianness.Little,
            DefaultEncoding: null,
            Imports: [],
            Params: [],
            Structs: allStructs,
            BitsStructs: [],
            Enums: [],
            Constants: [],
            Maps: [],
            Span: S);
    }

    /// <summary>
    /// Generate a plain ⎕NA-compatible right-argument string by stripping extensions.
    /// </summary>
    public string ToPlainNA()
    {
        var sb = new StringBuilder();

        // Return type
        if (_spec.ReturnType is not null)
        {
            sb.Append(TypeToNA(_spec.ReturnType));
            sb.Append(' ');
        }

        // Library|Function
        if (_spec.LibraryPath is not null)
        {
            sb.Append(_spec.LibraryPath);
            sb.Append('|');
            sb.Append(_spec.FunctionName);
            if (_spec.PassByPointer) sb.Append('*');
            if (_spec.ThreadSafe) sb.Append('&');
        }

        // Arguments
        foreach (var arg in _spec.Arguments)
        {
            sb.Append(' ');
            if (arg.Direction != NADirection.None)
            {
                sb.Append(arg.Direction switch
                {
                    NADirection.In => '<',
                    NADirection.Out => '>',
                    NADirection.InOut => '=',
                    _ => "",
                });
            }
            sb.Append(TypeToNA(arg.Type));
        }

        return sb.ToString();
    }

    // ── AST type generation ─────────────────────────────────────────────────

    private (TypeReference type, ArraySpec? array) TypeToAst(NATypeDesc desc, NADirection direction = NADirection.None)
    {
        return desc switch
        {
            NAPrimitiveDesc p => (PrimitiveToAst(p), null),
            NAPointerDesc => (new PrimitiveTypeRef(TokenType.U64, S), null),
            NADecimalDesc => (new PrimitiveTypeRef(TokenType.F64, S), null),
            NAComplexDesc => (ComplexToAst(), null),
            NANullTermDesc nt => (NullTermToAst(nt), null),
            NADoubleNullDesc dn => DoubleNullToAst(dn),
            NAStructDesc s => (StructToAst(s), null),
            NAArrayedDesc a => ArrayedToAst(a, direction),
            _ => throw new InvalidOperationException($"Unsupported type: {desc.GetType().Name}"),
        };
    }

    private static PrimitiveTypeRef PrimitiveToAst(NAPrimitiveDesc p)
    {
        var tokenType = (p.TypeLetter, p.Width) switch
        {
            ('I', 1) => TokenType.I8,
            ('I', 2) => TokenType.I16Le,
            ('I', 4) => TokenType.I32Le,
            ('I', 8) => TokenType.I64Le,
            ('U', 1) => TokenType.U8,
            ('U', 2) => TokenType.U16Le,
            ('U', 4) => TokenType.U32Le,
            ('U', 8) => TokenType.U64Le,
            // C/T map to unsigned integers of the same width
            ('C', 1) or ('T', 1) => TokenType.U8,
            ('C', 2) or ('T', 2) => TokenType.U16Le,
            ('C', 4) or ('T', 4) => TokenType.U32Le,
            ('F', 4) => TokenType.F32Le,
            ('F', 8) => TokenType.F64Le,
            _ => throw new InvalidOperationException($"Unsupported primitive: {p.TypeLetter}{p.Width}"),
        };
        return new PrimitiveTypeRef(tokenType, S);
    }

    private NamedTypeRef ComplexToAst()
    {
        string name = $"Complex_{_structCounter++}";
        var members = new List<MemberDecl>
        {
            new FieldDecl("re", new PrimitiveTypeRef(TokenType.F64Le, S), null, null, NoMods, S),
            new FieldDecl("im", new PrimitiveTypeRef(TokenType.F64Le, S), null, null, NoMods, S),
        };
        _helperStructs.Add(new StructDecl(name, [], members, IsRoot: false, Coverage: null, MaxDepth: null, S));
        return new NamedTypeRef(name, [], S);
    }

    private static TypeReference NullTermToAst(NANullTermDesc nt)
    {
        // 0T/0C → cstring, with encoding for wide chars
        var mods = EncodingMods(nt.Width);
        return new CStringTypeRef(S);
        // TODO: encoding is applied via FieldModifiers.Encoding — wire it through
    }

    private static (TypeReference type, ArraySpec? array) DoubleNullToAst(NADoubleNullDesc dn)
    {
        // 00T/00C → cstring[] @until(@last_size == 0)
        var elemType = new CStringTypeRef(S);
        // @last_size is represented as FunctionCallExpr("last_size", []) in BinScript AST
        var condition = new BinaryExpr(
            new FunctionCallExpr("last_size", [], S),
            BinaryOp.Eq,
            new IntLiteralExpr(0, S),
            S);
        var arraySpec = new UntilArraySpec(condition, S);
        return (elemType, arraySpec);
    }

    private NamedTypeRef StructToAst(NAStructDesc s)
    {
        string name = $"Struct_{_structCounter++}";
        var members = new List<MemberDecl>();
        int localField = 0;

        foreach (var field in s.Fields)
        {
            string fieldName = $"f_{localField++}";
            var (typeRef, arraySpec) = TypeToAst(field.Type, field.Direction);

            // Direction inside struct + non-pointer type → wrap in ptr<T, u64>
            if (field.Direction != NADirection.None && field.Type is not NAPointerDesc)
            {
                typeRef = new PtrTypeRef(typeRef, new PrimitiveTypeRef(TokenType.U64Le, S),
                    IsRelative: false, InnerModifiers: null, S);
            }

            var mods = NoMods;
            // Apply encoding for null-terminated types inside structs
            if (field.Type is NANullTermDesc nt)
                mods = EncodingMods(nt.Width);
            else if (field.Type is NADoubleNullDesc dn)
                mods = EncodingMods(dn.Width);

            members.Add(new FieldDecl(fieldName, typeRef, null, arraySpec, mods, S));
        }

        _helperStructs.Add(new StructDecl(name, [], members, IsRoot: false, Coverage: null, MaxDepth: null, S));
        return new NamedTypeRef(name, [], S);
    }

    private (TypeReference type, ArraySpec? array) ArrayedToAst(NAArrayedDesc a, NADirection direction)
    {
        var (elemType, innerArray) = TypeToAst(a.Element, direction);
        // If the inner type already has an array spec (e.g., 00T produces one),
        // the outer array wraps it — but this shouldn't happen in practice.

        ArraySpec? arraySpec = a.ArrayKind switch
        {
            FixedArrayKind f => new CountArraySpec(new IntLiteralExpr(f.Count, S), S),
            VariableArrayKind => new GreedyArraySpec(S),
            CountedArrayKind c when c.MaxCount is int max =>
                new CountArraySpec(new IntLiteralExpr(max, S), S),
            CountedArrayKind => new GreedyArraySpec(S),
            _ => throw new InvalidOperationException($"Unsupported array kind: {a.ArrayKind.GetType().Name}"),
        };

        return (elemType, arraySpec);
    }

    // ── Plain ⎕NA generation ────────────────────────────────────────────────

    private static string TypeToNA(NATypeDesc type)
    {
        return type switch
        {
            NAPrimitiveDesc p => $"{p.TypeLetter}{p.Width}",
            NAPointerDesc => "P",
            NADecimalDesc => "D",
            NAComplexDesc => "J",
            NANullTermDesc nt => $"0{nt.TypeLetter}{nt.Width}",
            // 00T is not valid in plain ⎕NA — downgrade to P (opaque pointer)
            NADoubleNullDesc => "P",
            NAStructDesc s => StructToNA(s),
            NAArrayedDesc a => ArrayedToNA(a),
            _ => throw new InvalidOperationException($"Unsupported type for plain NA: {type.GetType().Name}"),
        };
    }

    private static string StructToNA(NAStructDesc s)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        bool first = true;
        foreach (var field in s.Fields)
        {
            if (!first) sb.Append(' ');
            first = false;

            // Strip directions (not allowed in plain ⎕NA structs)
            string typeStr = TypeToNA(field.Type);

            // If the original had direction + non-pointer type → downgrade to P
            if (field.Direction != NADirection.None && field.Type is not NAPointerDesc)
                typeStr = "P";

            sb.Append(typeStr);
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string ArrayedToNA(NAArrayedDesc a)
    {
        string elem = TypeToNA(a.Element);
        return a.ArrayKind switch
        {
            FixedArrayKind f => $"{elem}[{f.Count}]",
            VariableArrayKind => $"{elem}[]",
            CountedArrayKind c when c.MaxCount is int max => $"{elem}[{max}]",
            CountedArrayKind => $"{elem}[]",
            _ => elem,
        };
    }

    // ── Shared helpers ──────────────────────────────────────────────────────

    private static FieldModifiers EncodingMods(int charWidth) => charWidth switch
    {
        2 => new FieldModifiers { Encoding = "utf-16le" },
        4 => new FieldModifiers { Encoding = "utf-32le" },
        _ => new FieldModifiers(),
    };
}
