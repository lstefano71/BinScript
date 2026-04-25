namespace BinScript.Core.Compiler;

using BinScript.Core.Bytecode;
using BinScript.Core.Compiler.Ast;

/// <summary>
/// Walks the typed AST produced by semantic analysis and emits a
/// <see cref="BytecodeProgram"/> using <see cref="BytecodeBuilder"/>.
/// </summary>
public sealed class BytecodeEmitter
{
    private readonly ScriptFile _ast;
    private readonly TypeResolver _typeEnv;
    private readonly Dictionary<string, object> _parameters;
    private readonly HashSet<string> _fileParamNames;

    // Shared string table across all struct builders.
    private readonly BytecodeBuilder _masterBuilder = new();

    // Maps struct/bits-struct name → index in the output Structs array.
    private readonly Dictionary<string, int> _structIndexMap = new();

    // Per-struct context used during emission.
    private readonly List<StructEmitContext> _structContexts = new();

    // Tracks the current search lambda parameter name (e.g., "s" in s => pred).
    // When set, s.field emits PushElemField instead of PushFieldVal.
    private string? _currentSearchLambdaParam;

    // Counter for generating unique mangled names per @map call site.
    private int _mapCallSiteCounter;
    // Memoize inlined @map bodies by source span so pre-scan and emission see the same rewritten AST.
    private readonly Dictionary<SourceSpan, Expression> _inlinedMapCalls = new();

    public BytecodeEmitter(ScriptFile ast, TypeResolver typeEnv, Dictionary<string, object>? parameters = null)
    {
        _ast = ast;
        _typeEnv = typeEnv;
        _parameters = parameters ?? new Dictionary<string, object>();
        _fileParamNames = new HashSet<string>(ast.Params.Select(p => p.Name));
    }

    public BytecodeProgram Emit()
    {
        // Pass 1: assign struct indices and intern names.
        AssignStructIndices();

        // Pass 2: emit bytecode for each struct and bits-struct.
        for (int i = 0; i < _structContexts.Count; i++)
        {
            var ctx = _structContexts[i];
            if (ctx.IsBitsStruct)
                EmitBitsStruct(ctx);
            else
                EmitStruct(ctx);
        }

        // Pass 3: assemble the final bytecode by concatenating per-struct code.
        return BuildProgram();
    }

    // ─── Pass 1: Index Assignment ─────────────────────────────────────

    private void AssignStructIndices()
    {
        int index = 0;

        foreach (var s in _ast.Structs)
        {
            _structIndexMap[s.Name] = index;
            var ctx = new StructEmitContext
            {
                Name = s.Name,
                NameIndex = _masterBuilder.InternString(s.Name),
                Decl = s,
                Parameters = s.Parameters,
                IsRoot = s.IsRoot,
                IsBitsStruct = false,
                Coverage = s.Coverage,
                MaxDepth = s.MaxDepth,
            };
            _structContexts.Add(ctx);
            index++;
        }

        foreach (var bs in _ast.BitsStructs)
        {
            _structIndexMap[bs.Name] = index;
            var ctx = new StructEmitContext
            {
                Name = bs.Name,
                NameIndex = _masterBuilder.InternString(bs.Name),
                BitsDecl = bs,
                IsBitsStruct = true,
            };
            _structContexts.Add(ctx);
            index++;
        }
    }

    // ─── Pass 2a: Emit regular struct ─────────────────────────────────

    private void EmitStruct(StructEmitContext ctx)
    {
        var builder = ctx.Builder;
        var decl = ctx.Decl!;

        // Register struct parameters as named bindings for expression lookup.
        for (int i = 0; i < decl.Parameters.Count; i++)
        {
            ctx.ParamIndices[decl.Parameters[i]] = (ushort)i;
        }

        // Pre-scan: collect all dotted field access paths (a.b) used in expressions
        // and pre-allocate hidden field IDs in the parent scope so that
        // EmitFieldAccess can resolve them via FieldIndices.
        PreAllocateDottedFields(ctx, decl.Members);

        // Pre-scan: identify which array fields are targets of .find()/.any()/.all()
        ctx.SearchedArrayNames = CollectSearchedArrayNames(decl.Members);

        EmitMembers(ctx, decl.Members);
        builder.Emit(Opcode.Return);
    }

    /// <summary>
    /// Scans all expressions in the struct's members for FieldAccessExpr patterns
    /// like <c>child.field</c> and pre-allocates hidden field IDs in the parent scope.
    /// Only pre-allocates paths whose root identifier is a declared field or let binding
    /// in this struct (not lambda parameters or other scopes).
    /// </summary>
    private void PreAllocateDottedFields(StructEmitContext ctx, IReadOnlyList<MemberDecl> members)
    {
        // Collect names declared in this struct scope.
        var declaredNames = new HashSet<string>();
        foreach (var member in members)
        {
            if (member is FieldDecl fd) declaredNames.Add(fd.Name);
            else if (member is LetBinding lb) declaredNames.Add(lb.Name);
            else if (member is AtBlock atBlock) CollectDeclaredNames(atBlock.Members, declaredNames);
        }

        var dottedPaths = new HashSet<string>();
        CollectDottedPaths(members, dottedPaths, new HashSet<string>());

        // After collection, any inlined @map bodies may have introduced mangled let names
        // (e.g., __map_0_s). Add these to declaredNames so their dotted paths get allocated.
        foreach (var inlined in _inlinedMapCalls.Values)
        {
            if (inlined is BlockExpr block)
            {
                foreach (var binding in block.Bindings)
                    declaredNames.Add(binding.Name);
            }
        }

        foreach (var path in dottedPaths)
        {
            // Only pre-allocate if the root identifier is a declared field.
            int dotIdx = path.IndexOf('.');
            if (dotIdx > 0)
            {
                string root = path.Substring(0, dotIdx);
                if (!declaredNames.Contains(root)) continue;
            }

            if (!ctx.FieldIndices.ContainsKey(path))
            {
                ctx.AllocateField(path, new FieldModifiers { IsHidden = true }, _masterBuilder);
            }
        }
    }

    private static void CollectDeclaredNames(IReadOnlyList<MemberDecl> members, HashSet<string> names)
    {
        foreach (var member in members)
        {
            if (member is FieldDecl fd) names.Add(fd.Name);
            else if (member is LetBinding lb) names.Add(lb.Name);
            else if (member is AtBlock atBlock) CollectDeclaredNames(atBlock.Members, names);
        }
    }

