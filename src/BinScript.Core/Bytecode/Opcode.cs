namespace BinScript.Core.Bytecode;

public enum Opcode : byte
{
    // Tier 1 — Fast-Path Instructions
    ReadU8 = 0x01,
    ReadI8 = 0x02,
    ReadU16Le = 0x03,
    ReadU16Be = 0x04,
    ReadI16Le = 0x05,
    ReadI16Be = 0x06,
    ReadU32Le = 0x07,
    ReadU32Be = 0x08,
    ReadI32Le = 0x09,
    ReadI32Be = 0x0A,
    ReadU64Le = 0x0B,
    ReadU64Be = 0x0C,
    ReadI64Le = 0x0D,
    ReadI64Be = 0x0E,
    ReadF32Le = 0x0F,
    ReadF32Be = 0x10,
    ReadF64Le = 0x11,
    ReadF64Be = 0x12,
    ReadBool = 0x13,
    ReadFixedStr = 0x14,
    ReadCString = 0x15,
    ReadBytesFixed = 0x16,
    SkipFixed = 0x17,
    AssertValue = 0x18,

    // Tier 2 — Complex-Path Instructions
    // Control flow
    CallStruct = 0x40,
    Return = 0x41,
    Jump = 0x42,
    JumpIfFalse = 0x43,
    JumpIfTrue = 0x44,

    // Seeking
    SeekAbs = 0x50,
    SeekPush = 0x51,
    SeekPop = 0x52,

    // Emitter events
    EmitStructBegin = 0x60,
    EmitStructEnd = 0x61,
    EmitArrayBegin = 0x62,
    EmitArrayEnd = 0x63,
    EmitVariantBegin = 0x64,
    EmitVariantEnd = 0x65,
    EmitBitsBegin = 0x66,
    EmitBitsEnd = 0x67,

    // Dynamic reads
    ReadBytesDyn = 0x70,
    ReadStringDyn = 0x71,
    ReadBits = 0x72,
    ReadBit = 0x73,

    // Expression VM
    PushConstI64 = 0x80,
    PushConstF64 = 0x81,
    PushConstStr = 0x82,
    PushFieldVal = 0x83,
    PushParam = 0x84,
    PushRuntimeVar = 0x85,
    PushIndex = 0x86,
    StoreFieldVal = 0x87,
    PushFileParam = 0x88,

    // Arithmetic/logic
    OpAdd = 0x90,
    OpSub = 0x91,
    OpMul = 0x92,
    OpDiv = 0x93,
    OpMod = 0x94,
    OpAnd = 0x95,
    OpOr = 0x96,
    OpXor = 0x97,
    OpNot = 0x98,
    OpShl = 0x99,
    OpShr = 0x9A,
    OpEq = 0x9B,
    OpNe = 0x9C,
    OpLt = 0x9D,
    OpGt = 0x9E,
    OpLe = 0x9F,
    OpGe = 0xA0,
    OpLogicalAnd = 0xA1,
    OpLogicalOr = 0xA2,
    OpLogicalNot = 0xA3,
    OpNeg = 0xA4,

    // Built-in functions
    FnSizeOf = 0xB0,
    FnOffsetOf = 0xB1,
    FnCount = 0xB2,
    FnStrLen = 0xB3,
    FnCrc32 = 0xB4,
    FnAdler32 = 0xB5,

    // String methods
    StrStartsWith = 0xC0,
    StrEndsWith = 0xC1,
    StrContains = 0xC2,

    // Array control
    ArrayBeginCount = 0xD0,
    ArrayBeginUntil = 0xD1,
    ArrayBeginSentinel = 0xD2,
    ArrayBeginGreedy = 0xD3,
    ArrayNext = 0xD4,
    ArrayEnd = 0xD5,

    // Array search (.find()/.any()/.all())
    ArrayStoreElem = 0xD6,     // u16 arrayFieldId — snapshot child FVT for later search
    ArraySearchBegin = 0xD7,   // u16 arrayFieldId, u8 mode — start search iteration
    PushElemField = 0xD8,      // u16 fieldNameIdx — push field from current search element
    ArraySearchCheck = 0xD9,   // i32 loopTarget, i32 notFoundTarget — check pred, advance/break
    ArraySearchCopy = 0xDA,    // u16 srcFieldNameIdx, u16 dstFieldId — copy matched field to parent
    ArraySearchEnd = 0xDB,     // (no operands) — pop search state

    // Match
    MatchBegin = 0xE0,
    MatchArmEq = 0xE1,
    MatchArmRange = 0xE2,
    MatchArmGuard = 0xE3,
    MatchDefault = 0xE4,
    MatchEnd = 0xE5,

    // Alignment
    Align = 0xF0,
    AlignFixed = 0xF1,

    // Pointer operations
    ReadPtrU32 = 0x74,
    ReadPtrU64 = 0x75,
    EmitNull = 0x76,

    // Cross-struct field promotion (dotted access a.b)
    CopyChildField = 0x77,   // u16 srcFieldId, u16 dstFieldId — copy from last child field table to parent
}
