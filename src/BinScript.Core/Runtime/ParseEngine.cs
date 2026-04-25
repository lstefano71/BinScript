namespace BinScript.Core.Runtime;

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using BinScript.Core.Bytecode;
using BinScript.Core.Compiler;
using BinScript.Core.Interfaces;
using BinScript.Core.Model;

/// <summary>
/// The bytecode VM that executes compiled bytecode against binary data.
/// </summary>
public sealed class ParseEngine
{
    // Array loop state
    private struct ArrayLoopState
    {
        public long Count;          // -1 for non-count loops
        public long Index;
        public int BodyStart;       // ip offset within struct for loop body start
        public ArrayLoopKind Kind;
    }

    private enum ArrayLoopKind : byte { Count, Until, Sentinel, Greedy }

    public ParseResult Parse(
        BytecodeProgram program,
        ReadOnlyMemory<byte> input,
        IResultEmitter emitter,
        ParseOptions? options = null,
        int rootStructIndex = -1)
    {
        options ??= new ParseOptions();
        var diagnostics = new List<Diagnostic>();
        var ctx = new ParseContext(input);

        int entryStruct = rootStructIndex >= 0 ? rootStructIndex : program.RootStructIndex;
        if (entryStruct < 0 || entryStruct >= program.Structs.Length)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error, "PE001", "No root struct found.",
                default));
            return new ParseResult(false, null, diagnostics, 0, input.Length);
        }

        try
        {
            var structMeta = program.Structs[entryStruct];
            string rootName = program.GetString(structMeta.NameIndex);
            emitter.BeginRoot(rootName);
            ExecuteStruct(program, ctx, emitter, entryStruct, [], options, diagnostics);
            emitter.EndRoot();
        }
        catch (ParseException ex)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error, "PE100", ex.Message, default));
            return new ParseResult(false, null, diagnostics, ctx.Position, input.Length);
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error, "PE101",
                $"Read past end of input at position {ctx.Position}.", default));
            return new ParseResult(false, null, diagnostics, ctx.Position, input.Length);
        }

        bool success = !diagnostics.Exists(d => d.Severity == DiagnosticSeverity.Error);
        return new ParseResult(success, null, diagnostics, ctx.Position, input.Length);
    }

    private void ExecuteStruct(
        BytecodeProgram program,
        ParseContext ctx,
        IResultEmitter emitter,
        int structIndex,
        StackValue[] parameters,
        ParseOptions options,
        List<Diagnostic> diagnostics)
    {
        if (ctx.Depth >= options.MaxDepth)
            throw new ParseException($"Max nesting depth {options.MaxDepth} exceeded.");

        var structMeta = program.Structs[structIndex];

        // Per-struct @max_depth enforcement
        if (structMeta.MaxDepth is int limit && ctx.GetStructDepth(structIndex) >= limit)
            throw new ParseException($"Max depth {limit} exceeded for struct '{program.GetString(structMeta.NameIndex)}'.");

        ctx.Depth++;
        ctx.IncrementStructDepth(structIndex);
        var fieldTable = new FieldValueTable(structMeta.Fields.Length + 16);
        ctx.PushFieldTable(fieldTable);
        ctx.PushParams(parameters);

        int savedStructIndex = ctx.CurrentStructIndex;
        ctx.CurrentStructIndex = structIndex;

        byte[] bytecode = program.Bytecode;
        int baseOffset = structMeta.BytecodeOffset;
        int endOffset = baseOffset + structMeta.BytecodeLength;
        int ip = baseOffset;

        var arrayStack = new Stack<ArrayLoopState>();
        bool inBits = false;

        while (ip < endOffset)
        {
            var opcode = (Opcode)bytecode[ip++];

            switch (opcode)
            {
                // ─── Tier 1: Primitive Reads ───────────────────────
                case Opcode.ReadU8:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    long offset = ctx.Position;
                    byte val = input(ctx)[0];
                    ctx.Position++;
                    var sv = StackValue.FromInt(val);
                    fieldTable.SetValue(fid, sv);
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, 1);
                    if (inBits) ctx.BitBuffer = val;
                    EmitFieldInt(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadI8:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    long offset = ctx.Position;
                    sbyte val = (sbyte)input(ctx)[0];
                    ctx.Position++;
                    var sv = StackValue.FromInt(val);
                    fieldTable.SetValue(fid, sv);
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, 1);
                    EmitFieldInt(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadU16Le:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    long offset = ctx.Position;
                    ushort val = BinaryPrimitives.ReadUInt16LittleEndian(inputSpan(ctx, 2));
                    ctx.Position += 2;
                    fieldTable.SetValue(fid, StackValue.FromInt(val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, 2);
                    if (inBits) ctx.BitBuffer = val;
                    EmitFieldInt(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadU16Be:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    long offset = ctx.Position;
                    ushort val = BinaryPrimitives.ReadUInt16BigEndian(inputSpan(ctx, 2));
                    ctx.Position += 2;
                    fieldTable.SetValue(fid, StackValue.FromInt(val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, 2);
                    if (inBits) ctx.BitBuffer = val;
                    EmitFieldInt(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadI16Le:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    long offset = ctx.Position;
                    short val = BinaryPrimitives.ReadInt16LittleEndian(inputSpan(ctx, 2));
                    ctx.Position += 2;
                    fieldTable.SetValue(fid, StackValue.FromInt(val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, 2);
                    EmitFieldInt(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadI16Be:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    long offset = ctx.Position;
                    short val = BinaryPrimitives.ReadInt16BigEndian(inputSpan(ctx, 2));
                    ctx.Position += 2;
                    fieldTable.SetValue(fid, StackValue.FromInt(val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, 2);
                    EmitFieldInt(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadU32Le:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    long offset = ctx.Position;
                    uint val = BinaryPrimitives.ReadUInt32LittleEndian(inputSpan(ctx, 4));
                    ctx.Position += 4;
                    fieldTable.SetValue(fid, StackValue.FromInt(val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, 4);
                    if (inBits) ctx.BitBuffer = val;
                    EmitFieldInt(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadU32Be:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    long offset = ctx.Position;
                    uint val = BinaryPrimitives.ReadUInt32BigEndian(inputSpan(ctx, 4));
                    ctx.Position += 4;
                    fieldTable.SetValue(fid, StackValue.FromInt(val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, 4);
                    if (inBits) ctx.BitBuffer = val;
                    EmitFieldInt(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadI32Le:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    long offset = ctx.Position;
                    int val = BinaryPrimitives.ReadInt32LittleEndian(inputSpan(ctx, 4));
                    ctx.Position += 4;
                    fieldTable.SetValue(fid, StackValue.FromInt(val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, 4);
                    EmitFieldInt(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadI32Be:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    long offset = ctx.Position;
                    int val = BinaryPrimitives.ReadInt32BigEndian(inputSpan(ctx, 4));
                    ctx.Position += 4;
                    fieldTable.SetValue(fid, StackValue.FromInt(val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, 4);
                    EmitFieldInt(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadU64Le:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    long offset = ctx.Position;
                    ulong val = BinaryPrimitives.ReadUInt64LittleEndian(inputSpan(ctx, 8));
                    ctx.Position += 8;
                    fieldTable.SetValue(fid, StackValue.FromInt((long)val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, 8);
                    EmitFieldUInt(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadU64Be:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    long offset = ctx.Position;
                    ulong val = BinaryPrimitives.ReadUInt64BigEndian(inputSpan(ctx, 8));
                    ctx.Position += 8;
                    fieldTable.SetValue(fid, StackValue.FromInt((long)val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, 8);
                    EmitFieldUInt(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadI64Le:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    long offset = ctx.Position;
                    long val = BinaryPrimitives.ReadInt64LittleEndian(inputSpan(ctx, 8));
                    ctx.Position += 8;
                    fieldTable.SetValue(fid, StackValue.FromInt(val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, 8);
                    EmitFieldInt(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadI64Be:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    long offset = ctx.Position;
                    long val = BinaryPrimitives.ReadInt64BigEndian(inputSpan(ctx, 8));
                    ctx.Position += 8;
                    fieldTable.SetValue(fid, StackValue.FromInt(val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, 8);
                    EmitFieldInt(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadF32Le:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    long offset = ctx.Position;
                    float val = BinaryPrimitives.ReadSingleLittleEndian(inputSpan(ctx, 4));
                    ctx.Position += 4;
                    fieldTable.SetValue(fid, StackValue.FromFloat(val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, 4);
                    EmitFieldFloat(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadF32Be:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    long offset = ctx.Position;
                    float val = BinaryPrimitives.ReadSingleBigEndian(inputSpan(ctx, 4));
                    ctx.Position += 4;
                    fieldTable.SetValue(fid, StackValue.FromFloat(val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, 4);
                    EmitFieldFloat(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadF64Le:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    long offset = ctx.Position;
                    double val = BinaryPrimitives.ReadDoubleLittleEndian(inputSpan(ctx, 8));
                    ctx.Position += 8;
                    fieldTable.SetValue(fid, StackValue.FromFloat(val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, 8);
                    EmitFieldFloat(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadF64Be:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    long offset = ctx.Position;
                    double val = BinaryPrimitives.ReadDoubleBigEndian(inputSpan(ctx, 8));
                    ctx.Position += 8;
                    fieldTable.SetValue(fid, StackValue.FromFloat(val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, 8);
                    EmitFieldFloat(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadBool:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    long offset = ctx.Position;
                    bool val = input(ctx)[0] != 0;
                    ctx.Position++;
                    fieldTable.SetValue(fid, StackValue.FromBool(val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, 1);
                    EmitFieldBool(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadFixedStr:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    ushort len = ReadU16(bytecode, ref ip);
                    byte enc = bytecode[ip++];
                    long offset = ctx.Position;
                    string val = DecodeString(inputSpan(ctx, len), (StringEncoding)enc);
                    ctx.Position += len;
                    fieldTable.SetValue(fid, StackValue.FromString(val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, len);
                    EmitFieldString(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadCString:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    byte enc = bytecode[ip++];
                    long offset = ctx.Position;
                    var span = ctx.Input.Span;
                    int start = (int)ctx.Position;
                    int end = start;
                    while (end < span.Length && span[end] != 0) end++;
                    int len = end - start;
                    string val = DecodeString(span.Slice(start, len), (StringEncoding)enc);
                    ctx.Position = end + 1; // skip null terminator
                    fieldTable.SetValue(fid, StackValue.FromString(val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, end + 1 - start);
                    EmitFieldString(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadBytesFixed:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    uint len = ReadU32(bytecode, ref ip);
                    long offset = ctx.Position;
                    byte[] val = inputSpan(ctx, (int)len).ToArray();
                    ctx.Position += len;
                    fieldTable.SetValue(fid, StackValue.FromBytes(val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, len);
                    EmitFieldBytes(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.SkipFixed:
                {
                    ushort len = ReadU16(bytecode, ref ip);
                    ctx.Position += len;
                    break;
                }

                // ─── Pointer Reads (no JSON emission) ─────────────
                case Opcode.ReadPtrU32:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    long offset = ctx.Position;
                    uint val = BinaryPrimitives.ReadUInt32LittleEndian(inputSpan(ctx, 4));
                    ctx.Position += 4;
                    fieldTable.SetValue(fid, StackValue.FromInt(val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, 4);
                    ctx.Push(StackValue.FromInt(val));
                    break;
                }
                case Opcode.ReadPtrU64:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    long offset = ctx.Position;
                    ulong val = BinaryPrimitives.ReadUInt64LittleEndian(inputSpan(ctx, 8));
                    ctx.Position += 8;
                    fieldTable.SetValue(fid, StackValue.FromInt((long)val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, 8);
                    ctx.Push(StackValue.FromInt((long)val));
                    break;
                }

                case Opcode.AssertValue:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    byte typeTag = bytecode[ip++];
                    var fieldVal = fieldTable.GetValue(fid);
                    switch (typeTag)
                    {
                        case 0: // i64
                        {
                            long expected = BinaryPrimitives.ReadInt64LittleEndian(
                                bytecode.AsSpan(ip, 8));
                            ip += 8;
                            if (fieldVal.AsLong() != expected)
                            {
                                string fname = GetFieldName(program, structMeta, fid);
                                throw new ParseException(
                                    $"Assertion failed for '{fname}': expected {expected}, got {fieldVal.AsLong()} at offset {fieldTable.GetOffset(fid)}.");
                            }
                            break;
                        }
                        case 2: // string
                        {
                            ushort strId = ReadU16(bytecode, ref ip);
                            string expected = program.GetString(strId);
                            if (fieldVal.StringValue != expected)
                            {
                                string fname = GetFieldName(program, structMeta, fid);
                                throw new ParseException(
                                    $"Assertion failed for '{fname}': expected \"{expected}\", got \"{fieldVal.StringValue}\" at offset {fieldTable.GetOffset(fid)}.");
                            }
                            break;
                        }
                        case 3: // bool
                        {
                            bool expected = bytecode[ip++] != 0;
                            if (fieldVal.AsBool() != expected)
                            {
                                string fname = GetFieldName(program, structMeta, fid);
                                throw new ParseException(
                                    $"Assertion failed for '{fname}': expected {expected}, got {fieldVal.AsBool()} at offset {fieldTable.GetOffset(fid)}.");
                            }
                            break;
                        }
                    }
                    break;
                }

                // ─── Control Flow ──────────────────────────────────
                case Opcode.CallStruct:
                {
                    ushort sid = ReadU16(bytecode, ref ip);
                    byte paramCount = bytecode[ip++];
                    var args = new StackValue[paramCount];
                    for (int i = paramCount - 1; i >= 0; i--)
                        args[i] = ctx.Pop();
                    ExecuteStruct(program, ctx, emitter, sid, args, options, diagnostics);
                    break;
                }
                case Opcode.Return:
                    ip = endOffset; // exit loop
                    break;

                case Opcode.Jump:
                {
                    int target = ReadI32(bytecode, ref ip);
                    ip = baseOffset + target;
                    break;
                }
                case Opcode.JumpIfFalse:
                {
                    int target = ReadI32(bytecode, ref ip);
                    var val = ctx.Pop();
                    if (!val.AsBool())
                        ip = baseOffset + target;
                    break;
                }
                case Opcode.JumpIfTrue:
                {
                    int target = ReadI32(bytecode, ref ip);
                    var val = ctx.Pop();
                    if (val.AsBool())
                        ip = baseOffset + target;
                    break;
                }

                // ─── Seeking ───────────────────────────────────────
                case Opcode.SeekAbs:
                {
                    var pos = ctx.Pop();
                    ctx.Position = pos.AsLong();
                    break;
                }
                case Opcode.SeekPush:
                    ctx.PushPosition();
                    break;
                case Opcode.SeekPop:
                    ctx.PopPosition();
                    break;

                // ─── Emitter Events ────────────────────────────────
                case Opcode.EmitStructBegin:
                {
                    ushort nameId = ReadU16(bytecode, ref ip);
                    ushort fid = ReadU16(bytecode, ref ip);
                    string sname = program.GetString(nameId);
                    string fname = GetFieldName(program, structMeta, fid);
                    emitter.BeginStruct(fname, sname);
                    break;
                }
                case Opcode.EmitStructEnd:
                    emitter.EndStruct();
                    break;

                case Opcode.EmitArrayBegin:
                {
                    ushort nameId = ReadU16(bytecode, ref ip);
                    ushort fid = ReadU16(bytecode, ref ip);
                    string fname = GetFieldName(program, structMeta, fid);
                    emitter.BeginArray(fname, -1);
                    break;
                }
                case Opcode.EmitArrayEnd:
                    emitter.EndArray();
                    break;

                case Opcode.EmitVariantBegin:
                {
                    ushort nameId = ReadU16(bytecode, ref ip);
                    ushort fid = ReadU16(bytecode, ref ip);
                    string vname = program.GetString(nameId);
                    string fname = GetFieldName(program, structMeta, fid);
                    emitter.BeginVariant(fname, vname);
                    break;
                }
                case Opcode.EmitVariantEnd:
                    emitter.EndVariant();
                    break;

                case Opcode.EmitBitsBegin:
                {
                    ushort nameId = ReadU16(bytecode, ref ip);
                    ushort fid = ReadU16(bytecode, ref ip);
                    string bname = program.GetString(nameId);
                    string fname = GetFieldName(program, structMeta, fid);
                    emitter.BeginBitsStruct(fname, bname);
                    ctx.BitBuffer = 0;
                    ctx.BitPosition = 0;
                    inBits = true;
                    break;
                }
                case Opcode.EmitBitsEnd:
                    emitter.EndBitsStruct();
                    ctx.BitPosition = 0;
                    inBits = false;
                    break;
                case Opcode.EmitNull:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    string fname = GetFieldName(program, structMeta, fid);
                    emitter.EmitNull(fname);
                    break;
                }

                // ─── Dynamic Reads ─────────────────────────────────
                case Opcode.ReadBytesDyn:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    long len = ctx.Pop().AsLong();
                    long offset = ctx.Position;
                    byte[] val = inputSpan(ctx, (int)len).ToArray();
                    ctx.Position += len;
                    fieldTable.SetValue(fid, StackValue.FromBytes(val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, len);
                    EmitFieldBytes(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadStringDyn:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    byte enc = bytecode[ip++];
                    long len = ctx.Pop().AsLong();
                    long offset = ctx.Position;
                    string val = DecodeString(inputSpan(ctx, (int)len), (StringEncoding)enc);
                    ctx.Position += len;
                    fieldTable.SetValue(fid, StackValue.FromString(val));
                    fieldTable.SetOffset(fid, offset);
                    fieldTable.SetSize(fid, len);
                    EmitFieldString(emitter, program, structMeta, fid, val);
                    break;
                }
                case Opcode.ReadBits:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    byte bitCount = bytecode[ip++];
                    ulong val = ExtractBits(ctx, bitCount);
                    fieldTable.SetValue(fid, StackValue.FromInt((long)val));
                    emitter.EmitBits(GetFieldName(program, structMeta, fid), val, bitCount);
                    break;
                }
                case Opcode.ReadBit:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    bool val = ExtractBits(ctx, 1) != 0;
                    fieldTable.SetValue(fid, StackValue.FromBool(val));
                    emitter.EmitBit(GetFieldName(program, structMeta, fid), val);
                    break;
                }

                // ─── Expression VM ─────────────────────────────────
                case Opcode.PushConstI64:
                {
                    long val = BinaryPrimitives.ReadInt64LittleEndian(
                        bytecode.AsSpan(ip, 8));
                    ip += 8;
                    ctx.Push(StackValue.FromInt(val));
                    break;
                }
                case Opcode.PushConstF64:
                {
                    double val = BinaryPrimitives.ReadDoubleLittleEndian(
                        bytecode.AsSpan(ip, 8));
                    ip += 8;
                    ctx.Push(StackValue.FromFloat(val));
                    break;
                }
                case Opcode.PushConstStr:
                {
                    ushort strId = ReadU16(bytecode, ref ip);
                    ctx.Push(StackValue.FromString(program.GetString(strId)));
                    break;
                }
                case Opcode.PushFieldVal:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    ctx.Push(fieldTable.GetValue(fid));
                    break;
                }
                case Opcode.PushParam:
                {
                    ushort paramIdx = ReadU16(bytecode, ref ip);
                    var parms = ctx.CurrentParams;
                    ctx.Push(paramIdx < parms.Length ? parms[paramIdx] : StackValue.Zero);
                    break;
                }
                case Opcode.PushRuntimeVar:
                {
                    byte varId = bytecode[ip++];
                    long val = varId switch
                    {
                        0 => ctx.InputSize,
                        1 => ctx.Offset,
                        2 => ctx.Remaining,
                        _ => 0,
                    };
                    ctx.Push(StackValue.FromInt(val));
                    break;
                }
                case Opcode.PushFileParam:
                {
                    ushort nameIdx = ReadU16(bytecode, ref ip);
                    string name = program.GetString(nameIdx);
                    if (options.RuntimeParameters != null && options.RuntimeParameters.TryGetValue(name, out long pval))
                        ctx.Push(StackValue.FromInt(pval));
                    else
                        throw new ParseException($"Runtime parameter '{name}' not provided.");
                    break;
                }
                case Opcode.PushIndex:
                    ctx.Push(StackValue.FromInt(ctx.CurrentArrayIndex));
                    break;
                case Opcode.StoreFieldVal:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    var val = ctx.Pop();
                    fieldTable.SetValue(fid, val);
                    // For derived fields that are not hidden, emit them
                    if (fid < structMeta.Fields.Length)
                    {
                        var fm = structMeta.Fields[fid];
                        bool isHidden = (fm.Flags & FieldFlags.Hidden) != 0;
                        bool isDerived = (fm.Flags & FieldFlags.Derived) != 0;
                        if (isDerived && !isHidden)
                        {
                            string fname = program.GetString(fm.NameIndex);
                            if (val.Kind == StackValueKind.Float)
                                emitter.EmitFloat(fname, val.FloatValue);
                            else if (val.Kind == StackValueKind.String)
                                emitter.EmitString(fname, val.StringValue ?? "");
                            else
                                emitter.EmitInt(fname, val.IntValue, null);
                        }
                    }
                    break;
                }

                case Opcode.CopyChildField:
                {
                    ushort childFieldNameIdx = ReadU16(bytecode, ref ip);
                    ushort dstFieldId = ReadU16(bytecode, ref ip);
                    if (ctx.LastChildFieldTable != null && ctx.LastChildStructIndex >= 0)
                    {
                        string childFieldName = program.GetString(childFieldNameIdx);
                        var childMeta = program.Structs[ctx.LastChildStructIndex];
                        for (int fi = 0; fi < childMeta.Fields.Length; fi++)
                        {
                            if (program.GetString(childMeta.Fields[fi].NameIndex) == childFieldName)
                            {
                                fieldTable.SetValue(dstFieldId, ctx.LastChildFieldTable.GetValue((ushort)fi));
                                fieldTable.SetOffset(dstFieldId, ctx.LastChildFieldTable.GetOffset((ushort)fi));
                                fieldTable.SetSize(dstFieldId, ctx.LastChildFieldTable.GetSize((ushort)fi));
                                fieldTable.SetArrayCount(dstFieldId, ctx.LastChildFieldTable.GetArrayCount((ushort)fi));
                                break;
                            }
                        }
                    }
                    break;
                }

                // ─── Arithmetic/Logic ──────────────────────────────
                case Opcode.OpAdd: ExpressionEvaluator.Add(ctx); break;
                case Opcode.OpSub: ExpressionEvaluator.Sub(ctx); break;
                case Opcode.OpMul: ExpressionEvaluator.Mul(ctx); break;
                case Opcode.OpDiv: ExpressionEvaluator.Div(ctx); break;
                case Opcode.OpMod: ExpressionEvaluator.Mod(ctx); break;
                case Opcode.OpAnd: ExpressionEvaluator.And(ctx); break;
                case Opcode.OpOr: ExpressionEvaluator.Or(ctx); break;
                case Opcode.OpXor: ExpressionEvaluator.Xor(ctx); break;
                case Opcode.OpNot: ExpressionEvaluator.Not(ctx); break;
                case Opcode.OpShl: ExpressionEvaluator.Shl(ctx); break;
                case Opcode.OpShr: ExpressionEvaluator.Shr(ctx); break;
                case Opcode.OpEq: ExpressionEvaluator.Eq(ctx); break;
                case Opcode.OpNe: ExpressionEvaluator.Ne(ctx); break;
                case Opcode.OpLt: ExpressionEvaluator.Lt(ctx); break;
                case Opcode.OpGt: ExpressionEvaluator.Gt(ctx); break;
                case Opcode.OpLe: ExpressionEvaluator.Le(ctx); break;
                case Opcode.OpGe: ExpressionEvaluator.Ge(ctx); break;
                case Opcode.OpLogicalAnd: ExpressionEvaluator.LogicalAnd(ctx); break;
                case Opcode.OpLogicalOr: ExpressionEvaluator.LogicalOr(ctx); break;
                case Opcode.OpLogicalNot: ExpressionEvaluator.LogicalNot(ctx); break;
                case Opcode.OpNeg: ExpressionEvaluator.Negate(ctx); break;

                // ─── Built-in Functions ────────────────────────────
                case Opcode.FnSizeOf:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    BuiltinFunctions.SizeOf(ctx, fid);
                    break;
                }
                case Opcode.FnOffsetOf:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    BuiltinFunctions.OffsetOf(ctx, fid);
                    break;
                }
                case Opcode.FnCount:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    BuiltinFunctions.Count(ctx, fid);
                    break;
                }
                case Opcode.FnStrLen:
                {
                    ushort fid = ReadU16(bytecode, ref ip);
                    BuiltinFunctions.StrLen(ctx, fid);
                    break;
                }
                case Opcode.FnCrc32:
                {
                    byte fieldCount = bytecode[ip++];
                    BuiltinFunctions.ComputeCrc32(ctx, fieldCount);
                    break;
                }
                case Opcode.FnAdler32:
                {
                    byte fieldCount = bytecode[ip++];
                    BuiltinFunctions.ComputeAdler32(ctx, fieldCount);
                    break;
                }

                // ─── String Methods ────────────────────────────────
                case Opcode.StrStartsWith: ExpressionEvaluator.StartsWith(ctx); break;
                case Opcode.StrEndsWith: ExpressionEvaluator.EndsWith(ctx); break;
                case Opcode.StrContains: ExpressionEvaluator.Contains(ctx); break;

                // ─── Array Control ─────────────────────────────────
                case Opcode.ArrayBeginCount:
                {
                    long count = ctx.Pop().AsLong();
                    fieldTable.SetArrayCount(GetLastArrayFieldId(arrayStack), count);
                    arrayStack.Push(new ArrayLoopState
                    {
                        Count = count,
                        Index = 0,
                        BodyStart = ip - baseOffset,
                        Kind = ArrayLoopKind.Count,
                    });
                    ctx.PushArrayIndex(0);
                    if (count <= 0)
                    {
                        // Skip to ArrayEnd
                        ip = SkipToArrayEnd(bytecode, ip, endOffset);
                        arrayStack.Pop();
                        ctx.PopArrayIndex();
                    }
                    break;
                }
                case Opcode.ArrayBeginUntil:
                {
                    arrayStack.Push(new ArrayLoopState
                    {
                        Count = -1,
                        Index = 0,
                        BodyStart = ip - baseOffset,
                        Kind = ArrayLoopKind.Until,
                    });
                    ctx.PushArrayIndex(0);
                    break;
                }
                case Opcode.ArrayBeginSentinel:
                {
                    arrayStack.Push(new ArrayLoopState
                    {
                        Count = -1,
                        Index = 0,
                        BodyStart = ip - baseOffset,
                        Kind = ArrayLoopKind.Sentinel,
                    });
                    ctx.PushArrayIndex(0);
                    break;
                }
                case Opcode.ArrayBeginGreedy:
                {
                    arrayStack.Push(new ArrayLoopState
                    {
                        Count = -1,
                        Index = 0,
                        BodyStart = ip - baseOffset,
                        Kind = ArrayLoopKind.Greedy,
                    });
                    ctx.PushArrayIndex(0);
                    break;
                }
                case Opcode.ArrayNext:
                {
                    if (arrayStack.Count > 0)
                    {
                        var state = arrayStack.Pop();
                        state.Index++;
                        arrayStack.Push(state);
                        ctx.SetCurrentArrayIndex(state.Index);
                    }
                    break;
                }
                case Opcode.ArrayEnd:
                {
                    if (arrayStack.Count > 0)
                    {
                        var state = arrayStack.Peek();
                        bool continueLoop = false;

                        switch (state.Kind)
                        {
                            case ArrayLoopKind.Count:
                                continueLoop = state.Index < state.Count;
                                break;
                            case ArrayLoopKind.Until:
                                // The condition expression was pushed after ArrayNext
                                var cond = ctx.Pop();
                                continueLoop = !cond.AsBool();
                                break;
                            case ArrayLoopKind.Greedy:
                                continueLoop = ctx.Position < ctx.InputSize;
                                break;
                            case ArrayLoopKind.Sentinel:
                                continueLoop = ctx.Position < ctx.InputSize;
                                break;
                        }

                        if (continueLoop && state.Index < options.MaxArrayElements)
                        {
                            ip = baseOffset + state.BodyStart;
                        }
                        else
                        {
                            arrayStack.Pop();
                            ctx.PopArrayIndex();
                        }
                    }
                    break;
                }

                // ─── Match ─────────────────────────────────────────
                case Opcode.MatchBegin:
                    // Discriminant is already on the eval stack
                    break;

                case Opcode.MatchArmEq:
                {
                    var disc = ctx.Peek();
                    byte typeTag = bytecode[ip++];
                    bool matches = false;
                    switch (typeTag)
                    {
                        case 0: // i64
                        {
                            long expected = BinaryPrimitives.ReadInt64LittleEndian(
                                bytecode.AsSpan(ip, 8));
                            ip += 8;
                            matches = disc.AsLong() == expected;
                            break;
                        }
                        case 2: // string
                        {
                            ushort strId = ReadU16(bytecode, ref ip);
                            matches = disc.StringValue == program.GetString(strId);
                            break;
                        }
                        case 3: // bool
                        {
                            bool expected = bytecode[ip++] != 0;
                            matches = disc.AsBool() == expected;
                            break;
                        }
                    }
                    int skipTarget = ReadI32(bytecode, ref ip);
                    if (!matches)
                    {
                        // Skip past this arm's body to the next arm
                        ip = baseOffset + skipTarget;
                    }
                    // If matches, fall through to execute arm body
                    break;
                }
                case Opcode.MatchArmRange:
                {
                    var disc = ctx.Peek();
                    // Read low value
                    byte lowTag = bytecode[ip++];
                    long low = 0;
                    switch (lowTag)
                    {
                        case 0: low = BinaryPrimitives.ReadInt64LittleEndian(bytecode.AsSpan(ip, 8)); ip += 8; break;
                        case 2: ip += 2; break;
                        case 3: ip++; break;
                    }
                    // Read high value
                    byte highTag = bytecode[ip++];
                    long high = 0;
                    switch (highTag)
                    {
                        case 0: high = BinaryPrimitives.ReadInt64LittleEndian(bytecode.AsSpan(ip, 8)); ip += 8; break;
                        case 2: ip += 2; break;
                        case 3: ip++; break;
                    }
                    int skipTarget2 = ReadI32(bytecode, ref ip);
                    long discVal = disc.AsLong();
                    if (discVal < low || discVal > high)
                        ip = baseOffset + skipTarget2;
                    break;
                }
                case Opcode.MatchArmGuard:
                {
                    int skipTarget3 = ReadI32(bytecode, ref ip);
                    // Guard: would need to evaluate a condition expression.
                    // For now, just fall through (treat as default).
                    break;
                }
                case Opcode.MatchDefault:
                {
                    int _skipTarget = ReadI32(bytecode, ref ip);
                    // Default always matches, fall through to arm body
                    break;
                }
                case Opcode.MatchEnd:
                    // Pop discriminant from eval stack if still there
                    if (ctx.EvalStackCount > 0)
                        ctx.Pop();
                    break;

                // ─── Alignment ─────────────────────────────────────
                case Opcode.Align:
                {
                    long alignment = ctx.Pop().AsLong();
                    if (alignment > 0)
                    {
                        long remainder = ctx.Position % alignment;
                        if (remainder != 0)
                            ctx.Position += alignment - remainder;
                    }
                    break;
                }
                case Opcode.AlignFixed:
                {
                    ushort alignment = ReadU16(bytecode, ref ip);
                    if (alignment > 0)
                    {
                        long remainder = ctx.Position % alignment;
                        if (remainder != 0)
                            ctx.Position += alignment - remainder;
                    }
                    break;
                }

                default:
                    throw new ParseException($"Unknown opcode 0x{(byte)opcode:X2} at ip={ip - 1}.");
            }
        }

        ctx.PopParams();
        ctx.LastChildFieldTable = fieldTable;
        ctx.LastChildStructIndex = structIndex;
        ctx.PopFieldTable();
        ctx.CurrentStructIndex = savedStructIndex;
        ctx.DecrementStructDepth(structIndex);
        ctx.Depth--;
    }

    // ─── Helper: get last array field_id from context ──────────────
    private static ushort GetLastArrayFieldId(Stack<ArrayLoopState> arrayStack)
    {
        // The array field id was already stored by EmitArrayBegin before the array opcodes.
        // We don't track it separately; array count is tracked by EmitArrayBegin's field_id.
        return 0;
    }

    // ─── Helper: skip to matching ArrayEnd ─────────────────────────
    private static int SkipToArrayEnd(byte[] bytecode, int ip, int endOffset)
    {
        int depth = 1;
        while (ip < endOffset && depth > 0)
        {
            var op = (Opcode)bytecode[ip++];
            switch (op)
            {
                case Opcode.ArrayBeginCount:
                case Opcode.ArrayBeginUntil:
                case Opcode.ArrayBeginSentinel:
                case Opcode.ArrayBeginGreedy:
                    depth++;
                    break;
                case Opcode.ArrayEnd:
                    depth--;
                    if (depth == 0) return ip;
                    break;
                default:
                    ip += GetOpcodeOperandSize(op, bytecode, ip);
                    break;
            }
        }
        return ip;
    }

    // ─── Helper: get operand size for skipping ─────────────────────
    private static int GetOpcodeOperandSize(Opcode op, byte[] bytecode, int ip)
    {
        return op switch
        {
            // Tier 1 reads with field_id:u16
            Opcode.ReadU8 or Opcode.ReadI8 or Opcode.ReadU16Le or Opcode.ReadU16Be or
            Opcode.ReadI16Le or Opcode.ReadI16Be or Opcode.ReadU32Le or Opcode.ReadU32Be or
            Opcode.ReadI32Le or Opcode.ReadI32Be or Opcode.ReadU64Le or Opcode.ReadU64Be or
            Opcode.ReadI64Le or Opcode.ReadI64Be or Opcode.ReadF32Le or Opcode.ReadF32Be or
            Opcode.ReadF64Le or Opcode.ReadF64Be or Opcode.ReadBool
                => 2,
            Opcode.ReadFixedStr => 2 + 2 + 1, // field_id + len + encoding
            Opcode.ReadCString => 2 + 1,       // field_id + encoding
            Opcode.ReadBytesFixed => 2 + 4,     // field_id + len
            Opcode.SkipFixed => 2,
            Opcode.ReadPtrU32 or Opcode.ReadPtrU64 => 2,
            Opcode.AssertValue => ComputeAssertValueSize(bytecode, ip),

            Opcode.CallStruct => 2 + 1,        // struct_id + param_count
            Opcode.Return => 0,
            Opcode.Jump or Opcode.JumpIfFalse or Opcode.JumpIfTrue => 4,

            Opcode.SeekAbs or Opcode.SeekPush or Opcode.SeekPop => 0,

            Opcode.EmitStructBegin or Opcode.EmitArrayBegin or Opcode.EmitVariantBegin or
            Opcode.EmitBitsBegin => 4, // name_id + field_id
            Opcode.EmitStructEnd or Opcode.EmitArrayEnd or Opcode.EmitVariantEnd or
            Opcode.EmitBitsEnd => 0,
            Opcode.EmitNull => 2,

            Opcode.ReadBytesDyn => 2,
            Opcode.ReadStringDyn => 2 + 1,
            Opcode.ReadBits => 2 + 1,
            Opcode.ReadBit => 2,

            Opcode.PushConstI64 or Opcode.PushConstF64 => 8,
            Opcode.PushConstStr or Opcode.PushFieldVal or Opcode.PushParam or
            Opcode.StoreFieldVal or Opcode.PushFileParam => 2,
            Opcode.CopyChildField => 2 + 2,    // childFieldNameIdx + dstFieldId
            Opcode.PushRuntimeVar => 1,
            Opcode.PushIndex => 0,

            // Arithmetic/logic: no operands
            >= Opcode.OpAdd and <= Opcode.OpNeg => 0,

            Opcode.FnSizeOf or Opcode.FnOffsetOf or Opcode.FnCount or Opcode.FnStrLen => 2,
            Opcode.FnCrc32 or Opcode.FnAdler32 => 1,

            Opcode.StrStartsWith or Opcode.StrEndsWith or Opcode.StrContains => 0,

            // Array ops (no inline operands in Begin/Next/End themselves)
            Opcode.ArrayBeginCount or Opcode.ArrayBeginUntil or
            Opcode.ArrayBeginSentinel or Opcode.ArrayBeginGreedy or
            Opcode.ArrayNext or Opcode.ArrayEnd => 0,

            Opcode.MatchBegin or Opcode.MatchEnd => 0,
            Opcode.MatchArmEq => ComputeMatchArmEqSize(bytecode, ip),
            Opcode.MatchArmRange => ComputeMatchArmRangeSize(bytecode, ip),
            Opcode.MatchArmGuard => 4,
            Opcode.MatchDefault => 4,

            Opcode.Align => 0,
            Opcode.AlignFixed => 2,

            _ => 0,
        };
    }

    private static int ComputeAssertValueSize(byte[] bytecode, int ip)
    {
        // field_id:u16 already consumed, we start at type_tag
        int size = 2; // field_id
        if (ip + 2 < bytecode.Length)
        {
            byte typeTag = bytecode[ip + 2];
            size += 1; // type_tag
            size += typeTag switch
            {
                0 => 8,  // i64
                2 => 2,  // string_id
                3 => 1,  // bool
                _ => 0,
            };
        }
        return size;
    }

    private static int ComputeMatchArmEqSize(byte[] bytecode, int ip)
    {
        if (ip >= bytecode.Length) return 0;
        byte typeTag = bytecode[ip];
        int valSize = typeTag switch
        {
            0 => 8,
            2 => 2,
            3 => 1,
            _ => 0,
        };
        return 1 + valSize + 4; // type_tag + value + jump:i32
    }

    private static int ComputeMatchArmRangeSize(byte[] bytecode, int ip)
    {
        int total = 0;
        int pos = ip;
        // low
        if (pos < bytecode.Length)
        {
            byte tag = bytecode[pos++];
            total++;
            total += tag switch { 0 => 8, 2 => 2, 3 => 1, _ => 0 };
            pos += tag switch { 0 => 8, 2 => 2, 3 => 1, _ => 0 };
        }
        // high
        if (pos < bytecode.Length)
        {
            byte tag = bytecode[pos++];
            total++;
            total += tag switch { 0 => 8, 2 => 2, 3 => 1, _ => 0 };
        }
        total += 4; // jump:i32
        return total;
    }

    // ─── Emit helpers ──────────────────────────────────────────────
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EmitFieldInt(IResultEmitter emitter, BytecodeProgram program,
        StructMeta structMeta, ushort fid, long value)
    {
        if (fid < structMeta.Fields.Length)
        {
            var fm = structMeta.Fields[fid];
            if ((fm.Flags & FieldFlags.Hidden) != 0) return;
            emitter.EmitInt(program.GetString(fm.NameIndex), value, null);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EmitFieldUInt(IResultEmitter emitter, BytecodeProgram program,
        StructMeta structMeta, ushort fid, ulong value)
    {
        if (fid < structMeta.Fields.Length)
        {
            var fm = structMeta.Fields[fid];
            if ((fm.Flags & FieldFlags.Hidden) != 0) return;
            emitter.EmitUInt(program.GetString(fm.NameIndex), value, null);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EmitFieldFloat(IResultEmitter emitter, BytecodeProgram program,
        StructMeta structMeta, ushort fid, double value)
    {
        if (fid < structMeta.Fields.Length)
        {
            var fm = structMeta.Fields[fid];
            if ((fm.Flags & FieldFlags.Hidden) != 0) return;
            emitter.EmitFloat(program.GetString(fm.NameIndex), value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EmitFieldBool(IResultEmitter emitter, BytecodeProgram program,
        StructMeta structMeta, ushort fid, bool value)
    {
        if (fid < structMeta.Fields.Length)
        {
            var fm = structMeta.Fields[fid];
            if ((fm.Flags & FieldFlags.Hidden) != 0) return;
            emitter.EmitBool(program.GetString(fm.NameIndex), value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EmitFieldString(IResultEmitter emitter, BytecodeProgram program,
        StructMeta structMeta, ushort fid, string value)
    {
        if (fid < structMeta.Fields.Length)
        {
            var fm = structMeta.Fields[fid];
            if ((fm.Flags & FieldFlags.Hidden) != 0) return;
            emitter.EmitString(program.GetString(fm.NameIndex), value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EmitFieldBytes(IResultEmitter emitter, BytecodeProgram program,
        StructMeta structMeta, ushort fid, byte[] value)
    {
        if (fid < structMeta.Fields.Length)
        {
            var fm = structMeta.Fields[fid];
            if ((fm.Flags & FieldFlags.Hidden) != 0) return;
            emitter.EmitBytes(program.GetString(fm.NameIndex), value);
        }
    }

    // ─── Bytecode reading helpers ──────────────────────────────────
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadU16(byte[] bytecode, ref int ip)
    {
        ushort val = BinaryPrimitives.ReadUInt16LittleEndian(bytecode.AsSpan(ip, 2));
        ip += 2;
        return val;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadU32(byte[] bytecode, ref int ip)
    {
        uint val = BinaryPrimitives.ReadUInt32LittleEndian(bytecode.AsSpan(ip, 4));
        ip += 4;
        return val;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReadI32(byte[] bytecode, ref int ip)
    {
        int val = BinaryPrimitives.ReadInt32LittleEndian(bytecode.AsSpan(ip, 4));
        ip += 4;
        return val;
    }

    // ─── Input access helpers ──────────────────────────────────────
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<byte> input(ParseContext ctx) =>
        ctx.Input.Span.Slice((int)ctx.Position);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<byte> inputSpan(ParseContext ctx, int length) =>
        ctx.Input.Span.Slice((int)ctx.Position, length);

    // ─── String decoding ───────────────────────────────────────────
    private static string DecodeString(ReadOnlySpan<byte> bytes, StringEncoding encoding)
    {
        return encoding switch
        {
            StringEncoding.Utf8 => Encoding.UTF8.GetString(bytes),
            StringEncoding.Ascii => Encoding.ASCII.GetString(bytes),
            StringEncoding.Utf16Le => Encoding.Unicode.GetString(bytes),
            StringEncoding.Utf16Be => Encoding.BigEndianUnicode.GetString(bytes),
            StringEncoding.Latin1 => Encoding.Latin1.GetString(bytes),
            _ => Encoding.UTF8.GetString(bytes),
        };
    }

    // ─── Bit extraction ────────────────────────────────────────────
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ExtractBits(ParseContext ctx, int bitCount)
    {
        ulong mask = (1UL << bitCount) - 1;
        ulong val = (ctx.BitBuffer >> ctx.BitPosition) & mask;
        ctx.BitPosition += bitCount;
        return val;
    }

    // ─── Field name lookup ─────────────────────────────────────────
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetFieldName(BytecodeProgram program, StructMeta structMeta, ushort fid)
    {
        if (fid < structMeta.Fields.Length)
            return program.GetString(structMeta.Fields[fid].NameIndex);
        return "";
    }
}

/// <summary>
/// Exception thrown during parse execution.
/// </summary>
public sealed class ParseException : Exception
{
    public ParseException(string message) : base(message) { }
}
