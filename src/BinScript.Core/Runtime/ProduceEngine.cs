namespace BinScript.Core.Runtime;

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using BinScript.Core.Bytecode;
using BinScript.Core.Interfaces;
using BinScript.Core.Model;

public sealed class ProduceException : Exception
{
    public ProduceException(string message) : base(message) { }
}

public sealed class ProduceEngine
{
    private struct ArrayLoopState
    {
        public long Count;
        public long Index;
        public int BodyStart;
    }

    public ProduceResult Produce(BytecodeProgram program, IDataSource dataSource,
        Span<byte> output, ParseOptions? options = null, int rootStructIndex = -1)
    {
        options ??= new ParseOptions();
        var diagnostics = new List<Diagnostic>();
        var ctx = new ParseContext(ReadOnlyMemory<byte>.Empty);
        int entryStruct = rootStructIndex >= 0 ? rootStructIndex : program.RootStructIndex;
        if (entryStruct < 0 || entryStruct >= program.Structs.Length)
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "PR001", "No root struct found.", default));
            return new ProduceResult(false, 0, diagnostics);
        }
        try
        {
            var structMeta = program.Structs[entryStruct];
            string rootName = program.GetString(structMeta.NameIndex);
            dataSource.EnterRoot(rootName);
            WriteStruct(program, ctx, dataSource, output, entryStruct, [], options, diagnostics);
            dataSource.ExitRoot();
        }
        catch (ProduceException ex)
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "PR100", ex.Message, default));
            return new ProduceResult(false, ctx.Position, diagnostics);
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, "PR101",
                $"Write past end of output at position {ctx.Position}.", default));
            return new ProduceResult(false, ctx.Position, diagnostics);
        }
        bool success = !diagnostics.Exists(d => d.Severity == DiagnosticSeverity.Error);
        return new ProduceResult(success, ctx.Position, diagnostics);
    }

    public (ProduceResult Result, byte[] Data) ProduceAlloc(BytecodeProgram program,
        IDataSource dataSource, ParseOptions? options = null, int rootStructIndex = -1)
    {
        long size = ComputeSize(program, dataSource, options, rootStructIndex);
        byte[] buffer = new byte[size];
        var result = Produce(program, dataSource, buffer.AsSpan(), options, rootStructIndex);
        return (result, buffer);
    }

    private long ComputeSize(BytecodeProgram program, IDataSource dataSource,
        ParseOptions? options, int rootStructIndex)
    {
        options ??= new ParseOptions();
        var ctx = new ParseContext(ReadOnlyMemory<byte>.Empty);
        int entryStruct = rootStructIndex >= 0 ? rootStructIndex : program.RootStructIndex;
        if (entryStruct < 0) return 0;
        var meta = program.Structs[entryStruct];
        if (meta.StaticSize >= 0) return meta.StaticSize;
        string rootName = program.GetString(meta.NameIndex);
        dataSource.EnterRoot(rootName);
        MeasureStruct(program, ctx, dataSource, entryStruct, [], options);
        dataSource.ExitRoot();
        return ctx.Position;
    }

    private void MeasureStruct(BytecodeProgram program, ParseContext ctx,
        IDataSource dataSource, int structIndex, StackValue[] parameters, ParseOptions options)
    {
        if (ctx.Depth >= options.MaxDepth) throw new ProduceException("Max nesting depth exceeded.");
        ctx.Depth++;
        var structMeta = program.Structs[structIndex];
        var fieldTable = new FieldValueTable(structMeta.Fields.Length + 16);
        ctx.PushFieldTable(fieldTable); ctx.PushParams(parameters);
        int savedStructIndex = ctx.CurrentStructIndex; ctx.CurrentStructIndex = structIndex;
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
                case Opcode.ReadU8: case Opcode.ReadI8: case Opcode.ReadBool:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  StackValue sv; if (opcode == Opcode.ReadBool) sv = StackValue.FromBool(dataSource.ReadBool(fname));
                  else if (opcode == Opcode.ReadI8) sv = StackValue.FromInt(dataSource.ReadInt(fname));
                  else sv = StackValue.FromInt((long)dataSource.ReadUInt(fname));
                  fieldTable.SetValue(fid, sv); fieldTable.SetOffset(fid, ctx.Position); fieldTable.SetSize(fid, 1);
                  ctx.Position++; break; }
                case Opcode.ReadU16Le: case Opcode.ReadU16Be: case Opcode.ReadI16Le: case Opcode.ReadI16Be:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  StackValue sv; if (opcode == Opcode.ReadI16Le || opcode == Opcode.ReadI16Be) sv = StackValue.FromInt(dataSource.ReadInt(fname));
                  else sv = StackValue.FromInt((long)dataSource.ReadUInt(fname));
                  fieldTable.SetValue(fid, sv); fieldTable.SetOffset(fid, ctx.Position); fieldTable.SetSize(fid, 2);
                  if (inBits) ctx.BitBuffer = (ulong)sv.IntValue; ctx.Position += 2; break; }
                case Opcode.ReadU32Le: case Opcode.ReadU32Be: case Opcode.ReadI32Le: case Opcode.ReadI32Be:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  StackValue sv; if (opcode == Opcode.ReadI32Le || opcode == Opcode.ReadI32Be) sv = StackValue.FromInt(dataSource.ReadInt(fname));
                  else sv = StackValue.FromInt((long)dataSource.ReadUInt(fname));
                  fieldTable.SetValue(fid, sv); fieldTable.SetOffset(fid, ctx.Position); fieldTable.SetSize(fid, 4);
                  if (inBits) ctx.BitBuffer = (ulong)sv.IntValue; ctx.Position += 4; break; }
                case Opcode.ReadU64Le: case Opcode.ReadU64Be: case Opcode.ReadI64Le: case Opcode.ReadI64Be:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  StackValue sv; if (opcode == Opcode.ReadI64Le || opcode == Opcode.ReadI64Be) sv = StackValue.FromInt(dataSource.ReadInt(fname));
                  else sv = StackValue.FromInt((long)dataSource.ReadUInt(fname));
                  fieldTable.SetValue(fid, sv); fieldTable.SetOffset(fid, ctx.Position); fieldTable.SetSize(fid, 8);
                  ctx.Position += 8; break; }
                case Opcode.ReadF32Le: case Opcode.ReadF32Be:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  var sv = StackValue.FromFloat(dataSource.ReadFloat(fname));
                  fieldTable.SetValue(fid, sv); fieldTable.SetOffset(fid, ctx.Position); fieldTable.SetSize(fid, 4);
                  ctx.Position += 4; break; }
                case Opcode.ReadF64Le: case Opcode.ReadF64Be:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  var sv = StackValue.FromFloat(dataSource.ReadFloat(fname));
                  fieldTable.SetValue(fid, sv); fieldTable.SetOffset(fid, ctx.Position); fieldTable.SetSize(fid, 8);
                  ctx.Position += 8; break; }
                case Opcode.ReadFixedStr:
                { ushort fid = ReadU16(bytecode, ref ip); ushort len = ReadU16(bytecode, ref ip); ip++;
                  string fname = GetFieldName(program, structMeta, fid);
                  fieldTable.SetValue(fid, StackValue.FromString(dataSource.ReadString(fname)));
                  fieldTable.SetOffset(fid, ctx.Position); fieldTable.SetSize(fid, len); ctx.Position += len; break; }
                case Opcode.ReadCString:
                { ushort fid = ReadU16(bytecode, ref ip); byte enc = bytecode[ip++];
                  string fname = GetFieldName(program, structMeta, fid);
                  string val = dataSource.ReadString(fname);
                  int byteLen = EncodeString(val, (StringEncoding)enc).Length + 1;
                  fieldTable.SetValue(fid, StackValue.FromString(val));
                  fieldTable.SetOffset(fid, ctx.Position); fieldTable.SetSize(fid, byteLen);
                  ctx.Position += byteLen; break; }
                case Opcode.ReadBytesFixed:
                { ushort fid = ReadU16(bytecode, ref ip); uint len = ReadU32(bytecode, ref ip);
                  string fname = GetFieldName(program, structMeta, fid);
                  fieldTable.SetValue(fid, StackValue.FromBytes(dataSource.ReadBytes(fname)));
                  fieldTable.SetOffset(fid, ctx.Position); fieldTable.SetSize(fid, len); ctx.Position += len; break; }
                case Opcode.SkipFixed:
                { ushort len = ReadU16(bytecode, ref ip); ctx.Position += len; break; }
                case Opcode.AssertValue:
                { ushort fid = ReadU16(bytecode, ref ip); byte tt = bytecode[ip++];
                  switch (tt) { case 0: ip += 8; break; case 2: ip += 2; break; case 3: ip++; break; } break; }
                case Opcode.CallStruct:
                { ushort sid = ReadU16(bytecode, ref ip); byte pc = bytecode[ip++];
                  var args = new StackValue[pc]; for (int i = pc - 1; i >= 0; i--) args[i] = ctx.Pop();
                  MeasureStruct(program, ctx, dataSource, sid, args, options); break; }
                case Opcode.Return: ip = endOffset; break;
                case Opcode.Jump: { int t = ReadI32(bytecode, ref ip); ip = baseOffset + t; break; }
                case Opcode.JumpIfFalse: { int t = ReadI32(bytecode, ref ip); if (!ctx.Pop().AsBool()) ip = baseOffset + t; break; }
                case Opcode.JumpIfTrue: { int t = ReadI32(bytecode, ref ip); if (ctx.Pop().AsBool()) ip = baseOffset + t; break; }
                case Opcode.SeekAbs: ctx.Position = ctx.Pop().AsLong(); break;
                case Opcode.SeekPush: ctx.PushPosition(); break;
                case Opcode.SeekPop: ctx.PopPosition(); break;
                case Opcode.EmitStructBegin:
                { ushort nid = ReadU16(bytecode, ref ip); ushort fid = ReadU16(bytecode, ref ip);
                  dataSource.EnterStruct(GetFieldName(program, structMeta, fid), program.GetString(nid)); break; }
                case Opcode.EmitStructEnd: dataSource.ExitStruct(); break;
                case Opcode.EmitArrayBegin:
                { ReadU16(bytecode, ref ip); ushort fid = ReadU16(bytecode, ref ip);
                  dataSource.EnterArray(GetFieldName(program, structMeta, fid)); break; }
                case Opcode.EmitArrayEnd: dataSource.ExitArray(); break;
                case Opcode.EmitVariantBegin:
                { ReadU16(bytecode, ref ip); ushort fid = ReadU16(bytecode, ref ip);
                  string fn = GetFieldName(program, structMeta, fid);
                  dataSource.EnterVariant(fn, dataSource.ReadVariantType(fn)); break; }
                case Opcode.EmitVariantEnd: dataSource.ExitVariant(); break;
                case Opcode.EmitBitsBegin:
                { ReadU16(bytecode, ref ip); ushort fid = ReadU16(bytecode, ref ip);
                  dataSource.EnterBitsStruct(GetFieldName(program, structMeta, fid));
                  ctx.BitBuffer = 0; ctx.BitPosition = 0; inBits = true; break; }
                case Opcode.EmitBitsEnd: dataSource.ExitBitsStruct(); ctx.BitPosition = 0; inBits = false; break;
                case Opcode.ReadBytesDyn:
                { ushort fid = ReadU16(bytecode, ref ip); long len = ctx.Pop().AsLong();
                  string fname = GetFieldName(program, structMeta, fid);
                  fieldTable.SetValue(fid, StackValue.FromBytes(dataSource.ReadBytes(fname)));
                  fieldTable.SetOffset(fid, ctx.Position); fieldTable.SetSize(fid, len); ctx.Position += len; break; }
                case Opcode.ReadStringDyn:
                { ushort fid = ReadU16(bytecode, ref ip); ip++; long len = ctx.Pop().AsLong();
                  string fname = GetFieldName(program, structMeta, fid);
                  fieldTable.SetValue(fid, StackValue.FromString(dataSource.ReadString(fname)));
                  fieldTable.SetOffset(fid, ctx.Position); fieldTable.SetSize(fid, len); ctx.Position += len; break; }
                case Opcode.ReadBits:
                { ushort fid = ReadU16(bytecode, ref ip); byte bc = bytecode[ip++];
                  string fname = GetFieldName(program, structMeta, fid);
                  fieldTable.SetValue(fid, StackValue.FromInt((long)dataSource.ReadBits(fname, bc)));
                  ctx.BitPosition += bc; break; }
                case Opcode.ReadBit:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  fieldTable.SetValue(fid, StackValue.FromBool(dataSource.ReadBit(fname))); ctx.BitPosition++; break; }
                case Opcode.PushConstI64:
                { ctx.Push(StackValue.FromInt(BinaryPrimitives.ReadInt64LittleEndian(bytecode.AsSpan(ip, 8)))); ip += 8; break; }
                case Opcode.PushConstF64:
                { ctx.Push(StackValue.FromFloat(BinaryPrimitives.ReadDoubleLittleEndian(bytecode.AsSpan(ip, 8)))); ip += 8; break; }
                case Opcode.PushConstStr:
                { ushort si = ReadU16(bytecode, ref ip); ctx.Push(StackValue.FromString(program.GetString(si))); break; }
                case Opcode.PushFieldVal: { ushort fid = ReadU16(bytecode, ref ip); ctx.Push(fieldTable.GetValue(fid)); break; }
                case Opcode.PushParam:
                { ushort pi = ReadU16(bytecode, ref ip); var p = ctx.CurrentParams;
                  ctx.Push(pi < p.Length ? p[pi] : StackValue.Zero); break; }
                case Opcode.PushRuntimeVar: { byte vi = bytecode[ip++]; ctx.Push(StackValue.FromInt(vi == 1 ? ctx.Offset : 0)); break; }
                case Opcode.PushIndex: ctx.Push(StackValue.FromInt(ctx.CurrentArrayIndex)); break;
                case Opcode.StoreFieldVal: { ushort fid = ReadU16(bytecode, ref ip); fieldTable.SetValue(fid, ctx.Pop()); break; }
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
                case Opcode.FnSizeOf: { ushort fid = ReadU16(bytecode, ref ip); BuiltinFunctions.SizeOf(ctx, fid); break; }
                case Opcode.FnOffsetOf: { ushort fid = ReadU16(bytecode, ref ip); BuiltinFunctions.OffsetOf(ctx, fid); break; }
                case Opcode.FnCount: { ushort fid = ReadU16(bytecode, ref ip); BuiltinFunctions.Count(ctx, fid); break; }
                case Opcode.FnStrLen: { ushort fid = ReadU16(bytecode, ref ip); BuiltinFunctions.StrLen(ctx, fid); break; }
                case Opcode.FnCrc32: { byte fc = bytecode[ip++]; BuiltinFunctions.ComputeCrc32(ctx, fc); break; }
                case Opcode.FnAdler32: { byte fc = bytecode[ip++]; BuiltinFunctions.ComputeAdler32(ctx, fc); break; }
                case Opcode.StrStartsWith: ExpressionEvaluator.StartsWith(ctx); break;
                case Opcode.StrEndsWith: ExpressionEvaluator.EndsWith(ctx); break;
                case Opcode.StrContains: ExpressionEvaluator.Contains(ctx); break;
                case Opcode.ArrayBeginCount:
                { long c = ctx.Pop().AsLong();
                  arrayStack.Push(new ArrayLoopState { Count = c, Index = 0, BodyStart = ip - baseOffset });
                  ctx.PushArrayIndex(0);
                  if (c <= 0) { ip = SkipToArrayEnd(bytecode, ip, endOffset); arrayStack.Pop(); ctx.PopArrayIndex(); }
                  else dataSource.EnterArrayElement(0); break; }
                case Opcode.ArrayBeginUntil: case Opcode.ArrayBeginSentinel: case Opcode.ArrayBeginGreedy:
                { int c = dataSource.GetArrayLength();
                  arrayStack.Push(new ArrayLoopState { Count = c, Index = 0, BodyStart = ip - baseOffset });
                  ctx.PushArrayIndex(0);
                  if (c <= 0) { ip = SkipToArrayEnd(bytecode, ip, endOffset); arrayStack.Pop(); ctx.PopArrayIndex(); }
                  else dataSource.EnterArrayElement(0); break; }
                case Opcode.ArrayNext:
                { if (arrayStack.Count > 0) { var s = arrayStack.Pop(); dataSource.ExitArrayElement(); s.Index++;
                  arrayStack.Push(s); ctx.SetCurrentArrayIndex(s.Index);
                  if (s.Index < s.Count) dataSource.EnterArrayElement((int)s.Index); } break; }
                case Opcode.ArrayEnd:
                { if (arrayStack.Count > 0) { var s = arrayStack.Peek();
                  if (s.Index < s.Count && s.Index < options.MaxArrayElements) ip = baseOffset + s.BodyStart;
                  else { arrayStack.Pop(); ctx.PopArrayIndex(); } } break; }
                case Opcode.MatchBegin: break;
                case Opcode.MatchArmEq:
                { var d = ctx.Peek(); byte tt = bytecode[ip++]; bool m = false;
                  switch (tt) { case 0: m = d.AsLong() == BinaryPrimitives.ReadInt64LittleEndian(bytecode.AsSpan(ip, 8)); ip += 8; break;
                  case 2: { ushort si = ReadU16(bytecode, ref ip); m = d.StringValue == program.GetString(si); break; }
                  case 3: m = d.AsBool() == (bytecode[ip++] != 0); break; }
                  int st = ReadI32(bytecode, ref ip); if (!m) ip = baseOffset + st; break; }
                case Opcode.MatchArmRange:
                { var d = ctx.Peek(); byte lt = bytecode[ip++]; long lo = 0;
                  switch (lt) { case 0: lo = BinaryPrimitives.ReadInt64LittleEndian(bytecode.AsSpan(ip, 8)); ip += 8; break; case 2: ip += 2; break; case 3: ip++; break; }
                  byte ht = bytecode[ip++]; long hi = 0;
                  switch (ht) { case 0: hi = BinaryPrimitives.ReadInt64LittleEndian(bytecode.AsSpan(ip, 8)); ip += 8; break; case 2: ip += 2; break; case 3: ip++; break; }
                  int st = ReadI32(bytecode, ref ip); long dv = d.AsLong(); if (dv < lo || dv > hi) ip = baseOffset + st; break; }
                case Opcode.MatchArmGuard: { ReadI32(bytecode, ref ip); break; }
                case Opcode.MatchDefault: { ReadI32(bytecode, ref ip); break; }
                case Opcode.MatchEnd: if (ctx.EvalStackCount > 0) ctx.Pop(); break;
                case Opcode.Align:
                { long a = ctx.Pop().AsLong(); if (a > 0) { long r = ctx.Position % a; if (r != 0) ctx.Position += a - r; } break; }
                case Opcode.AlignFixed:
                { ushort a = ReadU16(bytecode, ref ip); if (a > 0) { long r = ctx.Position % a; if (r != 0) ctx.Position += a - r; } break; }
                default: throw new ProduceException($"Unknown opcode 0x{(byte)opcode:X2} at ip={ip - 1}.");
            }
        }
        ctx.PopParams(); ctx.PopFieldTable(); ctx.CurrentStructIndex = savedStructIndex; ctx.Depth--;
    }

    private void WriteStruct(BytecodeProgram program, ParseContext ctx, IDataSource dataSource,
        Span<byte> output, int structIndex, StackValue[] parameters, ParseOptions options, List<Diagnostic> diagnostics)
    {
        if (ctx.Depth >= options.MaxDepth) throw new ProduceException("Max nesting depth exceeded.");
        ctx.Depth++;
        var structMeta = program.Structs[structIndex];
        var fieldTable = new FieldValueTable(structMeta.Fields.Length + 16);
        ctx.PushFieldTable(fieldTable); ctx.PushParams(parameters);
        int savedStructIndex = ctx.CurrentStructIndex; ctx.CurrentStructIndex = structIndex;
        byte[] bytecode = program.Bytecode;
        int baseOffset = structMeta.BytecodeOffset;
        int endOffset = baseOffset + structMeta.BytecodeLength;
        int ip = baseOffset;
        var arrayStack = new Stack<ArrayLoopState>();
        bool inBits = false; ulong bitAccumulator = 0; int bitPosition = 0;
        while (ip < endOffset)
        {
            var opcode = (Opcode)bytecode[ip++];
            switch (opcode)
            {
                case Opcode.ReadU8:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  long offset = ctx.Position; ulong val = dataSource.ReadUInt(fname);
                  output[(int)ctx.Position] = (byte)val;
                  fieldTable.SetValue(fid, StackValue.FromInt((long)val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, 1);
                  if (inBits) { bitAccumulator = val; ctx.BitBuffer = val; }
                  ctx.Position++; break; }
                case Opcode.ReadI8:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  long offset = ctx.Position; long val = dataSource.ReadInt(fname);
                  output[(int)ctx.Position] = (byte)(sbyte)val;
                  fieldTable.SetValue(fid, StackValue.FromInt(val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, 1); ctx.Position++; break; }
                case Opcode.ReadU16Le:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  long offset = ctx.Position; ulong val = dataSource.ReadUInt(fname);
                  BinaryPrimitives.WriteUInt16LittleEndian(output.Slice((int)ctx.Position, 2), (ushort)val);
                  fieldTable.SetValue(fid, StackValue.FromInt((long)val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, 2);
                  if (inBits) { bitAccumulator = val; ctx.BitBuffer = val; } ctx.Position += 2; break; }
                case Opcode.ReadU16Be:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  long offset = ctx.Position; ulong val = dataSource.ReadUInt(fname);
                  BinaryPrimitives.WriteUInt16BigEndian(output.Slice((int)ctx.Position, 2), (ushort)val);
                  fieldTable.SetValue(fid, StackValue.FromInt((long)val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, 2);
                  if (inBits) { bitAccumulator = val; ctx.BitBuffer = val; } ctx.Position += 2; break; }
                case Opcode.ReadI16Le:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  long offset = ctx.Position; long val = dataSource.ReadInt(fname);
                  BinaryPrimitives.WriteInt16LittleEndian(output.Slice((int)ctx.Position, 2), (short)val);
                  fieldTable.SetValue(fid, StackValue.FromInt(val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, 2); ctx.Position += 2; break; }
                case Opcode.ReadI16Be:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  long offset = ctx.Position; long val = dataSource.ReadInt(fname);
                  BinaryPrimitives.WriteInt16BigEndian(output.Slice((int)ctx.Position, 2), (short)val);
                  fieldTable.SetValue(fid, StackValue.FromInt(val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, 2); ctx.Position += 2; break; }
                case Opcode.ReadU32Le:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  long offset = ctx.Position; ulong val = dataSource.ReadUInt(fname);
                  BinaryPrimitives.WriteUInt32LittleEndian(output.Slice((int)ctx.Position, 4), (uint)val);
                  fieldTable.SetValue(fid, StackValue.FromInt((long)val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, 4);
                  if (inBits) { bitAccumulator = val; ctx.BitBuffer = val; } ctx.Position += 4; break; }
                case Opcode.ReadU32Be:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  long offset = ctx.Position; ulong val = dataSource.ReadUInt(fname);
                  BinaryPrimitives.WriteUInt32BigEndian(output.Slice((int)ctx.Position, 4), (uint)val);
                  fieldTable.SetValue(fid, StackValue.FromInt((long)val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, 4);
                  if (inBits) { bitAccumulator = val; ctx.BitBuffer = val; } ctx.Position += 4; break; }
                case Opcode.ReadI32Le:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  long offset = ctx.Position; long val = dataSource.ReadInt(fname);
                  BinaryPrimitives.WriteInt32LittleEndian(output.Slice((int)ctx.Position, 4), (int)val);
                  fieldTable.SetValue(fid, StackValue.FromInt(val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, 4); ctx.Position += 4; break; }
                case Opcode.ReadI32Be:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  long offset = ctx.Position; long val = dataSource.ReadInt(fname);
                  BinaryPrimitives.WriteInt32BigEndian(output.Slice((int)ctx.Position, 4), (int)val);
                  fieldTable.SetValue(fid, StackValue.FromInt(val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, 4); ctx.Position += 4; break; }
                case Opcode.ReadU64Le:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  long offset = ctx.Position; ulong val = dataSource.ReadUInt(fname);
                  BinaryPrimitives.WriteUInt64LittleEndian(output.Slice((int)ctx.Position, 8), val);
                  fieldTable.SetValue(fid, StackValue.FromInt((long)val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, 8); ctx.Position += 8; break; }
                case Opcode.ReadU64Be:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  long offset = ctx.Position; ulong val = dataSource.ReadUInt(fname);
                  BinaryPrimitives.WriteUInt64BigEndian(output.Slice((int)ctx.Position, 8), val);
                  fieldTable.SetValue(fid, StackValue.FromInt((long)val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, 8); ctx.Position += 8; break; }
                case Opcode.ReadI64Le:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  long offset = ctx.Position; long val = dataSource.ReadInt(fname);
                  BinaryPrimitives.WriteInt64LittleEndian(output.Slice((int)ctx.Position, 8), val);
                  fieldTable.SetValue(fid, StackValue.FromInt(val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, 8); ctx.Position += 8; break; }
                case Opcode.ReadI64Be:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  long offset = ctx.Position; long val = dataSource.ReadInt(fname);
                  BinaryPrimitives.WriteInt64BigEndian(output.Slice((int)ctx.Position, 8), val);
                  fieldTable.SetValue(fid, StackValue.FromInt(val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, 8); ctx.Position += 8; break; }
                case Opcode.ReadF32Le:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  long offset = ctx.Position; double val = dataSource.ReadFloat(fname);
                  BinaryPrimitives.WriteSingleLittleEndian(output.Slice((int)ctx.Position, 4), (float)val);
                  fieldTable.SetValue(fid, StackValue.FromFloat(val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, 4); ctx.Position += 4; break; }
                case Opcode.ReadF32Be:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  long offset = ctx.Position; double val = dataSource.ReadFloat(fname);
                  BinaryPrimitives.WriteSingleBigEndian(output.Slice((int)ctx.Position, 4), (float)val);
                  fieldTable.SetValue(fid, StackValue.FromFloat(val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, 4); ctx.Position += 4; break; }
                case Opcode.ReadF64Le:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  long offset = ctx.Position; double val = dataSource.ReadFloat(fname);
                  BinaryPrimitives.WriteDoubleLittleEndian(output.Slice((int)ctx.Position, 8), val);
                  fieldTable.SetValue(fid, StackValue.FromFloat(val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, 8); ctx.Position += 8; break; }
                case Opcode.ReadF64Be:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  long offset = ctx.Position; double val = dataSource.ReadFloat(fname);
                  BinaryPrimitives.WriteDoubleBigEndian(output.Slice((int)ctx.Position, 8), val);
                  fieldTable.SetValue(fid, StackValue.FromFloat(val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, 8); ctx.Position += 8; break; }
                case Opcode.ReadBool:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  long offset = ctx.Position; bool val = dataSource.ReadBool(fname);
                  output[(int)ctx.Position] = val ? (byte)1 : (byte)0;
                  fieldTable.SetValue(fid, StackValue.FromBool(val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, 1); ctx.Position++; break; }
                case Opcode.ReadFixedStr:
                { ushort fid = ReadU16(bytecode, ref ip); ushort len = ReadU16(bytecode, ref ip); byte enc = bytecode[ip++];
                  string fname = GetFieldName(program, structMeta, fid); long offset = ctx.Position;
                  string val = dataSource.ReadString(fname);
                  byte[] encoded = EncodeString(val, (StringEncoding)enc);
                  var dest = output.Slice((int)ctx.Position, len); dest.Clear();
                  encoded.AsSpan(0, Math.Min(encoded.Length, len)).CopyTo(dest);
                  fieldTable.SetValue(fid, StackValue.FromString(val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, len); ctx.Position += len; break; }
                case Opcode.ReadCString:
                { ushort fid = ReadU16(bytecode, ref ip); byte enc = bytecode[ip++];
                  string fname = GetFieldName(program, structMeta, fid); long offset = ctx.Position;
                  string val = dataSource.ReadString(fname);
                  byte[] encoded = EncodeString(val, (StringEncoding)enc);
                  encoded.AsSpan().CopyTo(output.Slice((int)ctx.Position));
                  ctx.Position += encoded.Length; output[(int)ctx.Position] = 0; ctx.Position++;
                  int totalLen = encoded.Length + 1;
                  fieldTable.SetValue(fid, StackValue.FromString(val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, totalLen); break; }
                case Opcode.ReadBytesFixed:
                { ushort fid = ReadU16(bytecode, ref ip); uint len = ReadU32(bytecode, ref ip);
                  string fname = GetFieldName(program, structMeta, fid); long offset = ctx.Position;
                  byte[] val = dataSource.ReadBytes(fname);
                  var dest = output.Slice((int)ctx.Position, (int)len); dest.Clear();
                  val.AsSpan(0, Math.Min(val.Length, (int)len)).CopyTo(dest);
                  fieldTable.SetValue(fid, StackValue.FromBytes(val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, len); ctx.Position += len; break; }
                case Opcode.SkipFixed:
                { ushort len = ReadU16(bytecode, ref ip);
                  output.Slice((int)ctx.Position, len).Clear(); ctx.Position += len; break; }
                case Opcode.AssertValue:
                { ushort fid = ReadU16(bytecode, ref ip); byte tt = bytecode[ip++];
                  switch (tt) { case 0: ip += 8; break; case 2: ip += 2; break; case 3: ip++; break; } break; }
                case Opcode.CallStruct:
                { ushort sid = ReadU16(bytecode, ref ip); byte pc = bytecode[ip++];
                  var args = new StackValue[pc]; for (int i = pc - 1; i >= 0; i--) args[i] = ctx.Pop();
                  WriteStruct(program, ctx, dataSource, output, sid, args, options, diagnostics); break; }
                case Opcode.Return: ip = endOffset; break;
                case Opcode.Jump: { int t = ReadI32(bytecode, ref ip); ip = baseOffset + t; break; }
                case Opcode.JumpIfFalse: { int t = ReadI32(bytecode, ref ip); if (!ctx.Pop().AsBool()) ip = baseOffset + t; break; }
                case Opcode.JumpIfTrue: { int t = ReadI32(bytecode, ref ip); if (ctx.Pop().AsBool()) ip = baseOffset + t; break; }
                case Opcode.SeekAbs: ctx.Position = ctx.Pop().AsLong(); break;
                case Opcode.SeekPush: ctx.PushPosition(); break;
                case Opcode.SeekPop: ctx.PopPosition(); break;
                case Opcode.EmitStructBegin:
                { ushort nid = ReadU16(bytecode, ref ip); ushort fid = ReadU16(bytecode, ref ip);
                  dataSource.EnterStruct(GetFieldName(program, structMeta, fid), program.GetString(nid)); break; }
                case Opcode.EmitStructEnd: dataSource.ExitStruct(); break;
                case Opcode.EmitArrayBegin:
                { ReadU16(bytecode, ref ip); ushort fid = ReadU16(bytecode, ref ip);
                  dataSource.EnterArray(GetFieldName(program, structMeta, fid)); break; }
                case Opcode.EmitArrayEnd: dataSource.ExitArray(); break;
                case Opcode.EmitVariantBegin:
                { ReadU16(bytecode, ref ip); ushort fid = ReadU16(bytecode, ref ip);
                  string fn = GetFieldName(program, structMeta, fid);
                  dataSource.EnterVariant(fn, dataSource.ReadVariantType(fn)); break; }
                case Opcode.EmitVariantEnd: dataSource.ExitVariant(); break;
                case Opcode.EmitBitsBegin:
                { ReadU16(bytecode, ref ip); ushort fid = ReadU16(bytecode, ref ip);
                  dataSource.EnterBitsStruct(GetFieldName(program, structMeta, fid));
                  bitAccumulator = 0; bitPosition = 0; ctx.BitBuffer = 0; ctx.BitPosition = 0; inBits = true; break; }
                case Opcode.EmitBitsEnd: dataSource.ExitBitsStruct(); ctx.BitPosition = 0; inBits = false; break;
                case Opcode.ReadBytesDyn:
                { ushort fid = ReadU16(bytecode, ref ip); long len = ctx.Pop().AsLong();
                  string fname = GetFieldName(program, structMeta, fid); long offset = ctx.Position;
                  byte[] val = dataSource.ReadBytes(fname);
                  var dest = output.Slice((int)ctx.Position, (int)len); dest.Clear();
                  val.AsSpan(0, Math.Min(val.Length, (int)len)).CopyTo(dest);
                  fieldTable.SetValue(fid, StackValue.FromBytes(val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, len); ctx.Position += len; break; }
                case Opcode.ReadStringDyn:
                { ushort fid = ReadU16(bytecode, ref ip); byte enc = bytecode[ip++]; long len = ctx.Pop().AsLong();
                  string fname = GetFieldName(program, structMeta, fid); long offset = ctx.Position;
                  string val = dataSource.ReadString(fname);
                  byte[] encoded = EncodeString(val, (StringEncoding)enc);
                  var dest = output.Slice((int)ctx.Position, (int)len); dest.Clear();
                  encoded.AsSpan(0, Math.Min(encoded.Length, (int)len)).CopyTo(dest);
                  fieldTable.SetValue(fid, StackValue.FromString(val));
                  fieldTable.SetOffset(fid, offset); fieldTable.SetSize(fid, len); ctx.Position += len; break; }
                case Opcode.ReadBits:
                { ushort fid = ReadU16(bytecode, ref ip); byte bc = bytecode[ip++];
                  string fname = GetFieldName(program, structMeta, fid);
                  ulong val = dataSource.ReadBits(fname, bc); ulong mask = (1UL << bc) - 1;
                  bitAccumulator |= (val & mask) << bitPosition; bitPosition += bc;
                  fieldTable.SetValue(fid, StackValue.FromInt((long)val)); ctx.BitPosition = bitPosition; break; }
                case Opcode.ReadBit:
                { ushort fid = ReadU16(bytecode, ref ip); string fname = GetFieldName(program, structMeta, fid);
                  bool val = dataSource.ReadBit(fname);
                  if (val) bitAccumulator |= 1UL << bitPosition; bitPosition++;
                  fieldTable.SetValue(fid, StackValue.FromBool(val)); ctx.BitPosition = bitPosition; break; }
                case Opcode.PushConstI64:
                { ctx.Push(StackValue.FromInt(BinaryPrimitives.ReadInt64LittleEndian(bytecode.AsSpan(ip, 8)))); ip += 8; break; }
                case Opcode.PushConstF64:
                { ctx.Push(StackValue.FromFloat(BinaryPrimitives.ReadDoubleLittleEndian(bytecode.AsSpan(ip, 8)))); ip += 8; break; }
                case Opcode.PushConstStr:
                { ushort si = ReadU16(bytecode, ref ip); ctx.Push(StackValue.FromString(program.GetString(si))); break; }
                case Opcode.PushFieldVal: { ushort fid = ReadU16(bytecode, ref ip); ctx.Push(fieldTable.GetValue(fid)); break; }
                case Opcode.PushParam:
                { ushort pi = ReadU16(bytecode, ref ip); var p = ctx.CurrentParams;
                  ctx.Push(pi < p.Length ? p[pi] : StackValue.Zero); break; }
                case Opcode.PushRuntimeVar: { byte vi = bytecode[ip++]; ctx.Push(StackValue.FromInt(vi == 1 ? ctx.Offset : 0)); break; }
                case Opcode.PushIndex: ctx.Push(StackValue.FromInt(ctx.CurrentArrayIndex)); break;
                case Opcode.StoreFieldVal: { ushort fid = ReadU16(bytecode, ref ip); fieldTable.SetValue(fid, ctx.Pop()); break; }
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
                case Opcode.FnSizeOf: { ushort fid = ReadU16(bytecode, ref ip); BuiltinFunctions.SizeOf(ctx, fid); break; }
                case Opcode.FnOffsetOf: { ushort fid = ReadU16(bytecode, ref ip); BuiltinFunctions.OffsetOf(ctx, fid); break; }
                case Opcode.FnCount: { ushort fid = ReadU16(bytecode, ref ip); BuiltinFunctions.Count(ctx, fid); break; }
                case Opcode.FnStrLen: { ushort fid = ReadU16(bytecode, ref ip); BuiltinFunctions.StrLen(ctx, fid); break; }
                case Opcode.FnCrc32: { byte fc = bytecode[ip++]; BuiltinFunctions.ComputeCrc32(ctx, fc); break; }
                case Opcode.FnAdler32: { byte fc = bytecode[ip++]; BuiltinFunctions.ComputeAdler32(ctx, fc); break; }
                case Opcode.StrStartsWith: ExpressionEvaluator.StartsWith(ctx); break;
                case Opcode.StrEndsWith: ExpressionEvaluator.EndsWith(ctx); break;
                case Opcode.StrContains: ExpressionEvaluator.Contains(ctx); break;
                case Opcode.ArrayBeginCount:
                { long c = ctx.Pop().AsLong(); fieldTable.SetArrayCount(0, c);
                  arrayStack.Push(new ArrayLoopState { Count = c, Index = 0, BodyStart = ip - baseOffset });
                  ctx.PushArrayIndex(0);
                  if (c <= 0) { ip = SkipToArrayEnd(bytecode, ip, endOffset); arrayStack.Pop(); ctx.PopArrayIndex(); }
                  else dataSource.EnterArrayElement(0); break; }
                case Opcode.ArrayBeginUntil: case Opcode.ArrayBeginSentinel: case Opcode.ArrayBeginGreedy:
                { int c = dataSource.GetArrayLength();
                  arrayStack.Push(new ArrayLoopState { Count = c, Index = 0, BodyStart = ip - baseOffset });
                  ctx.PushArrayIndex(0);
                  if (c <= 0) { ip = SkipToArrayEnd(bytecode, ip, endOffset); arrayStack.Pop(); ctx.PopArrayIndex(); }
                  else dataSource.EnterArrayElement(0); break; }
                case Opcode.ArrayNext:
                { if (arrayStack.Count > 0) { var s = arrayStack.Pop(); dataSource.ExitArrayElement(); s.Index++;
                  arrayStack.Push(s); ctx.SetCurrentArrayIndex(s.Index);
                  if (s.Index < s.Count) dataSource.EnterArrayElement((int)s.Index); } break; }
                case Opcode.ArrayEnd:
                { if (arrayStack.Count > 0) { var s = arrayStack.Peek();
                  if (s.Index < s.Count && s.Index < options.MaxArrayElements) ip = baseOffset + s.BodyStart;
                  else { arrayStack.Pop(); ctx.PopArrayIndex(); } } break; }
                case Opcode.MatchBegin: break;
                case Opcode.MatchArmEq:
                { var d = ctx.Peek(); byte tt = bytecode[ip++]; bool m = false;
                  switch (tt) { case 0: m = d.AsLong() == BinaryPrimitives.ReadInt64LittleEndian(bytecode.AsSpan(ip, 8)); ip += 8; break;
                  case 2: { ushort si = ReadU16(bytecode, ref ip); m = d.StringValue == program.GetString(si); break; }
                  case 3: m = d.AsBool() == (bytecode[ip++] != 0); break; }
                  int st = ReadI32(bytecode, ref ip); if (!m) ip = baseOffset + st; break; }
                case Opcode.MatchArmRange:
                { var d = ctx.Peek(); byte lt = bytecode[ip++]; long lo = 0;
                  switch (lt) { case 0: lo = BinaryPrimitives.ReadInt64LittleEndian(bytecode.AsSpan(ip, 8)); ip += 8; break; case 2: ip += 2; break; case 3: ip++; break; }
                  byte ht = bytecode[ip++]; long hi = 0;
                  switch (ht) { case 0: hi = BinaryPrimitives.ReadInt64LittleEndian(bytecode.AsSpan(ip, 8)); ip += 8; break; case 2: ip += 2; break; case 3: ip++; break; }
                  int st = ReadI32(bytecode, ref ip); long dv = d.AsLong(); if (dv < lo || dv > hi) ip = baseOffset + st; break; }
                case Opcode.MatchArmGuard: { ReadI32(bytecode, ref ip); break; }
                case Opcode.MatchDefault: { ReadI32(bytecode, ref ip); break; }
                case Opcode.MatchEnd: if (ctx.EvalStackCount > 0) ctx.Pop(); break;
                case Opcode.Align:
                { long a = ctx.Pop().AsLong(); if (a > 0) { long r = ctx.Position % a;
                  if (r != 0) { int pad = (int)(a - r); output.Slice((int)ctx.Position, pad).Clear(); ctx.Position += pad; } } break; }
                case Opcode.AlignFixed:
                { ushort a = ReadU16(bytecode, ref ip); if (a > 0) { long r = ctx.Position % a;
                  if (r != 0) { int pad = (int)(a - r); output.Slice((int)ctx.Position, pad).Clear(); ctx.Position += pad; } } break; }
                default: throw new ProduceException($"Unknown opcode 0x{(byte)opcode:X2} at ip={ip - 1}.");
            }
        }
        ctx.PopParams(); ctx.PopFieldTable(); ctx.CurrentStructIndex = savedStructIndex; ctx.Depth--;
    }

    private static int SkipToArrayEnd(byte[] bytecode, int ip, int endOffset)
    {
        int depth = 1;
        while (ip < endOffset && depth > 0)
        {
            var op = (Opcode)bytecode[ip++];
            switch (op)
            {
                case Opcode.ArrayBeginCount: case Opcode.ArrayBeginUntil:
                case Opcode.ArrayBeginSentinel: case Opcode.ArrayBeginGreedy: depth++; break;
                case Opcode.ArrayEnd: depth--; if (depth == 0) return ip; break;
                default: ip += GetOpcodeOperandSize(op, bytecode, ip); break;
            }
        }
        return ip;
    }

    private static int GetOpcodeOperandSize(Opcode op, byte[] bytecode, int ip)
    {
        return op switch
        {
            Opcode.ReadU8 or Opcode.ReadI8 or Opcode.ReadU16Le or Opcode.ReadU16Be or
            Opcode.ReadI16Le or Opcode.ReadI16Be or Opcode.ReadU32Le or Opcode.ReadU32Be or
            Opcode.ReadI32Le or Opcode.ReadI32Be or Opcode.ReadU64Le or Opcode.ReadU64Be or
            Opcode.ReadI64Le or Opcode.ReadI64Be or Opcode.ReadF32Le or Opcode.ReadF32Be or
            Opcode.ReadF64Le or Opcode.ReadF64Be or Opcode.ReadBool => 2,
            Opcode.ReadFixedStr => 5, Opcode.ReadCString => 3, Opcode.ReadBytesFixed => 6,
            Opcode.SkipFixed => 2,
            Opcode.AssertValue => ComputeAssertValueSize(bytecode, ip),
            Opcode.CallStruct => 3, Opcode.Return => 0,
            Opcode.Jump or Opcode.JumpIfFalse or Opcode.JumpIfTrue => 4,
            Opcode.SeekAbs or Opcode.SeekPush or Opcode.SeekPop => 0,
            Opcode.EmitStructBegin or Opcode.EmitArrayBegin or Opcode.EmitVariantBegin or Opcode.EmitBitsBegin => 4,
            Opcode.EmitStructEnd or Opcode.EmitArrayEnd or Opcode.EmitVariantEnd or Opcode.EmitBitsEnd => 0,
            Opcode.ReadBytesDyn => 2, Opcode.ReadStringDyn => 3, Opcode.ReadBits => 3, Opcode.ReadBit => 2,
            Opcode.PushConstI64 or Opcode.PushConstF64 => 8,
            Opcode.PushConstStr or Opcode.PushFieldVal or Opcode.PushParam or Opcode.StoreFieldVal => 2,
            Opcode.PushRuntimeVar => 1, Opcode.PushIndex => 0,
            >= Opcode.OpAdd and <= Opcode.OpNeg => 0,
            Opcode.FnSizeOf or Opcode.FnOffsetOf or Opcode.FnCount or Opcode.FnStrLen => 2,
            Opcode.FnCrc32 or Opcode.FnAdler32 => 1,
            Opcode.StrStartsWith or Opcode.StrEndsWith or Opcode.StrContains => 0,
            Opcode.ArrayBeginCount or Opcode.ArrayBeginUntil or Opcode.ArrayBeginSentinel or
            Opcode.ArrayBeginGreedy or Opcode.ArrayNext or Opcode.ArrayEnd => 0,
            Opcode.MatchBegin or Opcode.MatchEnd => 0,
            Opcode.MatchArmEq => ComputeMatchArmEqSize(bytecode, ip),
            Opcode.MatchArmRange => ComputeMatchArmRangeSize(bytecode, ip),
            Opcode.MatchArmGuard or Opcode.MatchDefault => 4,
            Opcode.Align => 0, Opcode.AlignFixed => 2,
            _ => 0,
        };
    }

    private static int ComputeAssertValueSize(byte[] bytecode, int ip)
    {
        int size = 2;
        if (ip + 2 < bytecode.Length)
        { byte tt = bytecode[ip + 2]; size += 1; size += tt switch { 0 => 8, 2 => 2, 3 => 1, _ => 0 }; }
        return size;
    }

    private static int ComputeMatchArmEqSize(byte[] bytecode, int ip)
    {
        if (ip >= bytecode.Length) return 0;
        byte tt = bytecode[ip];
        return 1 + (tt switch { 0 => 8, 2 => 2, 3 => 1, _ => 0 }) + 4;
    }

    private static int ComputeMatchArmRangeSize(byte[] bytecode, int ip)
    {
        int total = 0; int pos = ip;
        if (pos < bytecode.Length) { byte t = bytecode[pos++]; total++; int s = t switch { 0 => 8, 2 => 2, 3 => 1, _ => 0 }; total += s; pos += s; }
        if (pos < bytecode.Length) { byte t = bytecode[pos++]; total++; total += t switch { 0 => 8, 2 => 2, 3 => 1, _ => 0 }; }
        total += 4; return total;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadU16(byte[] bytecode, ref int ip)
    { ushort v = BinaryPrimitives.ReadUInt16LittleEndian(bytecode.AsSpan(ip, 2)); ip += 2; return v; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadU32(byte[] bytecode, ref int ip)
    { uint v = BinaryPrimitives.ReadUInt32LittleEndian(bytecode.AsSpan(ip, 4)); ip += 4; return v; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReadI32(byte[] bytecode, ref int ip)
    { int v = BinaryPrimitives.ReadInt32LittleEndian(bytecode.AsSpan(ip, 4)); ip += 4; return v; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetFieldName(BytecodeProgram program, StructMeta structMeta, ushort fid)
    {
        if (fid < structMeta.Fields.Length) return program.GetString(structMeta.Fields[fid].NameIndex);
        return "";
    }

    private static byte[] EncodeString(string value, StringEncoding encoding)
    {
        return encoding switch
        {
            StringEncoding.Utf8 => Encoding.UTF8.GetBytes(value),
            StringEncoding.Ascii => Encoding.ASCII.GetBytes(value),
            StringEncoding.Utf16Le => Encoding.Unicode.GetBytes(value),
            StringEncoding.Utf16Be => Encoding.BigEndianUnicode.GetBytes(value),
            StringEncoding.Latin1 => Encoding.Latin1.GetBytes(value),
            _ => Encoding.UTF8.GetBytes(value),
        };
    }
}
