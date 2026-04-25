namespace BinScript.Core.Runtime;

using BinScript.Core.Bytecode;

/// <summary>
/// Runtime state/context for the parse VM.
/// Holds the cursor, stacks, and per-struct field tables.
/// </summary>
public sealed class ParseContext
{
    public ReadOnlyMemory<byte> Input { get; }
    public long Position { get; set; }

    // Position stack for @at blocks (SeekPush/SeekPop)
    private readonly Stack<long> _positionStack = new();

    // Field value table per struct scope
    private readonly Stack<FieldValueTable> _fieldTableStack = new();

    // Evaluation stack for expressions
    private readonly Stack<StackValue> _evalStack = new(32);

    // Array index stack (for nested arrays, _index)
    private readonly Stack<long> _arrayIndexStack = new();

    // Parameter stack for nested struct calls
    private readonly Stack<StackValue[]> _paramStack = new();

    // Bit accumulator for bit-level reads
    public ulong BitBuffer { get; set; }
    public int BitPosition { get; set; }

    // Struct call depth tracking
    public int Depth { get; set; }

    // Per-struct depth counters for @max_depth enforcement
    private readonly Dictionary<int, int> _structDepth = new();

    // Current struct index being executed
    public int CurrentStructIndex { get; set; } = -1;

    // Array element storage for .find()/.any()/.all() methods
    // Maps field_id → list of per-element field tables (cloned after each struct array element)
    private readonly Dictionary<ushort, List<FieldValueTable>> _arrayElements = new();

    public ParseContext(ReadOnlyMemory<byte> input)
    {
        Input = input;
    }

    // Runtime variables
    public long InputSize => Input.Length;
    public long Offset => Position;
    public long Remaining => InputSize - Position;

    // Position stack operations
    public void PushPosition() => _positionStack.Push(Position);
    public void PopPosition() => Position = _positionStack.Pop();

    // Eval stack operations
    public void Push(StackValue value) => _evalStack.Push(value);
    public StackValue Pop() => _evalStack.Pop();
    public StackValue Peek() => _evalStack.Peek();
    public int EvalStackCount => _evalStack.Count;

    // Array index operations
    public void PushArrayIndex(long index) => _arrayIndexStack.Push(index);
    public long PopArrayIndex() => _arrayIndexStack.Pop();
    public long CurrentArrayIndex => _arrayIndexStack.Count > 0 ? _arrayIndexStack.Peek() : 0;
    public void SetCurrentArrayIndex(long index)
    {
        if (_arrayIndexStack.Count > 0)
        {
            _arrayIndexStack.Pop();
            _arrayIndexStack.Push(index);
        }
    }

    // Field table operations
    public void PushFieldTable(FieldValueTable table) => _fieldTableStack.Push(table);
    public FieldValueTable PopFieldTable() => _fieldTableStack.Pop();
    public FieldValueTable CurrentFieldTable => _fieldTableStack.Peek();

    // Last child field table — captured at end of ExecuteStruct for CopyChildField
    public FieldValueTable? LastChildFieldTable { get; set; }
    public int LastChildStructIndex { get; set; } = -1;

    // Parameter operations
    public void PushParams(StackValue[] parameters) => _paramStack.Push(parameters);
    public StackValue[] PopParams() => _paramStack.Pop();
    public StackValue[] CurrentParams => _paramStack.Count > 0 ? _paramStack.Peek() : [];

    // Per-struct depth tracking for @max_depth
    public int GetStructDepth(int structIndex)
    {
        return _structDepth.TryGetValue(structIndex, out int d) ? d : 0;
    }

    public void IncrementStructDepth(int structIndex)
    {
        _structDepth[structIndex] = GetStructDepth(structIndex) + 1;
    }

    public void DecrementStructDepth(int structIndex)
    {
        int d = GetStructDepth(structIndex);
        if (d > 1)
            _structDepth[structIndex] = d - 1;
        else
            _structDepth.Remove(structIndex);
    }

    // Array element storage
    public void InitArrayStore(ushort fieldId)
    {
        _arrayElements[fieldId] = new List<FieldValueTable>();
    }

    public void StoreArrayElement(ushort fieldId, FieldValueTable table)
    {
        if (_arrayElements.TryGetValue(fieldId, out var list))
            list.Add(table.Clone());
    }

    public List<FieldValueTable>? GetArrayElements(ushort fieldId)
    {
        return _arrayElements.TryGetValue(fieldId, out var list) ? list : null;
    }
}
