namespace BinScript.Interop;

/// <summary>
/// Thread-local error state for <c>binscript_last_error()</c>.
/// The last error string is owned by the interop layer and must NOT be freed by callers.
/// </summary>
public static class ErrorState
{
    [ThreadStatic]
    private static string? _lastError;

    /// <summary>Store an error message for the current thread.</summary>
    public static void Set(string message) => _lastError = message;

    /// <summary>Retrieve the last error message (or null if none).</summary>
    public static string? Get() => _lastError;

    /// <summary>Clear the last error for the current thread.</summary>
    public static void Clear() => _lastError = null;
}
