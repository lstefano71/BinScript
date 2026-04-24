namespace BinScript.Core.Model;

/// <summary>
/// Result of a produce (binary write) operation.
/// </summary>
public sealed record ProduceResult(
    bool Success,
    long BytesWritten,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    /// <summary>Output bytes when using ProduceAlloc.</summary>
    public byte[]? OutputBytes { get; init; }
}
