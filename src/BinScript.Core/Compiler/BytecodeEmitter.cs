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

    // Shared string table across all struct builders.
    private readonly BytecodeBuilder _masterBuilder = new();

    // Maps struct/bits-struct name → index in the output Structs array.
    private readonly Dictionary<string, int> _structIndexMap = new();

    // Per-struct context used during emission.
    private readonly List<StructEmitContext> _structContexts = new();

    public BytecodeEmitter(ScriptFile ast, TypeResolver typeEnv, Dictionary<string, object>? parameters = null)
    {
        _ast = ast;
        _typeEnv = typeEnv;
        _parameters = parameters ?? new Dictionary<string, object>();
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

        EmitMembers(ctx, decl.Members);
        builder.Emit(Opcode.Return);
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
                ctx.Builder.Emit(Opcode.SeekPush);
                EmitExpression(ctx, atBlock.Offset);
                ctx.Builder.Emit(Opcode.SeekAbs);
                EmitMembers(ctx, atBlock.Members);
                ctx.Builder.Emit(Opcode.SeekPop);
                break;
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
                // For complex expressions, use i64 type tag with 0 as placeholder.
                ctx.Builder.EmitU8(0);
                ctx.Builder.EmitI64(0);
                break;
        }
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

        ctx.Builder.Emit(Opcode.CallStruct);
        ctx.Builder.EmitU16((ushort)structIdx);
        ctx.Builder.EmitU8((byte)named.Arguments.Count);

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

            // Emit the type read for this arm.
            EmitFieldRead(ctx, arm.Type, fieldId, modifiers);

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
        ushort nameIdx = _masterBuilder.InternString(field.Name);
        ctx.Builder.Emit(Opcode.EmitArrayBegin);
        ctx.Builder.EmitU16(nameIdx);
        ctx.Builder.EmitU16(fieldId);

        switch (field.Array)
        {
            case CountArraySpec countArr:
                EmitExpression(ctx, countArr.Count);
                ctx.Builder.Emit(Opcode.ArrayBeginCount);
                break;
            case UntilArraySpec untilArr:
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

        // Emit the field body (the read for one element).
        EmitFieldRead(ctx, field.Type, fieldId, field.Modifiers);
        EmitFieldPostProcessing(ctx, field, fieldId);

        ctx.Builder.Emit(Opcode.ArrayNext);

        // For until arrays, emit the condition check.
        if (field.Array is UntilArraySpec untilSpec)
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
        EmitExpression(ctx, let.Value);
        // Store the computed value as a hidden field.
        ctx.Builder.Emit(Opcode.StoreFieldVal);
        ctx.Builder.EmitU16(fieldId);
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
        // Flatten to a dotted path and intern it.
        string path = FlattenFieldPath(fa);
        ushort nameIdx = _masterBuilder.InternString(path);
        ctx.Builder.Emit(Opcode.PushFieldVal);
        ctx.Builder.EmitU16(nameIdx);
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

        public StructDecl? Decl { get; init; }
        public BitsStructDecl? BitsDecl { get; init; }
        public IReadOnlyList<string>? Parameters { get; init; }

        public BytecodeBuilder Builder { get; } = new();
        public Dictionary<string, ushort> FieldIndices { get; } = new();
        public Dictionary<string, ushort> ParamIndices { get; } = new();
        public List<FieldMeta> FieldMetas { get; } = new();
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
    }
}
