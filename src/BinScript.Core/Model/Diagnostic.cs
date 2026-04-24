using BinScript.Core.Compiler;

namespace BinScript.Core.Model;

public enum DiagnosticSeverity { Error, Warning, Info }

public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    SourceSpan Span);