    /// <summary>
    /// Recursively walks all expressions in members to find FieldAccessExpr nodes
    /// and collects their flattened dotted path strings.
    /// Tracks lambda parameters to exclude paths rooted in them.
    /// </summary>
    private void CollectDottedPaths(IReadOnlyList<MemberDecl> members, HashSet<string> paths, HashSet<string> lambdaParams)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case FieldDecl field:
                    CollectDottedPathsInField(field, paths, lambdaParams);
                    break;
                case LetBinding let:
                    CollectDottedPathsInExpr(let.Value, paths, lambdaParams);
                    break;
                case SeekDirective seek:
                    CollectDottedPathsInExpr(seek.Offset, paths, lambdaParams);
                    break;
                case AtBlock atBlock:
                    if (atBlock.Guard is not null) CollectDottedPathsInExpr(atBlock.Guard, paths, lambdaParams);
                    CollectDottedPathsInExpr(atBlock.Offset, paths, lambdaParams);
                    CollectDottedPaths(atBlock.Members, paths, lambdaParams);
                    break;
                case AssertDirective assert:
                    CollectDottedPathsInExpr(assert.Condition, paths, lambdaParams);
                    break;
            }
        }
    }

    private void CollectDottedPathsInField(FieldDecl field, HashSet<string> paths, HashSet<string> lambdaParams)
    {
        if (field.Modifiers.DerivedExpression is not null)
            CollectDottedPathsInExpr(field.Modifiers.DerivedExpression, paths, lambdaParams);
        if (field.Array is CountArraySpec count)
            CollectDottedPathsInExpr(count.Count, paths, lambdaParams);
        else if (field.Array is UntilArraySpec until)
            CollectDottedPathsInExpr(until.Condition, paths, lambdaParams);
        else if (field.Array is SentinelArraySpec sentinel)
            CollectSentinelDottedPaths(sentinel, field.Name, paths, lambdaParams);
        // Also scan struct call arguments
        if (field.Type is NamedTypeRef named)
            foreach (var arg in named.Arguments) CollectDottedPathsInExpr(arg, paths, lambdaParams);
        if (field.Type is MatchTypeRef match)
        {
            CollectDottedPathsInExpr(match.Match.Discriminant, paths, lambdaParams);
            foreach (var arm in match.Match.Arms)
            {
                if (arm.Guard is not null) CollectDottedPathsInExpr(arm.Guard, paths, lambdaParams);
                if (arm.Type is NamedTypeRef namedArm)
                    foreach (var arg in namedArm.Arguments) CollectDottedPathsInExpr(arg, paths, lambdaParams);
                // Match arm arrays
                if (arm.Array is CountArraySpec armCount)
                    CollectDottedPathsInExpr(armCount.Count, paths, lambdaParams);
                else if (arm.Array is UntilArraySpec armUntil)
                    CollectDottedPathsInExpr(armUntil.Condition, paths, lambdaParams);
                else if (arm.Array is SentinelArraySpec armSentinel)
                    CollectSentinelDottedPaths(armSentinel, field.Name, paths, lambdaParams);
            }
        }
    }

    /// <summary>
    /// Collect dotted paths from a sentinel predicate, substituting the lambda param with the field name.
    /// E.g., <c>@until_sentinel(e =&gt; e.rva == 0)</c> on field "imports" → collects "imports.rva".
    /// </summary>
    private void CollectSentinelDottedPaths(SentinelArraySpec sentinel, string fieldName,
        HashSet<string> paths, HashSet<string> lambdaParams)
    {
        var substituted = SubstituteSentinelParam(sentinel.Predicate, sentinel.ParamName, fieldName);
        CollectDottedPathsInExpr(substituted, paths, lambdaParams);
    }

    /// <summary>Replace sentinel lambda parameter references with the array field name.</summary>
    private static Expression SubstituteSentinelParam(Expression expr, string paramName, string fieldName)
    {
        return expr switch
        {
            IdentifierExpr id when id.Name == paramName
                => new IdentifierExpr(fieldName, id.Span),
            BinaryExpr bin
                => new BinaryExpr(
                    SubstituteSentinelParam(bin.Left, paramName, fieldName),
                    bin.Op,
                    SubstituteSentinelParam(bin.Right, paramName, fieldName),
                    bin.Span),
            UnaryExpr un
                => new UnaryExpr(un.Op, SubstituteSentinelParam(un.Operand, paramName, fieldName), un.Span),
            FieldAccessExpr fa
                => new FieldAccessExpr(SubstituteSentinelParam(fa.Object, paramName, fieldName), fa.FieldName, fa.Span),
            FunctionCallExpr fc
                => new FunctionCallExpr(fc.FunctionName,
                    fc.Args.Select(a => SubstituteSentinelParam(a, paramName, fieldName)).ToList(),
                    fc.Span),
            _ => expr,
        };
    }

    private void CollectDottedPathsInExpr(Expression expr, HashSet<string> paths, HashSet<string> lambdaParams)
    {
        switch (expr)
        {
            case FieldAccessExpr fa:
            {
                string path = FlattenFieldPath(fa);
                // Only collect if root is NOT a lambda parameter.
                string root = GetRootIdentifier(fa);
                if (!lambdaParams.Contains(root))
                    paths.Add(path);
                // Also recurse into sub-expressions
                CollectDottedPathsInExpr(fa.Object, paths, lambdaParams);
                break;
            }
            case BinaryExpr bin:
                CollectDottedPathsInExpr(bin.Left, paths, lambdaParams);
                CollectDottedPathsInExpr(bin.Right, paths, lambdaParams);
                break;
            case UnaryExpr un:
                CollectDottedPathsInExpr(un.Operand, paths, lambdaParams);
                break;
            case IndexAccessExpr ia:
                CollectDottedPathsInExpr(ia.Object, paths, lambdaParams);
                CollectDottedPathsInExpr(ia.Index, paths, lambdaParams);
                break;
            case FunctionCallExpr fn:
                // Check for @map call — inline and collect from rewritten body.
                if (_typeEnv.Maps.TryGetValue(fn.FunctionName, out var mapDecl))
                {
                    var inlined = GetOrInlineMapCall(fn, mapDecl);
                    CollectDottedPathsInExpr(inlined, paths, lambdaParams);
                }
                else
                {
                    foreach (var arg in fn.Args) CollectDottedPathsInExpr(arg, paths, lambdaParams);
                }
                break;
            case MethodCallExpr mc:
                CollectDottedPathsInExpr(mc.Object, paths, lambdaParams);
                foreach (var arg in mc.Args) CollectDottedPathsInExpr(arg, paths, lambdaParams);
                break;
            case LambdaExpr lambda:
            {
                // Lambda parameter shadows — add to exclusion set.
                var innerParams = new HashSet<string>(lambdaParams) { lambda.Parameter };
                CollectDottedPathsInExpr(lambda.Body, paths, innerParams);
                break;
            }
            case BlockExpr block:
            {
                foreach (var binding in block.Bindings)
                    CollectDottedPathsInExpr(binding.Value, paths, lambdaParams);
                CollectDottedPathsInExpr(block.Result, paths, lambdaParams);
                break;
            }
        }
    }

    private static string GetRootIdentifier(Expression expr)
    {
        return expr switch
        {
            IdentifierExpr id => id.Name,
            FieldAccessExpr fa => GetRootIdentifier(fa.Object),
            IndexAccessExpr ia => GetRootIdentifier(ia.Object),
            _ => "",
        };
    }

    /// <summary>
    /// Scans all expressions in members for MethodCallExpr with .find()/.any()/.all()/.find_or()
    /// and returns the set of array field names that are targets of these calls.
    /// </summary>
    private HashSet<string> CollectSearchedArrayNames(IReadOnlyList<MemberDecl> members)
    {
        var names = new HashSet<string>();
        foreach (var member in members)
            CollectSearchedArrayNamesInMember(member, names);
        return names;
    }

    private void CollectSearchedArrayNamesInMember(MemberDecl member, HashSet<string> names)
    {
        switch (member)
        {
            case FieldDecl field:
                if (field.Type is MatchTypeRef mt)
                {
                    CollectSearchedArrayNamesInExpr(mt.Match.Discriminant, names);
                    foreach (var arm in mt.Match.Arms)
                    {
                        if (arm.Array is CountArraySpec armCas)
                            CollectSearchedArrayNamesInExpr(armCas.Count, names);
                        else if (arm.Array is UntilArraySpec armUas)
                            CollectSearchedArrayNamesInExpr(armUas.Condition, names);
                    }
                }
                if (field.Array is CountArraySpec cas)
                    CollectSearchedArrayNamesInExpr(cas.Count, names);
                else if (field.Array is UntilArraySpec uas)
                    CollectSearchedArrayNamesInExpr(uas.Condition, names);
                break;
            case LetBinding lb:
                CollectSearchedArrayNamesInExpr(lb.Value, names);
                break;
            case AtBlock atBlock:
                CollectSearchedArrayNamesInExpr(atBlock.Offset, names);
                if (atBlock.Guard is not null) CollectSearchedArrayNamesInExpr(atBlock.Guard, names);
                foreach (var m in atBlock.Members)
                    CollectSearchedArrayNamesInMember(m, names);
                break;
        }
    }

    private void CollectSearchedArrayNamesInExpr(Expression expr, HashSet<string> names)
    {
        switch (expr)
        {
            case MethodCallExpr mc when mc.MethodName is "find" or "find_or" or "any" or "all":
                if (mc.Object is IdentifierExpr id)
                    names.Add(id.Name);
                foreach (var arg in mc.Args)
                    CollectSearchedArrayNamesInExpr(arg, names);
                break;
            case MethodCallExpr mc2:
                CollectSearchedArrayNamesInExpr(mc2.Object, names);
                foreach (var arg in mc2.Args)
                    CollectSearchedArrayNamesInExpr(arg, names);
                break;
            case BinaryExpr bin:
                CollectSearchedArrayNamesInExpr(bin.Left, names);
                CollectSearchedArrayNamesInExpr(bin.Right, names);
                break;
            case UnaryExpr un:
                CollectSearchedArrayNamesInExpr(un.Operand, names);
                break;
            case FunctionCallExpr fn:
                // Check for @map call — inline and collect from rewritten body.
                if (_typeEnv.Maps.TryGetValue(fn.FunctionName, out var mapDecl))
                {
                    var inlined = GetOrInlineMapCall(fn, mapDecl);
                    CollectSearchedArrayNamesInExpr(inlined, names);
                }
                else
                {
                    foreach (var arg in fn.Args)
                        CollectSearchedArrayNamesInExpr(arg, names);
                }
                break;
            case LambdaExpr lambda:
                CollectSearchedArrayNamesInExpr(lambda.Body, names);
                break;
            case BlockExpr block:
                foreach (var binding in block.Bindings)
                    CollectSearchedArrayNamesInExpr(binding.Value, names);
                CollectSearchedArrayNamesInExpr(block.Result, names);
                break;
        }
    }

    private void EmitMembers(StructEmitContext ctx, IReadOnlyList<MemberDecl> members)
    {
        for (int i = 0; i < members.Count; i++)
        {
            EmitMember(ctx, members[i]);
        }
    }

    private void EmitMember(StructEmitContext ctx, MemberDecl member)
    {
        switch (member)
        {
            case FieldDecl field:
                EmitField(ctx, field);
                break;
            case SeekDirective seek:
                EmitExpression(ctx, seek.Offset);
                ctx.Builder.Emit(Opcode.SeekAbs);
                break;
            case AtBlock atBlock:
            {
                int skipPatch = -1;
                if (atBlock.Guard is not null)
                {
                    EmitExpression(ctx, atBlock.Guard);
                    ctx.Builder.Emit(Opcode.JumpIfFalse);
                    skipPatch = ctx.Builder.ReserveI32();
                }
                ctx.Builder.Emit(Opcode.SeekPush);
                EmitExpression(ctx, atBlock.Offset);
                ctx.Builder.Emit(Opcode.SeekAbs);
                EmitMembers(ctx, atBlock.Members);
                ctx.Builder.Emit(Opcode.SeekPop);
                if (skipPatch >= 0)
                    ctx.Builder.PatchI32(skipPatch, ctx.Builder.Position);
                break;
            }
            case LetBinding let:
                EmitLetBinding(ctx, let);
                break;
            case AlignDirective align:
                EmitAlign(ctx, align);
                break;
            case SkipDirective skip:
                ctx.Builder.Emit(Opcode.SkipFixed);
                ctx.Builder.EmitU16((ushort)skip.ByteCount);
                break;
            case AssertDirective assert:
                EmitAssertDirective(ctx, assert);
                break;
        }
    }

    private void EmitField(StructEmitContext ctx, FieldDecl field)
    {
        ushort fieldId = ctx.AllocateField(field.Name, field.Modifiers, _masterBuilder);

        if (field.Modifiers.IsDerived && field.Modifiers.DerivedExpression is not null)
        {
            EmitExpression(ctx, field.Modifiers.DerivedExpression);
            // The VM stores the expression result into the field.
            ctx.Builder.Emit(Opcode.StoreFieldVal);
            ctx.Builder.EmitU16(fieldId);
            return;
        }

        if (field.Array is not null)
        {
            EmitArrayField(ctx, field, fieldId);
        }
        else
        {
            EmitFieldRead(ctx, field.Type, fieldId, field.Modifiers);
            EmitFieldPostProcessing(ctx, field, fieldId);
        }
    }

    private void EmitFieldRead(StructEmitContext ctx, TypeReference type, ushort fieldId, FieldModifiers modifiers)
    {
        switch (type)
        {
            case PrimitiveTypeRef prim:
                EmitPrimitiveRead(ctx, prim, fieldId);
                break;
            case CStringTypeRef:
                EmitCStringRead(ctx, fieldId, modifiers);
                break;
            case StringTypeRef str:
                EmitStringRead(ctx, str, fieldId, modifiers);
                break;
            case FixedStringTypeRef fixStr:
                EmitFixedStringRead(ctx, fixStr, fieldId, modifiers);
                break;
            case BytesTypeRef bytes:
                EmitBytesRead(ctx, bytes, fieldId);
                break;
            case NamedTypeRef named:
                EmitNamedTypeRead(ctx, named, fieldId);
                break;
            case MatchTypeRef matchType:
                EmitMatchTypeRead(ctx, matchType, fieldId, modifiers);
                break;
            case PtrTypeRef ptr:
                EmitPtrRead(ctx, ptr, fieldId, modifiers);
                break;
            case NullableTypeRef nullable:
                EmitNullableRead(ctx, nullable, fieldId, modifiers);
                break;
            case BitTypeRef:
                ctx.Builder.Emit(Opcode.ReadBit);
                ctx.Builder.EmitU16(fieldId);
                break;
            case BitsTypeRef bits:
                ctx.Builder.Emit(Opcode.ReadBits);
                ctx.Builder.EmitU16(fieldId);
                ctx.Builder.EmitU8((byte)bits.Count);
                break;
        }
    }

    private void EmitFieldPostProcessing(StructEmitContext ctx, FieldDecl field, ushort fieldId)
    {
        if (field.MagicValue is not null)
        {
            ctx.Builder.Emit(Opcode.AssertValue);
            ctx.Builder.EmitU16(fieldId);
            EmitInlineValue(ctx, field.MagicValue);
        }
    }

    private void EmitInlineValue(StructEmitContext ctx, Expression expr)
    {
        switch (expr)
        {
            case IntLiteralExpr intLit:
                ctx.Builder.EmitU8(0); // type tag: i64
                ctx.Builder.EmitI64(intLit.Value);
                break;
            case StringLiteralExpr strLit:
                ctx.Builder.EmitU8(2); // type tag: string
                ctx.Builder.EmitU16(_masterBuilder.InternString(strLit.Value));
                break;
            case BoolLiteralExpr boolLit:
                ctx.Builder.EmitU8(3); // type tag: bool
                ctx.Builder.EmitU8(boolLit.Value ? (byte)1 : (byte)0);
                break;
            default:
                // Try to resolve enum-qualified values like ElfClass.ELFCLASS64
                if (TryResolveEnumValue(expr, out long enumVal))
                {
                    ctx.Builder.EmitU8(0); // type tag: i64
                    ctx.Builder.EmitI64(enumVal);
                }
                else if (TryEvalConstant(expr, out long constVal))
                {
                    ctx.Builder.EmitU8(0);
                    ctx.Builder.EmitI64(constVal);
                }
                else
                {
                    ctx.Builder.EmitU8(0);
                    ctx.Builder.EmitI64(0);
                }
                break;
        }
    }

    private bool TryResolveEnumValue(Expression expr, out long value)
    {
        value = 0;
        if (expr is FieldAccessExpr fa && fa.Object is IdentifierExpr id)
        {
            if (_typeEnv.Enums.TryGetValue(id.Name, out var enumDecl))
            {
                foreach (var variant in enumDecl.Variants)
                {
                    if (variant.Name == fa.FieldName && TryEvalConstant(variant.Value, out value))
                        return true;
                }
            }
        }
        return false;
    }

    // ─── Primitive reads ──────────────────────────────────────────────

    private void EmitPrimitiveRead(StructEmitContext ctx, PrimitiveTypeRef prim, ushort fieldId)
    {
        var opcode = ResolvePrimitiveOpcode(prim.PrimitiveToken);
        ctx.Builder.Emit(opcode);
        ctx.Builder.EmitU16(fieldId);
    }

    private Opcode ResolvePrimitiveOpcode(TokenType token)
    {
        return token switch
        {
            TokenType.U8 => Opcode.ReadU8,
            TokenType.I8 => Opcode.ReadI8,
            TokenType.Bool => Opcode.ReadBool,

            TokenType.U16Le => Opcode.ReadU16Le,
            TokenType.U16Be => Opcode.ReadU16Be,
            TokenType.U16 => DefaultEndianIs(Endianness.Big) ? Opcode.ReadU16Be : Opcode.ReadU16Le,

            TokenType.I16Le => Opcode.ReadI16Le,
            TokenType.I16Be => Opcode.ReadI16Be,
            TokenType.I16 => DefaultEndianIs(Endianness.Big) ? Opcode.ReadI16Be : Opcode.ReadI16Le,

            TokenType.U32Le => Opcode.ReadU32Le,
            TokenType.U32Be => Opcode.ReadU32Be,
            TokenType.U32 => DefaultEndianIs(Endianness.Big) ? Opcode.ReadU32Be : Opcode.ReadU32Le,

            TokenType.I32Le => Opcode.ReadI32Le,
            TokenType.I32Be => Opcode.ReadI32Be,
            TokenType.I32 => DefaultEndianIs(Endianness.Big) ? Opcode.ReadI32Be : Opcode.ReadI32Le,

            TokenType.U64Le => Opcode.ReadU64Le,
            TokenType.U64Be => Opcode.ReadU64Be,
            TokenType.U64 => DefaultEndianIs(Endianness.Big) ? Opcode.ReadU64Be : Opcode.ReadU64Le,

            TokenType.I64Le => Opcode.ReadI64Le,
            TokenType.I64Be => Opcode.ReadI64Be,
            TokenType.I64 => DefaultEndianIs(Endianness.Big) ? Opcode.ReadI64Be : Opcode.ReadI64Le,

            TokenType.F32Le => Opcode.ReadF32Le,
            TokenType.F32Be => Opcode.ReadF32Be,
            TokenType.F32 => DefaultEndianIs(Endianness.Big) ? Opcode.ReadF32Be : Opcode.ReadF32Le,

            TokenType.F64Le => Opcode.ReadF64Le,
            TokenType.F64Be => Opcode.ReadF64Be,
            TokenType.F64 => DefaultEndianIs(Endianness.Big) ? Opcode.ReadF64Be : Opcode.ReadF64Le,

            _ => throw new InvalidOperationException($"Unsupported primitive token: {token}"),
        };
    }

    private bool DefaultEndianIs(Endianness e) => (_ast.DefaultEndian ?? Endianness.Little) == e;

    // ─── String reads ─────────────────────────────────────────────────

    private byte ResolveEncoding(FieldModifiers modifiers)
    {
        var enc = modifiers.Encoding ?? _ast.DefaultEncoding ?? "utf8";
        return enc.ToLowerInvariant() switch
        {
            "utf8" or "utf-8" => (byte)StringEncoding.Utf8,
            "ascii" => (byte)StringEncoding.Ascii,
            "utf16le" or "utf-16le" => (byte)StringEncoding.Utf16Le,
            "utf16be" or "utf-16be" => (byte)StringEncoding.Utf16Be,
            "latin1" => (byte)StringEncoding.Latin1,
            _ => (byte)StringEncoding.Utf8,
        };
    }

    private void EmitCStringRead(StructEmitContext ctx, ushort fieldId, FieldModifiers modifiers)
    {
        ctx.Builder.Emit(Opcode.ReadCString);
        ctx.Builder.EmitU16(fieldId);
        ctx.Builder.EmitU8(ResolveEncoding(modifiers));
    }

    private void EmitStringRead(StructEmitContext ctx, StringTypeRef str, ushort fieldId, FieldModifiers modifiers)
    {
        EmitExpression(ctx, str.Length);
        ctx.Builder.Emit(Opcode.ReadStringDyn);
        ctx.Builder.EmitU16(fieldId);
        ctx.Builder.EmitU8(ResolveEncoding(modifiers));
    }

    private void EmitFixedStringRead(StructEmitContext ctx, FixedStringTypeRef fixStr, ushort fieldId, FieldModifiers modifiers)
    {
        if (TryEvalConstant(fixStr.Length, out long len))
        {
            ctx.Builder.Emit(Opcode.ReadFixedStr);
            ctx.Builder.EmitU16(fieldId);
            ctx.Builder.EmitU16((ushort)len);
            ctx.Builder.EmitU8(ResolveEncoding(modifiers));
        }
        else
        {
            EmitExpression(ctx, fixStr.Length);
            ctx.Builder.Emit(Opcode.ReadStringDyn);
            ctx.Builder.EmitU16(fieldId);
            ctx.Builder.EmitU8(ResolveEncoding(modifiers));
        }
    }

    // ─── Bytes reads ──────────────────────────────────────────────────

    private void EmitBytesRead(StructEmitContext ctx, BytesTypeRef bytes, ushort fieldId)
    {
        if (TryEvalConstant(bytes.Length, out long len))
        {
            ctx.Builder.Emit(Opcode.ReadBytesFixed);
            ctx.Builder.EmitU16(fieldId);
            ctx.Builder.EmitU32((uint)len);
        }
        else
        {
            EmitExpression(ctx, bytes.Length);
            ctx.Builder.Emit(Opcode.ReadBytesDyn);
            ctx.Builder.EmitU16(fieldId);
        }
    }

    // ─── Pointer reads ───────────────────────────────────────────────

    private void EmitPtrRead(StructEmitContext ctx, PtrTypeRef ptr, ushort fieldId, FieldModifiers modifiers)
    {
        // Determine pointer width opcode
        var widthOpcode = GetPtrWidthOpcode(ptr);

        // Allocate a hidden field to store the raw pointer value
        ushort ptrFieldId = ctx.AllocateHiddenField($"_ptr_{fieldId}", _masterBuilder);

        // Read the raw pointer value (no JSON emission)
        ctx.Builder.Emit(widthOpcode);
        ctx.Builder.EmitU16(ptrFieldId);

        // Compute buffer offset: raw_ptr - base_ptr (for absolute) or raw_ptr + field_offset (for relative)
        ctx.Builder.Emit(Opcode.PushFieldVal);
        ctx.Builder.EmitU16(ptrFieldId);
        if (ptr.IsRelative)
        {
            // relptr: offset = field_position + raw_value
            ctx.Builder.Emit(Opcode.PushConstI64);
            ctx.Builder.EmitI64(0); // placeholder — we'd need field offset
            // For now, use runtime field offset
            ctx.Builder.Emit(Opcode.FnOffsetOf);
            ctx.Builder.EmitU16(ptrFieldId);
            ctx.Builder.Emit(Opcode.OpAdd);
        }
        else
        {
            // absolute ptr: offset = raw_ptr - base_ptr
            ushort basePtrNameIdx = _masterBuilder.InternString("base_ptr");
            ctx.Builder.Emit(Opcode.PushFileParam);
            ctx.Builder.EmitU16(basePtrNameIdx);
            ctx.Builder.Emit(Opcode.OpSub);
        }

        // Save position, seek to target, read inner type, restore
        ctx.Builder.Emit(Opcode.SeekPush);
        ctx.Builder.Emit(Opcode.SeekAbs);  // pops buffer_offset from eval stack
        EmitFieldRead(ctx, ptr.InnerType, fieldId, modifiers.Merge(ptr.InnerModifiers));
        ctx.Builder.Emit(Opcode.SeekPop);
    }

    private void EmitNullableRead(StructEmitContext ctx, NullableTypeRef nullable, ushort fieldId, FieldModifiers modifiers)
    {
        if (nullable.InnerType is PtrTypeRef ptr)
        {
            // Nullable pointer: read pointer, check for zero, branch
            var widthOpcode = GetPtrWidthOpcode(ptr);
            ushort ptrFieldId = ctx.AllocateHiddenField($"_ptr_{fieldId}", _masterBuilder);

            // Read the raw pointer value
            ctx.Builder.Emit(widthOpcode);
            ctx.Builder.EmitU16(ptrFieldId);

            // Check if null (value == 0)
            ctx.Builder.Emit(Opcode.PushFieldVal);
            ctx.Builder.EmitU16(ptrFieldId);
            ctx.Builder.Emit(Opcode.PushConstI64);
            ctx.Builder.EmitI64(0);
            ctx.Builder.Emit(Opcode.OpEq);
            ctx.Builder.Emit(Opcode.JumpIfFalse);
            int nonNullPatch = ctx.Builder.ReserveI32();

            // Null path: emit JSON null for this field
            EmitFieldNull(ctx, fieldId);
            ctx.Builder.Emit(Opcode.Jump);
            int endPatch = ctx.Builder.ReserveI32();

            // Non-null path: dereference
            ctx.Builder.PatchI32(nonNullPatch, ctx.Builder.Position);
            ctx.Builder.Emit(Opcode.PushFieldVal);
            ctx.Builder.EmitU16(ptrFieldId);
            if (ptr.IsRelative)
            {
                ctx.Builder.Emit(Opcode.FnOffsetOf);
                ctx.Builder.EmitU16(ptrFieldId);
                ctx.Builder.Emit(Opcode.OpAdd);
            }
            else
            {
                ushort basePtrNameIdx = _masterBuilder.InternString("base_ptr");
                ctx.Builder.Emit(Opcode.PushFileParam);
                ctx.Builder.EmitU16(basePtrNameIdx);
                ctx.Builder.Emit(Opcode.OpSub);
            }
            ctx.Builder.Emit(Opcode.SeekPush);
            ctx.Builder.Emit(Opcode.SeekAbs);
            EmitFieldRead(ctx, ptr.InnerType, fieldId, modifiers.Merge(ptr.InnerModifiers));
            ctx.Builder.Emit(Opcode.SeekPop);

            // End
            ctx.Builder.PatchI32(endPatch, ctx.Builder.Position);
        }
        else
        {
            // Nullable non-pointer: for now, just emit the inner type as-is
            EmitFieldRead(ctx, nullable.InnerType, fieldId, modifiers);
        }
    }

    private static Opcode GetPtrWidthOpcode(PtrTypeRef ptr)
    {
        if (ptr.Width is null)
            return Opcode.ReadPtrU64; // default width

        return ptr.Width.PrimitiveToken switch
        {
            TokenType.U32 or TokenType.U32Le => Opcode.ReadPtrU32,
            TokenType.U64 or TokenType.U64Le => Opcode.ReadPtrU64,
            _ => Opcode.ReadPtrU64,
        };
    }

    private void EmitFieldNull(StructEmitContext ctx, ushort fieldId)
    {
        ctx.Builder.Emit(Opcode.EmitNull);
        ctx.Builder.EmitU16(fieldId);
    }

    // ─── Named type reads (struct / enum / bits-struct) ───────────────

    private void EmitNamedTypeRead(StructEmitContext ctx, NamedTypeRef named, ushort fieldId)
    {
        if (_typeEnv.Enums.TryGetValue(named.Name, out var enumDecl))
        {
            EmitEnumRead(ctx, enumDecl, fieldId);
        }
        else if (_typeEnv.BitsStructs.TryGetValue(named.Name, out var bitsDecl))
        {
            EmitInlineBitsRead(ctx, bitsDecl, fieldId);
        }
        else if (_structIndexMap.TryGetValue(named.Name, out int structIdx))
        {
            EmitStructCall(ctx, named, fieldId, structIdx);
        }
    }

    private void EmitEnumRead(StructEmitContext ctx, EnumDecl enumDecl, ushort fieldId)
    {
        if (enumDecl.BaseType is PrimitiveTypeRef baseType)
        {
            EmitPrimitiveRead(ctx, baseType, fieldId);
        }
    }

    private void EmitInlineBitsRead(StructEmitContext ctx, BitsStructDecl bitsDecl, ushort fieldId)
    {
        ushort nameIdx = _masterBuilder.InternString(bitsDecl.Name);
        ctx.Builder.Emit(Opcode.EmitBitsBegin);
        ctx.Builder.EmitU16(nameIdx);
        ctx.Builder.EmitU16(fieldId);

        // Read the base type value.
        if (bitsDecl.BaseType is PrimitiveTypeRef baseType)
        {
            var opcode = ResolvePrimitiveOpcode(baseType.PrimitiveToken);
            ctx.Builder.Emit(opcode);
            ctx.Builder.EmitU16(fieldId);
        }

        // Emit bit field reads.
        for (int i = 0; i < bitsDecl.Fields.Count; i++)
        {
            var bitField = bitsDecl.Fields[i];
            ushort bitFieldId = ctx.AllocateField(bitField.Name, new FieldModifiers(), _masterBuilder);
            switch (bitField.Type)
            {
                case SingleBit:
                    ctx.Builder.Emit(Opcode.ReadBit);
                    ctx.Builder.EmitU16(bitFieldId);
                    break;
                case MultiBits mb:
                    ctx.Builder.Emit(Opcode.ReadBits);
                    ctx.Builder.EmitU16(bitFieldId);
                    ctx.Builder.EmitU8((byte)mb.Count);
                    break;
            }
        }

        ctx.Builder.Emit(Opcode.EmitBitsEnd);
    }

    private void EmitStructCall(StructEmitContext ctx, NamedTypeRef named, ushort fieldId, int structIdx)
    {
        ushort nameIdx = _masterBuilder.InternString(named.Name);
        ctx.Builder.Emit(Opcode.EmitStructBegin);
        ctx.Builder.EmitU16(nameIdx);
        ctx.Builder.EmitU16(fieldId);

        // Push arguments for the struct call.
        for (int i = 0; i < named.Arguments.Count; i++)
        {
            EmitExpression(ctx, named.Arguments[i]);
        }

        // Forward array stores for arguments that carry searchable arrays.
        // This must happen after arg pushes but before CallStruct.
        for (int i = 0; i < named.Arguments.Count; i++)
        {
            if (named.Arguments[i] is IdentifierExpr argId)
            {
                if (ctx.FieldIndices.TryGetValue(argId.Name, out ushort argFieldId))
                {
                    // Forward the field's array store (if any) to child param i
                    ctx.Builder.Emit(Opcode.ForwardArrayStore);
                    ctx.Builder.EmitU16(argFieldId);
                    ctx.Builder.EmitU16((ushort)i);
                }
                else if (ctx.ParamIndices.TryGetValue(argId.Name, out ushort argParamIdx))
                {
                    // Transitively forward a received param's array store
                    ctx.Builder.Emit(Opcode.ForwardParamArrayStore);
                    ctx.Builder.EmitU16(argParamIdx);
                    ctx.Builder.EmitU16((ushort)i);
                }
            }
        }

        ctx.Builder.Emit(Opcode.CallStruct);
        ctx.Builder.EmitU16((ushort)structIdx);
        ctx.Builder.EmitU8((byte)named.Arguments.Count);

        // Emit CopyChildField for any dotted paths referencing this child's fields.
        string parentFieldName = ctx.GetFieldName(fieldId);
        string prefix = parentFieldName + ".";
        foreach (var kv in ctx.FieldIndices)
        {
            if (kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            {
                string childFieldName = kv.Key.Substring(prefix.Length);
                ushort childNameIdx = _masterBuilder.InternString(childFieldName);
                ctx.Builder.Emit(Opcode.CopyChildField);
                ctx.Builder.EmitU16(childNameIdx);
                ctx.Builder.EmitU16(kv.Value);
            }
        }

        ctx.Builder.Emit(Opcode.EmitStructEnd);
    }

    // ─── Match type reads ─────────────────────────────────────────────

    private void EmitMatchTypeRead(StructEmitContext ctx, MatchTypeRef matchType, ushort fieldId, FieldModifiers modifiers)
    {
        var match = matchType.Match;

        // Evaluate discriminant and push on stack.
        EmitExpression(ctx, match.Discriminant);
        ctx.Builder.Emit(Opcode.MatchBegin);

        var armEndPatches = new List<int>();

        for (int i = 0; i < match.Arms.Count; i++)
        {
            var arm = match.Arms[i];
            int skipArmPatch;

            switch (arm.Pattern)
            {
                case ValuePattern vp:
                    ctx.Builder.Emit(Opcode.MatchArmEq);
                    EmitInlineValue(ctx, vp.Value);
                    skipArmPatch = ctx.Builder.ReserveI32();
                    break;
                case RangePattern rp:
                    ctx.Builder.Emit(Opcode.MatchArmRange);
                    EmitInlineValue(ctx, rp.Low);
                    EmitInlineValue(ctx, rp.High);
                    skipArmPatch = ctx.Builder.ReserveI32();
                    break;
                case WildcardPattern:
                    ctx.Builder.Emit(Opcode.MatchDefault);
                    skipArmPatch = ctx.Builder.ReserveI32();
                    break;
                case IdentifierPattern ip:
                    if (arm.Guard is not null)
                    {
                        ctx.Builder.Emit(Opcode.MatchArmGuard);
                        skipArmPatch = ctx.Builder.ReserveI32();
                    }
                    else
                    {
                        ctx.Builder.Emit(Opcode.MatchDefault);
                        skipArmPatch = ctx.Builder.ReserveI32();
                    }
                    break;
                default:
                    continue;
            }

            // Emit the type read for this arm (with optional array wrapping).
            if (arm.Array is not null)
            {
                string parentFieldName = ctx.GetFieldName(fieldId);
                EmitArrayLoop(ctx, parentFieldName, fieldId, arm.Type, arm.Array, modifiers,
                    ctx.SearchedArrayNames.Contains(parentFieldName));
            }
            else
            {
                EmitFieldRead(ctx, arm.Type, fieldId, modifiers);
            }

            // Jump to end of match after arm body.
            ctx.Builder.Emit(Opcode.Jump);
            int endPatch = ctx.Builder.ReserveI32();
            armEndPatches.Add(endPatch);

            // Patch the arm skip to jump here (past the arm body).
            ctx.Builder.PatchI32(skipArmPatch, ctx.Builder.Position);
        }

        // Patch all arm-end jumps to here.
        for (int i = 0; i < armEndPatches.Count; i++)
        {
            ctx.Builder.PatchI32(armEndPatches[i], ctx.Builder.Position);
        }

        ctx.Builder.Emit(Opcode.MatchEnd);
    }

    // ─── Array emission ───────────────────────────────────────────────

    private void EmitArrayField(StructEmitContext ctx, FieldDecl field, ushort fieldId)
    {
        EmitArrayLoop(ctx, field.Name, fieldId, field.Type, field.Array!, field.Modifiers,
            ctx.SearchedArrayNames.Contains(field.Name));
    }

    /// <summary>
    /// Shared array loop emission used by both regular array fields and match arm arrays.
    /// </summary>
    private void EmitArrayLoop(StructEmitContext ctx, string fieldName, ushort fieldId,
        TypeReference elemType, ArraySpec arraySpec, FieldModifiers modifiers, bool isSearchTarget)
    {
        ushort nameIdx = _masterBuilder.InternString(fieldName);
        ctx.Builder.Emit(Opcode.EmitArrayBegin);
        ctx.Builder.EmitU16(nameIdx);
        ctx.Builder.EmitU16(fieldId);

        switch (arraySpec)
        {
            case CountArraySpec countArr:
                EmitExpression(ctx, countArr.Count);
                ctx.Builder.Emit(Opcode.ArrayBeginCount);
                break;
            case UntilArraySpec:
                ctx.Builder.Emit(Opcode.ArrayBeginUntil);
                break;
            case SentinelArraySpec:
                ctx.Builder.Emit(Opcode.ArrayBeginSentinel);
                break;
            case GreedyArraySpec:
                ctx.Builder.Emit(Opcode.ArrayBeginGreedy);
                break;
        }

        int loopTop = ctx.Builder.Position;

        // For sentinel arrays, save emitter checkpoint before each element read.
        if (arraySpec is SentinelArraySpec)
            ctx.Builder.Emit(Opcode.SentinelSave);

        // Emit the field body (the read for one element).
        EmitFieldRead(ctx, elemType, fieldId, modifiers);

        // For sentinel arrays, evaluate the predicate BEFORE post-processing.
        // If the element is the sentinel, rollback the emitter and break.
        if (arraySpec is SentinelArraySpec sentinel)
        {
            var substituted = SubstituteSentinelParam(sentinel.Predicate, sentinel.ParamName, fieldName);
            EmitExpression(ctx, substituted);
            ctx.Builder.Emit(Opcode.SentinelCheck);
        }

        // If this array is a target of .find()/.any()/.all(), snapshot element field tables.
        if (isSearchTarget)
        {
            ctx.Builder.Emit(Opcode.ArrayStoreElem);
            ctx.Builder.EmitU16(fieldId);
        }

        ctx.Builder.Emit(Opcode.ArrayNext);

        // For until arrays, emit the condition check.
        if (arraySpec is UntilArraySpec untilSpec)
        {
            EmitExpression(ctx, untilSpec.Condition);
        }

        ctx.Builder.Emit(Opcode.ArrayEnd);

        ctx.Builder.Emit(Opcode.EmitArrayEnd);
    }

    // ─── Let binding ──────────────────────────────────────────────────

    private void EmitLetBinding(StructEmitContext ctx, LetBinding let)
    {
        ushort fieldId = ctx.AllocateField(let.Name, new FieldModifiers { IsHidden = true }, _masterBuilder);

        // Special handling for .find()/.find_or() — result is a struct projection, not a scalar.
        if (let.Value is MethodCallExpr mc && mc.MethodName is "find" or "find_or")
        {
            EmitArraySearchLetBinding(ctx, let.Name, fieldId, mc);
            return;
        }

        EmitExpression(ctx, let.Value);
        // Store the computed value as a hidden field.
        ctx.Builder.Emit(Opcode.StoreFieldVal);
        ctx.Builder.EmitU16(fieldId);
    }

    /// <summary>
    /// Emits a .find()/.find_or() search loop for a let-binding.
    /// Copies matched element fields to pre-allocated hidden field IDs.
    /// </summary>
    private void EmitArraySearchLetBinding(StructEmitContext ctx, string letName, ushort fieldId, MethodCallExpr mc)
    {
        string arrayName = ((IdentifierExpr)mc.Object).Name;

        byte mode = (byte)(mc.MethodName == "find" ? 0 : 1);

        // Emit the appropriate search-begin opcode depending on whether the
        // array is a local field or a struct parameter.
        if (ctx.FieldIndices.TryGetValue(arrayName, out ushort arrayFieldId))
        {
            ctx.Builder.Emit(Opcode.ArraySearchBegin);
            ctx.Builder.EmitU16(arrayFieldId);
            ctx.Builder.EmitU8(mode);
        }
        else if (ctx.ParamIndices.TryGetValue(arrayName, out ushort paramIdx))
        {
            ctx.Builder.Emit(Opcode.ArraySearchBeginParam);
            ctx.Builder.EmitU16(paramIdx);
            ctx.Builder.EmitU8(mode);
        }
        else
        {
            throw new InvalidOperationException(
                $"Array '{arrayName}' for .{mc.MethodName}() is not a field or parameter in current scope.");
        }

        int loopStart = ctx.Builder.Position;

        // Compile predicate lambda body
        var lambda = (LambdaExpr)mc.Args[0];
        var savedParam = _currentSearchLambdaParam;
        _currentSearchLambdaParam = lambda.Parameter;
        EmitExpression(ctx, lambda.Body);
        _currentSearchLambdaParam = savedParam;

        // ArraySearchCheck: loopTarget + notFoundTarget
        ctx.Builder.Emit(Opcode.ArraySearchCheck);
        ctx.Builder.EmitI32(loopStart);
        int notFoundPatch = ctx.Builder.ReserveI32();

        // ─── Found path: copy matched element fields to pre-allocated hidden fields ───
        foreach (var kvp in ctx.FieldIndices)
        {
            if (kvp.Key.StartsWith(letName + "."))
            {
                string childFieldName = kvp.Key.Substring(letName.Length + 1);
                ctx.Builder.Emit(Opcode.ArraySearchCopy);
                ctx.Builder.EmitU16(_masterBuilder.InternString(childFieldName));
                ctx.Builder.EmitU16(kvp.Value);
            }
        }

        ctx.Builder.Emit(Opcode.Jump);
        int endPatch = ctx.Builder.ReserveI32();

        // ─── Not-found path ───
        ctx.Builder.PatchI32(notFoundPatch, ctx.Builder.Position);
        if (mode == 1 && mc.Args.Count > 1)
        {
            // find_or: compile default expression
            EmitExpression(ctx, mc.Args[1]);
            ctx.Builder.Emit(Opcode.StoreFieldVal);
            ctx.Builder.EmitU16(fieldId);
        }
        // For find (mode 0): ArraySearchEnd will throw if no match

        ctx.Builder.PatchI32(endPatch, ctx.Builder.Position);
        ctx.Builder.Emit(Opcode.ArraySearchEnd);
    }

    // ─── Block expression (@map body) ─────────────────────────────────

    /// <summary>
    /// Emits a block expression: processes @let bindings (including .find() searches),
    /// then emits the result expression which leaves a value on the stack.
    /// Used for inlined @map bodies.
    /// </summary>
    private void EmitBlockExpr(StructEmitContext ctx, BlockExpr block)
    {
        // Process each let binding.
        foreach (var binding in block.Bindings)
        {
            ushort fieldId = ctx.AllocateField(binding.Name, new FieldModifiers { IsHidden = true }, _masterBuilder);

            // Special handling for .find()/.find_or() — result is a struct projection.
            if (binding.Value is MethodCallExpr mc && mc.MethodName is "find" or "find_or")
            {
                EmitArraySearchLetBinding(ctx, binding.Name, fieldId, mc);
                continue;
            }

            EmitExpression(ctx, binding.Value);
            ctx.Builder.Emit(Opcode.StoreFieldVal);
            ctx.Builder.EmitU16(fieldId);
        }

        // Emit the result expression — leaves a value on the stack.
        EmitExpression(ctx, block.Result);
    }

    // ─── Align ────────────────────────────────────────────────────────

    private void EmitAlign(StructEmitContext ctx, AlignDirective align)
    {
        if (TryEvalConstant(align.Alignment, out long val))
        {
            ctx.Builder.Emit(Opcode.AlignFixed);
            ctx.Builder.EmitU16((ushort)val);
        }
        else
        {
            EmitExpression(ctx, align.Alignment);
            ctx.Builder.Emit(Opcode.Align);
        }
    }

    // ─── Assert directive ─────────────────────────────────────────────

    private void EmitAssertDirective(StructEmitContext ctx, AssertDirective assert)
    {
        EmitExpression(ctx, assert.Condition);
        ctx.Builder.Emit(Opcode.JumpIfTrue);
        int patch = ctx.Builder.ReserveI32();
        // If condition was false, we'd normally halt. Emit a PushConstStr with the message
        // so the runtime can report it.
        ushort msgIdx = _masterBuilder.InternString(assert.Message);
        ctx.Builder.Emit(Opcode.PushConstStr);
        ctx.Builder.EmitU16(msgIdx);
        ctx.Builder.PatchI32(patch, ctx.Builder.Position);
    }

    // ─── Pass 2b: Emit bits struct ────────────────────────────────────

    private void EmitBitsStruct(StructEmitContext ctx)
    {
        var decl = ctx.BitsDecl!;
        var builder = ctx.Builder;

        ushort nameIdx = ctx.NameIndex;
        builder.Emit(Opcode.EmitBitsBegin);
        builder.EmitU16(nameIdx);
        builder.EmitU16(0); // self field_id

        // Read the base type.
        if (decl.BaseType is PrimitiveTypeRef baseType)
        {
            ushort baseFieldId = ctx.AllocateField("_base", new FieldModifiers { IsHidden = true }, _masterBuilder);
            var opcode = ResolvePrimitiveOpcode(baseType.PrimitiveToken);
            builder.Emit(opcode);
            builder.EmitU16(baseFieldId);
        }

        // Emit each bit field.
        for (int i = 0; i < decl.Fields.Count; i++)
        {
            var bitField = decl.Fields[i];
            ushort fieldId = ctx.AllocateField(bitField.Name, new FieldModifiers(), _masterBuilder);
            switch (bitField.Type)
            {
                case SingleBit:
                    builder.Emit(Opcode.ReadBit);
                    builder.EmitU16(fieldId);
                    break;
                case MultiBits mb:
                    builder.Emit(Opcode.ReadBits);
                    builder.EmitU16(fieldId);
                    builder.EmitU8((byte)mb.Count);
                    break;
            }
        }

        builder.Emit(Opcode.EmitBitsEnd);
        builder.Emit(Opcode.Return);
    }

    // ─── Expression codegen ───────────────────────────────────────────

    private void EmitExpression(StructEmitContext ctx, Expression expr)
    {
        switch (expr)
        {
            case IntLiteralExpr intLit:
                ctx.Builder.Emit(Opcode.PushConstI64);
                ctx.Builder.EmitI64(intLit.Value);
                break;

            case StringLiteralExpr strLit:
                ctx.Builder.Emit(Opcode.PushConstStr);
                ctx.Builder.EmitU16(_masterBuilder.InternString(strLit.Value));
                break;

            case BoolLiteralExpr boolLit:
                ctx.Builder.Emit(Opcode.PushConstI64);
                ctx.Builder.EmitI64(boolLit.Value ? 1 : 0);
                break;

            case IdentifierExpr id:
                EmitIdentifier(ctx, id);
                break;

            case FieldAccessExpr fa:
                EmitFieldAccess(ctx, fa);
                break;

            case IndexAccessExpr ia:
                EmitExpression(ctx, ia.Object);
                EmitExpression(ctx, ia.Index);
                break;

            case BinaryExpr bin:
                EmitExpression(ctx, bin.Left);
                EmitExpression(ctx, bin.Right);
                EmitBinaryOp(ctx, bin.Op);
                break;

            case UnaryExpr un:
                EmitExpression(ctx, un.Operand);
                EmitUnaryOp(ctx, un.Op);
                break;

            case FunctionCallExpr fn:
                EmitFunctionCall(ctx, fn);
                break;

            case MethodCallExpr mc:
                EmitMethodCall(ctx, mc);
                break;

            case MatchExpr matchExpr:
                // Inline match expression in expression context.
                EmitExpression(ctx, matchExpr.Discriminant);
                ctx.Builder.Emit(Opcode.MatchBegin);
                ctx.Builder.Emit(Opcode.MatchEnd);
                break;

            case BlockExpr block:
                EmitBlockExpr(ctx, block);
                break;
        }
    }

    private void EmitIdentifier(StructEmitContext ctx, IdentifierExpr id)
    {
        // Check compile-time constants.
        if (_typeEnv.Constants.TryGetValue(id.Name, out var constDecl))
        {
            if (TryEvalConstant(constDecl.Value, out long val))
            {
                ctx.Builder.Emit(Opcode.PushConstI64);
                ctx.Builder.EmitI64(val);
                return;
            }
        }

        // Check struct parameters.
        if (ctx.ParamIndices.TryGetValue(id.Name, out ushort paramIdx))
        {
            ctx.Builder.Emit(Opcode.PushParam);
            ctx.Builder.EmitU16(paramIdx);
            return;
        }

        // Check file-level @param declarations.
        if (_fileParamNames.Contains(id.Name))
        {
            ushort nameIdx = _masterBuilder.InternString(id.Name);
            ctx.Builder.Emit(Opcode.PushFileParam);
            ctx.Builder.EmitU16(nameIdx);
            return;
        }

        // Check runtime pseudo-variables.
        switch (id.Name)
        {
            case "@input_size":
                ctx.Builder.Emit(Opcode.PushRuntimeVar);
                ctx.Builder.EmitU8((byte)RuntimeVar.InputSize);
                return;
            case "@offset":
                ctx.Builder.Emit(Opcode.PushRuntimeVar);
                ctx.Builder.EmitU8((byte)RuntimeVar.Offset);
                return;
            case "@remaining":
                ctx.Builder.Emit(Opcode.PushRuntimeVar);
                ctx.Builder.EmitU8((byte)RuntimeVar.Remaining);
                return;
            case "_index":
                ctx.Builder.Emit(Opcode.PushIndex);
                return;
        }

        // Otherwise it's a field reference.
        if (ctx.FieldIndices.TryGetValue(id.Name, out ushort fid))
        {
            ctx.Builder.Emit(Opcode.PushFieldVal);
            ctx.Builder.EmitU16(fid);
        }
        else
        {
            // Unknown identifier — emit as field reference with interned name.
            ushort nameIdx = _masterBuilder.InternString(id.Name);
            ctx.Builder.Emit(Opcode.PushFieldVal);
            ctx.Builder.EmitU16(nameIdx);
        }
    }

    private void EmitFieldAccess(StructEmitContext ctx, FieldAccessExpr fa)
    {
        // When inside a search lambda, s.field_name → PushElemField
        if (_currentSearchLambdaParam != null && fa.Object is IdentifierExpr root
            && root.Name == _currentSearchLambdaParam)
        {
            ctx.Builder.Emit(Opcode.PushElemField);
            ctx.Builder.EmitU16(_masterBuilder.InternString(fa.FieldName));
            return;
        }

        // Flatten to a dotted path and check if it was pre-allocated.
        string path = FlattenFieldPath(fa);
        if (ctx.FieldIndices.TryGetValue(path, out ushort fid))
        {
            ctx.Builder.Emit(Opcode.PushFieldVal);
            ctx.Builder.EmitU16(fid);
        }
        else if (TryResolveEnumValue(fa, out long enumVal))
        {
            ctx.Builder.Emit(Opcode.PushConstI64);
            ctx.Builder.EmitI64(enumVal);
        }
        else
        {
            // Fallback for unresolved paths — will likely return zero at runtime.
            ushort nameIdx = _masterBuilder.InternString(path);
            ctx.Builder.Emit(Opcode.PushFieldVal);
            ctx.Builder.EmitU16(nameIdx);
        }
    }

    private static string FlattenFieldPath(Expression expr)
    {
        return expr switch
        {
            FieldAccessExpr fa => FlattenFieldPath(fa.Object) + "." + fa.FieldName,
            IdentifierExpr id => id.Name,
            _ => "?",
        };
    }

    // ─── @map inlining ───────────────────────────────────────────────────

    /// <summary>
    /// Returns the inlined (rewritten) expression for a @map call, using a memoized
    /// cache so that pre-scan passes and emission see the same mangled names.
    /// </summary>
    private Expression GetOrInlineMapCall(FunctionCallExpr fn, MapDecl map)
    {
        if (!_inlinedMapCalls.TryGetValue(fn.Span, out var inlined))
        {
            inlined = InlineMapCall(map, fn.Args, _mapCallSiteCounter++);
            _inlinedMapCalls[fn.Span] = inlined;
        }
        return inlined;
    }

    /// <summary>
    /// Creates a rewritten copy of a @map body with:
    /// - Parameter names substituted with actual argument expressions
    /// - Block-local @let binding names mangled to avoid collision across call sites
    /// </summary>
    private static Expression InlineMapCall(MapDecl map, IReadOnlyList<Expression> args, int callsiteId)
    {
        // Build param → arg substitution map.
        var paramSubst = new Dictionary<string, Expression>();
        for (int i = 0; i < map.Params.Count && i < args.Count; i++)
            paramSubst[map.Params[i].Name] = args[i];

        if (map.Body is BlockExpr block)
        {
            // Mangle internal let binding names to avoid collision.
            string prefix = $"__map_{callsiteId}_";
            var letNameMap = new Dictionary<string, string>();
            foreach (var binding in block.Bindings)
                letNameMap[binding.Name] = prefix + binding.Name;

            // Rewrite bindings.
            var newBindings = new List<MapLetBinding>(block.Bindings.Count);
            foreach (var binding in block.Bindings)
            {
                var newValue = SubstituteExpr(binding.Value, paramSubst, letNameMap);
                newBindings.Add(new MapLetBinding(letNameMap[binding.Name], newValue, binding.Span));
            }

            // Rewrite result expression.
            var newResult = SubstituteExpr(block.Result, paramSubst, letNameMap);
            return new BlockExpr(newBindings, newResult, block.Span);
        }
        else
        {
            // Simple expression body — just substitute parameters.
            return SubstituteExpr(map.Body, paramSubst, new Dictionary<string, string>());
        }
    }

    /// <summary>
    /// Recursively substitutes @map parameter references and let-binding name references
    /// in an expression. Lambda parameters shadow both maps.
    /// </summary>
    private static Expression SubstituteExpr(
        Expression expr,
        Dictionary<string, Expression> paramSubst,
        Dictionary<string, string> letNameMap)
    {
        switch (expr)
        {
            case IdentifierExpr id:
                if (paramSubst.TryGetValue(id.Name, out var replacement))
                    return replacement;
                if (letNameMap.TryGetValue(id.Name, out var mangledName))
                    return new IdentifierExpr(mangledName, id.Span);
                return expr;

            case FieldAccessExpr fa:
                return new FieldAccessExpr(
                    SubstituteExpr(fa.Object, paramSubst, letNameMap),
                    fa.FieldName, fa.Span);

            case IndexAccessExpr ia:
                return new IndexAccessExpr(
                    SubstituteExpr(ia.Object, paramSubst, letNameMap),
                    SubstituteExpr(ia.Index, paramSubst, letNameMap),
                    ia.Span);

            case BinaryExpr bin:
                return new BinaryExpr(
                    SubstituteExpr(bin.Left, paramSubst, letNameMap),
                    bin.Op,
                    SubstituteExpr(bin.Right, paramSubst, letNameMap),
                    bin.Span);

            case UnaryExpr un:
                return new UnaryExpr(
                    un.Op,
                    SubstituteExpr(un.Operand, paramSubst, letNameMap),
                    un.Span);

            case FunctionCallExpr fn:
                return new FunctionCallExpr(
                    fn.FunctionName,
                    fn.Args.Select(a => SubstituteExpr(a, paramSubst, letNameMap)).ToList(),
                    fn.Span);

            case MethodCallExpr mc:
                return new MethodCallExpr(
                    SubstituteExpr(mc.Object, paramSubst, letNameMap),
                    mc.MethodName,
                    mc.Args.Select(a => SubstituteExpr(a, paramSubst, letNameMap)).ToList(),
                    mc.Span);

            case LambdaExpr lambda:
            {
                // Lambda parameter shadows param/let names.
                var innerParamSubst = new Dictionary<string, Expression>(paramSubst);
                innerParamSubst.Remove(lambda.Parameter);
                var innerLetMap = new Dictionary<string, string>(letNameMap);
                innerLetMap.Remove(lambda.Parameter);
                return new LambdaExpr(
                    lambda.Parameter,
                    SubstituteExpr(lambda.Body, innerParamSubst, innerLetMap),
                    lambda.Span);
            }

            case BlockExpr block:
            {
                var newBindings = block.Bindings.Select(b =>
                    new MapLetBinding(b.Name,
                        SubstituteExpr(b.Value, paramSubst, letNameMap),
                        b.Span)).ToList();
                var newResult = SubstituteExpr(block.Result, paramSubst, letNameMap);
                return new BlockExpr(newBindings, newResult, block.Span);
            }

            // Literals and null pass through unchanged.
            default:
                return expr;
        }
    }

    private void EmitBinaryOp(StructEmitContext ctx, BinaryOp op)
    {
        var opcode = op switch
        {
            BinaryOp.Add => Opcode.OpAdd,
            BinaryOp.Sub => Opcode.OpSub,
            BinaryOp.Mul => Opcode.OpMul,
            BinaryOp.Div => Opcode.OpDiv,
            BinaryOp.Mod => Opcode.OpMod,
            BinaryOp.BitAnd => Opcode.OpAnd,
            BinaryOp.BitOr => Opcode.OpOr,
            BinaryOp.BitXor => Opcode.OpXor,
            BinaryOp.Shl => Opcode.OpShl,
            BinaryOp.Shr => Opcode.OpShr,
            BinaryOp.Eq => Opcode.OpEq,
            BinaryOp.Ne => Opcode.OpNe,
            BinaryOp.Lt => Opcode.OpLt,
            BinaryOp.Gt => Opcode.OpGt,
            BinaryOp.Le => Opcode.OpLe,
            BinaryOp.Ge => Opcode.OpGe,
            BinaryOp.LogicAnd => Opcode.OpLogicalAnd,
            BinaryOp.LogicOr => Opcode.OpLogicalOr,
            _ => throw new InvalidOperationException($"Unknown binary op: {op}"),
        };
        ctx.Builder.Emit(opcode);
    }

    private void EmitUnaryOp(StructEmitContext ctx, UnaryOp op)
    {
        var opcode = op switch
        {
            UnaryOp.Negate => Opcode.OpNeg,
            UnaryOp.BitNot => Opcode.OpNot,
            UnaryOp.LogicNot => Opcode.OpLogicalNot,
            _ => throw new InvalidOperationException($"Unknown unary op: {op}"),
        };
        ctx.Builder.Emit(opcode);
    }

    private void EmitFunctionCall(StructEmitContext ctx, FunctionCallExpr fn)
    {
        // @map calls take priority over built-in functions.
        if (_typeEnv.Maps.TryGetValue(fn.FunctionName, out var mapDecl))
        {
            var inlined = GetOrInlineMapCall(fn, mapDecl);
            EmitExpression(ctx, inlined);
            return;
        }

        switch (fn.FunctionName)
        {
            // Runtime pseudo-variables (parsed as zero-arg function calls).
            case "remaining":
                ctx.Builder.Emit(Opcode.PushRuntimeVar);
                ctx.Builder.EmitU8((byte)RuntimeVar.Remaining);
                break;
            case "offset":
                ctx.Builder.Emit(Opcode.PushRuntimeVar);
                ctx.Builder.EmitU8((byte)RuntimeVar.Offset);
                break;
            case "input_size":
                ctx.Builder.Emit(Opcode.PushRuntimeVar);
                ctx.Builder.EmitU8((byte)RuntimeVar.InputSize);
                break;

            case "sizeof":
                if (fn.Args.Count > 0 && fn.Args[0] is IdentifierExpr sizeId)
                {
                    ushort fid = ResolveFieldId(ctx, sizeId.Name);
                    ctx.Builder.Emit(Opcode.FnSizeOf);
                    ctx.Builder.EmitU16(fid);
                }
                break;
            case "offset_of":
                if (fn.Args.Count > 0 && fn.Args[0] is IdentifierExpr offId)
                {
                    ushort fid = ResolveFieldId(ctx, offId.Name);
                    ctx.Builder.Emit(Opcode.FnOffsetOf);
                    ctx.Builder.EmitU16(fid);
                }
                break;
            case "count":
                if (fn.Args.Count > 0 && fn.Args[0] is IdentifierExpr cntId)
                {
                    ushort fid = ResolveFieldId(ctx, cntId.Name);
                    ctx.Builder.Emit(Opcode.FnCount);
                    ctx.Builder.EmitU16(fid);
                }
                break;
            case "strlen":
                if (fn.Args.Count > 0 && fn.Args[0] is IdentifierExpr strId)
                {
                    ushort fid = ResolveFieldId(ctx, strId.Name);
                    ctx.Builder.Emit(Opcode.FnStrLen);
                    ctx.Builder.EmitU16(fid);
                }
                break;
            case "crc32":
                for (int i = 0; i < fn.Args.Count; i++)
                    EmitExpression(ctx, fn.Args[i]);
                ctx.Builder.Emit(Opcode.FnCrc32);
                ctx.Builder.EmitU8((byte)fn.Args.Count);
                break;
            case "adler32":
                for (int i = 0; i < fn.Args.Count; i++)
                    EmitExpression(ctx, fn.Args[i]);
                ctx.Builder.Emit(Opcode.FnAdler32);
                ctx.Builder.EmitU8((byte)fn.Args.Count);
                break;
            default:
                // Emit arguments for unknown functions.
                for (int i = 0; i < fn.Args.Count; i++)
                    EmitExpression(ctx, fn.Args[i]);
                break;
        }
    }

    private void EmitMethodCall(StructEmitContext ctx, MethodCallExpr mc)
    {
        // Array search methods need special handling
        if (mc.MethodName is "find" or "find_or" or "any" or "all")
        {
            EmitArraySearchExpression(ctx, mc);
            return;
        }

        EmitExpression(ctx, mc.Object);
        for (int i = 0; i < mc.Args.Count; i++)
            EmitExpression(ctx, mc.Args[i]);

        switch (mc.MethodName)
        {
            case "starts_with":
                ctx.Builder.Emit(Opcode.StrStartsWith);
                break;
            case "ends_with":
                ctx.Builder.Emit(Opcode.StrEndsWith);
                break;
            case "contains":
                ctx.Builder.Emit(Opcode.StrContains);
                break;
        }
    }

    /// <summary>
    /// Emits an array search expression (.any()/.all()) that pushes a bool onto the eval stack.
    /// Also handles .find()/.find_or() when used in expression context (not let-binding).
    /// </summary>
    private void EmitArraySearchExpression(StructEmitContext ctx, MethodCallExpr mc)
    {
        string arrayName = ((IdentifierExpr)mc.Object).Name;
        ushort arrayFieldId = ctx.FieldIndices[arrayName];

        byte mode = mc.MethodName switch
        {
            "find" => 0,
            "find_or" => 1,
            "any" => 2,
            "all" => 3,
            _ => 0,
        };

        ctx.Builder.Emit(Opcode.ArraySearchBegin);
        ctx.Builder.EmitU16(arrayFieldId);
        ctx.Builder.EmitU8(mode);

        int loopStart = ctx.Builder.Position;

        var lambda = (LambdaExpr)mc.Args[0];
        var savedParam = _currentSearchLambdaParam;
        _currentSearchLambdaParam = lambda.Parameter;
        EmitExpression(ctx, lambda.Body);
        _currentSearchLambdaParam = savedParam;

        ctx.Builder.Emit(Opcode.ArraySearchCheck);
        ctx.Builder.EmitI32(loopStart);
        int notFoundPatch = ctx.Builder.ReserveI32();

        // Found path: push true for any, false for all (early exit)
        if (mode == 2) // any → found match → true
        {
            ctx.Builder.Emit(Opcode.PushConstI64);
            ctx.Builder.EmitI64(1);
        }
        else if (mode == 3) // all → found non-match → false
        {
            ctx.Builder.Emit(Opcode.PushConstI64);
            ctx.Builder.EmitI64(0);
        }

        ctx.Builder.Emit(Opcode.Jump);
        int endPatch = ctx.Builder.ReserveI32();

        // Not-found path: push false for any, true for all (exhausted)
        ctx.Builder.PatchI32(notFoundPatch, ctx.Builder.Position);
        if (mode == 2) // any → no match → false
        {
            ctx.Builder.Emit(Opcode.PushConstI64);
            ctx.Builder.EmitI64(0);
        }
        else if (mode == 3) // all → all matched → true
        {
            ctx.Builder.Emit(Opcode.PushConstI64);
            ctx.Builder.EmitI64(1);
        }

        ctx.Builder.PatchI32(endPatch, ctx.Builder.Position);
        ctx.Builder.Emit(Opcode.ArraySearchEnd);
    }

    private ushort ResolveFieldId(StructEmitContext ctx, string name)
    {
        if (ctx.FieldIndices.TryGetValue(name, out ushort fid))
            return fid;
        return _masterBuilder.InternString(name);
    }

    // ─── Constant evaluation ──────────────────────────────────────────

    private bool TryEvalConstant(Expression expr, out long value)
    {
        switch (expr)
        {
            case IntLiteralExpr intLit:
                value = intLit.Value;
                return true;
            case IdentifierExpr id when _typeEnv.Constants.TryGetValue(id.Name, out var c):
                return TryEvalConstant(c.Value, out value);
            case BinaryExpr bin when TryEvalConstant(bin.Left, out long l) && TryEvalConstant(bin.Right, out long r):
                value = bin.Op switch
                {
                    BinaryOp.Add => l + r,
                    BinaryOp.Sub => l - r,
                    BinaryOp.Mul => l * r,
                    BinaryOp.Div when r != 0 => l / r,
                    BinaryOp.Mod when r != 0 => l % r,
                    _ => 0,
                };
                return true;
            default:
                value = 0;
                return false;
        }
    }

    // ─── Static size calculation ──────────────────────────────────────

    private int ComputeStaticSize(StructEmitContext ctx)
    {
        if (ctx.IsBitsStruct)
        {
            return ctx.BitsDecl!.BaseType is PrimitiveTypeRef p ? PrimitiveSize(p.PrimitiveToken) : -1;
        }

        var decl = ctx.Decl!;
        int total = 0;
        for (int i = 0; i < decl.Members.Count; i++)
        {
            int sz = MemberStaticSize(decl.Members[i]);
            if (sz < 0) return -1;
            total += sz;
        }
        return total;
    }

    private int MemberStaticSize(MemberDecl member)
    {
        switch (member)
        {
            case FieldDecl field:
                if (field.Array is not null)
                {
                    if (field.Array is CountArraySpec cas && TryEvalConstant(cas.Count, out long count))
                    {
                        int elemSize = TypeStaticSize(field.Type);
                        if (elemSize >= 0)
                            return (int)(count * elemSize);
                    }
                    return -1;
                }
                return TypeStaticSize(field.Type);
            case SkipDirective skip:
                return skip.ByteCount;
            case AlignDirective:
                return -1; // alignment is position-dependent
            case SeekDirective:
            case AtBlock:
                return -1;
            case LetBinding:
                return 0;
            case AssertDirective:
                return 0;
            default:
                return -1;
        }
    }

    private int TypeStaticSize(TypeReference type)
    {
        switch (type)
        {
            case PrimitiveTypeRef prim:
                return PrimitiveSize(prim.PrimitiveToken);
            case FixedStringTypeRef fs:
                return TryEvalConstant(fs.Length, out long fsLen) ? (int)fsLen : -1;
            case BytesTypeRef bytes:
                return TryEvalConstant(bytes.Length, out long bLen) ? (int)bLen : -1;
            case NamedTypeRef named:
                if (_typeEnv.Enums.TryGetValue(named.Name, out var e) && e.BaseType is PrimitiveTypeRef ep)
                    return PrimitiveSize(ep.PrimitiveToken);
                if (_structIndexMap.TryGetValue(named.Name, out int idx) && idx < _structContexts.Count)
                {
                    var target = _structContexts[idx];
                    return ComputeStaticSize(target);
                }
                return -1;
            case CStringTypeRef:
            case StringTypeRef:
            case MatchTypeRef:
                return -1;
            default:
                return -1;
        }
    }

    private static int PrimitiveSize(TokenType token) => token switch
    {
        TokenType.U8 or TokenType.I8 or TokenType.Bool => 1,
        TokenType.U16 or TokenType.U16Le or TokenType.U16Be or
        TokenType.I16 or TokenType.I16Le or TokenType.I16Be => 2,
        TokenType.U32 or TokenType.U32Le or TokenType.U32Be or
        TokenType.I32 or TokenType.I32Le or TokenType.I32Be or
        TokenType.F32 or TokenType.F32Le or TokenType.F32Be => 4,
        TokenType.U64 or TokenType.U64Le or TokenType.U64Be or
        TokenType.I64 or TokenType.I64Le or TokenType.I64Be or
        TokenType.F64 or TokenType.F64Le or TokenType.F64Be => 8,
        _ => -1,
    };

    // ─── Pass 3: Build final program ──────────────────────────────────

    private BytecodeProgram BuildProgram()
    {
        // Concatenate all per-struct bytecode into one array, recording offsets.
        int totalLen = 0;
        for (int i = 0; i < _structContexts.Count; i++)
            totalLen += _structContexts[i].Builder.ToArray().Length;

        var allBytecode = new byte[totalLen];
        int offset = 0;
        var structMetas = new StructMeta[_structContexts.Count];
        int rootIndex = -1;

        for (int i = 0; i < _structContexts.Count; i++)
        {
            var ctx = _structContexts[i];
            byte[] code = ctx.Builder.ToArray();
            Buffer.BlockCopy(code, 0, allBytecode, offset, code.Length);

            StructFlags flags = StructFlags.None;
            if (ctx.IsRoot) flags |= StructFlags.IsRoot;
            if (ctx.IsBitsStruct) flags |= StructFlags.IsBits;
            if (ctx.Coverage == CoverageMode.Partial) flags |= StructFlags.IsPartialCoverage;

            if (ctx.IsRoot) rootIndex = i;

            structMetas[i] = new StructMeta
            {
                NameIndex = ctx.NameIndex,
                ParamCount = (byte)(ctx.Parameters?.Count ?? 0),
                Fields = ctx.FieldMetas.ToArray(),
                BytecodeOffset = offset,
                BytecodeLength = code.Length,
                StaticSize = ComputeStaticSize(ctx),
                Flags = flags,
                MaxDepth = ctx.MaxDepth,
            };

            offset += code.Length;
        }

        return new BytecodeProgram
        {
            Bytecode = allBytecode,
            StringTable = _masterBuilder.GetStringTable(),
            Structs = structMetas,
            RootStructIndex = rootIndex,
            Parameters = new Dictionary<string, object>(_parameters),
            Source = null,
            Version = 1,
        };
    }

    // ─── Per-struct emit context ──────────────────────────────────────

    private sealed class StructEmitContext
    {
        public required string Name { get; init; }
        public required ushort NameIndex { get; init; }
        public bool IsBitsStruct { get; init; }
        public bool IsRoot { get; init; }
        public CoverageMode? Coverage { get; init; }
        public int? MaxDepth { get; init; }

        public StructDecl? Decl { get; init; }
        public BitsStructDecl? BitsDecl { get; init; }
        public IReadOnlyList<string>? Parameters { get; init; }

        public BytecodeBuilder Builder { get; } = new();
        public Dictionary<string, ushort> FieldIndices { get; } = new();
        public Dictionary<string, ushort> ParamIndices { get; } = new();
        public List<FieldMeta> FieldMetas { get; } = new();
        public HashSet<string> SearchedArrayNames { get; set; } = new();
        private ushort _nextFieldId;

        public ushort AllocateField(string name, FieldModifiers mods, BytecodeBuilder masterBuilder)
        {
            ushort id = _nextFieldId++;
            FieldIndices[name] = id;

            FieldFlags flags = FieldFlags.None;
            if (mods.IsDerived) flags |= FieldFlags.Derived;
            if (mods.IsHidden) flags |= FieldFlags.Hidden;

            FieldMetas.Add(new FieldMeta
            {
                NameIndex = masterBuilder.InternString(name),
                Flags = flags,
            });

            return id;
        }

        public ushort AllocateHiddenField(string name, BytecodeBuilder masterBuilder)
        {
            return AllocateField(name, new FieldModifiers { IsHidden = true }, masterBuilder);
        }

        public string GetFieldName(ushort fieldId)
        {
            foreach (var kv in FieldIndices)
                if (kv.Value == fieldId)
                    return kv.Key;
            return $"field_{fieldId}";
        }
    }
}
