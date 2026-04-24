namespace BinScript.Core.Model;

/// <summary>
/// Options for controlling parse behavior.
/// </summary>
public sealed record ParseOptions
{
    /// <summary>Maximum nesting depth for struct calls.</summary>
    public int MaxDepth { get; init; } = 256;

    /// <summary>Maximum number of array elements before aborting.</summary>
    public int MaxArrayElements { get; init; } = 10_000_000;
}
