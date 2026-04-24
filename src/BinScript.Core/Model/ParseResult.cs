namespace BinScript.Core.Model;

/// <summary>
/// Result of a parse operation.
/// </summary>
public sealed record ParseResult(
    bool Success,
    string? Output,
    IReadOnlyList<Diagnostic> Diagnostics,
    long BytesConsumed,
    long InputSize);
