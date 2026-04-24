namespace BinScript.Core.Compiler;

using BinScript.Core.Compiler.Ast;
using BinScript.Core.Interfaces;
using BinScript.Core.Model;

/// <summary>
/// Builds a type environment from a <see cref="ScriptFile"/> AST.
/// Registers structs, bits-structs, enums, and constants, then validates
/// that every type reference resolves to a known declaration.
/// </summary>
public sealed class TypeResolver
{
    public Dictionary<string, StructDecl> Structs { get; } = new();
    public Dictionary<string, BitsStructDecl> BitsStructs { get; } = new();
    public Dictionary<string, EnumDecl> Enums { get; } = new();
    public Dictionary<string, ConstDecl> Constants { get; } = new();

    public List<Diagnostic> Diagnostics { get; } = [];

    // Track which modules have been resolved to detect import cycles.
    private readonly HashSet<string> _resolvedModules = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolve all declarations in <paramref name="file"/>, including imports.
    /// </summary>
    public void Resolve(ScriptFile file, IModuleResolver? moduleResolver = null)
    {
        // 1. Process imports first so that imported types are available.
        ResolveImports(file, moduleResolver);

        // 2. Register declarations from this file.
        RegisterDeclarations(file);

        // 3. Validate all type references in struct fields.
        ValidateTypeReferences(file);
    }

    // ─── Import resolution ──────────────────────────────────────────

    private void ResolveImports(ScriptFile file, IModuleResolver? moduleResolver)
    {
        foreach (var import in file.Imports)
        {
            if (moduleResolver is null)
            {
                Diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error, "BSC200",
                    $"Cannot resolve import \"{import.Path}\": no module resolver provided",
                    import.Span));
                continue;
            }

            if (!_resolvedModules.Add(import.Path))
            {
                // Already resolved (or currently resolving) — cycle or duplicate.
                continue;
            }

            var source = moduleResolver.ResolveModule(import.Path);
            if (source is null)
            {
                Diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error, "BSC201",
                    $"Unknown module \"{import.Path}\"",
                    import.Span));
                continue;
            }

            // Parse the imported module.
            var lexer = new Lexer(source, import.Path);
            var tokens = lexer.TokenizeAll();
            var parser = new Parser(tokens, import.Path);
            var (importedFile, parseDiags) = parser.Parse();

            Diagnostics.AddRange(parseDiags);

            // Recursively resolve imports in the imported module.
            ResolveImports(importedFile, moduleResolver);

            // Register the imported declarations.
            RegisterDeclarations(importedFile);
        }
    }

    // ─── Registration ───────────────────────────────────────────────

    private void RegisterDeclarations(ScriptFile file)
    {
        foreach (var s in file.Structs)
        {
            if (!TryRegisterName(s.Name, s.Span))
                continue;
            Structs[s.Name] = s;
        }

        foreach (var bs in file.BitsStructs)
        {
            if (!TryRegisterName(bs.Name, bs.Span))
                continue;
            BitsStructs[bs.Name] = bs;
        }

        foreach (var e in file.Enums)
        {
            if (!TryRegisterName(e.Name, e.Span))
                continue;
            Enums[e.Name] = e;
        }

        foreach (var c in file.Constants)
        {
            if (!TryRegisterName(c.Name, c.Span))
                continue;
            Constants[c.Name] = c;
        }
    }

    /// <summary>Returns <c>false</c> and emits a diagnostic if the name is already taken.</summary>
    private bool TryRegisterName(string name, SourceSpan span)
    {
        if (Structs.ContainsKey(name) || BitsStructs.ContainsKey(name) ||
            Enums.ContainsKey(name) || Constants.ContainsKey(name))
        {
            Diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error, "BSC100",
                $"Duplicate declaration \"{name}\"",
                span));
            return false;
        }
        return true;
    }

    // ─── Validation ─────────────────────────────────────────────────

    private void ValidateTypeReferences(ScriptFile file)
    {
        foreach (var s in file.Structs)
            ValidateStructMembers(s.Members, s.Parameters);

        // Bits-struct base types are always primitives – nothing extra to validate.
    }

    private void ValidateStructMembers(IReadOnlyList<MemberDecl> members, IReadOnlyList<string> structParams)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case FieldDecl field:
                    ValidateTypeRef(field.Type, structParams);
                    break;

                case AtBlock atBlock:
                    ValidateStructMembers(atBlock.Members, structParams);
                    break;
            }
        }
    }

    private void ValidateTypeRef(TypeReference typeRef, IReadOnlyList<string> structParams)
    {
        switch (typeRef)
        {
            // Built-in types — always valid.
            case PrimitiveTypeRef:
            case CStringTypeRef:
            case StringTypeRef:
            case FixedStringTypeRef:
            case BytesTypeRef:
            case BitTypeRef:
            case BitsTypeRef:
                break;

            case NamedTypeRef named:
                ValidateNamedType(named, structParams);
                break;

            case MatchTypeRef matchRef:
                foreach (var arm in matchRef.Match.Arms)
                    ValidateTypeRef(arm.Type, structParams);
                break;
        }
    }

    private void ValidateNamedType(NamedTypeRef named, IReadOnlyList<string> structParams)
    {
        var name = named.Name;

        // Check if it's a known struct.
        if (Structs.TryGetValue(name, out var structDecl))
        {
            // Validate argument count.
            if (named.Arguments.Count != structDecl.Parameters.Count)
            {
                Diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error, "BSC101",
                    $"Struct \"{name}\" expects {structDecl.Parameters.Count} parameter(s) but was given {named.Arguments.Count}",
                    named.Span));
            }
            return;
        }

        // Enums and bits-structs take no parameters.
        if (Enums.ContainsKey(name) || BitsStructs.ContainsKey(name))
            return;

        // It might be a struct parameter acting as a type name — skip validation.
        if (structParams.Contains(name))
            return;

        // Unknown type.
        Diagnostics.Add(new Diagnostic(
            DiagnosticSeverity.Error, "BSC102",
            $"Unknown type \"{name}\"",
            named.Span));
    }
}
