namespace BinScript.Core.Runtime;

using BinScript.Core.Bytecode;

/// <summary>Stores snapshotted array elements for later .find()/.any()/.all() search.</summary>
public sealed class ArrayElementStore
{
    public List<FieldValueTable> Elements { get; } = new();
    public int StructIndex { get; set; } = -1;
}

/// <summary>Runtime state for an active array search iteration.</summary>
public sealed class ArraySearchState
{
    public ArrayElementStore Store { get; set; } = null!;
    public int CurrentIndex { get; set; }
    public FieldValueTable? MatchedElement { get; set; }
    public byte Mode { get; set; } // 0=find, 1=find_or, 2=any, 3=all
}

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

    // Array element storage for .find()/.any()/.all() — keyed by array's field ID, per scope
    private readonly Stack<Dictionary<ushort, ArrayElementStore>> _arrayElementsStack = new();

    // Pending forwarded array stores for the next CallStruct — keyed by destination param index
    private readonly Dictionary<int, ArrayElementStore> _pendingParamArrayStores = new();

    // Per-call-frame param array stores (pushed/popped with struct calls)
    private readonly Stack<Dictionary<int, ArrayElementStore>> _paramArrayStoreStack = new();

    // Search state stack for nested .find()/.any()/.all() calls
    private readonly Stack<ArraySearchState> _searchStack = new();

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

    // Array element storage (scope-aware)
    public void PushArrayElementScope() => _arrayElementsStack.Push(new Dictionary<ushort, ArrayElementStore>());
    public void PopArrayElementScope() => _arrayElementsStack.Pop();

    public void StoreArrayElement(ushort fieldId)
    {
        if (LastChildFieldTable == null) return;
        var scope = _arrayElementsStack.Peek();
        if (!scope.TryGetValue(fieldId, out var store))
        {
            store = new ArrayElementStore { StructIndex = LastChildStructIndex };
            scope[fieldId] = store;
        }
        store.Elements.Add(LastChildFieldTable.Clone());
    }

    public ArrayElementStore? GetArrayElementStore(ushort fieldId)
    {
        // Search current scope first, then parent scopes
        foreach (var scope in _arrayElementsStack)
        {
            if (scope.TryGetValue(fieldId, out var store))
                return store;
        }
        return null;
    }

    // Forwarding: parent → child array store passing
    public void SetPendingParamArrayStore(int dstParamIdx, ArrayElementStore store)
    {
        _pendingParamArrayStores[dstParamIdx] = store;
    }

    public Dictionary<int, ArrayElementStore> TakePendingParamArrayStores()
    {
        if (_pendingParamArrayStores.Count == 0)
            return new Dictionary<int, ArrayElementStore>();
        var result = new Dictionary<int, ArrayElementStore>(_pendingParamArrayStores);
        _pendingParamArrayStores.Clear();
        return result;
    }

    // Per-call-frame param array stores
    public void PushParamArrayStores(Dictionary<int, ArrayElementStore> stores)
        => _paramArrayStoreStack.Push(stores);
    public void PopParamArrayStores() => _paramArrayStoreStack.Pop();

    public ArrayElementStore? GetParamArrayStore(int paramIdx)
    {
        if (_paramArrayStoreStack.Count > 0)
        {
            var frame = _paramArrayStoreStack.Peek();
            if (frame.TryGetValue(paramIdx, out var store))
                return store;
        }
        return null;
    }

    // Search state operations
    public void PushSearch(ArraySearchState state) => _searchStack.Push(state);
    public ArraySearchState PopSearch() => _searchStack.Pop();
    public ArraySearchState PeekSearch() => _searchStack.Peek();
    public bool HasActiveSearch => _searchStack.Count > 0;
}
