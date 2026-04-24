namespace BinScript.Core.Compiler;

using BinScript.Core.Bytecode;
using BinScript.Core.Compiler.Ast;
using BinScript.Core.Model;

/// <summary>
/// The result of compiling a BinScript source file through the full pipeline.
/// </summary>
public sealed record CompilationResult(
    BytecodeProgram? Program,
    ScriptFile? Ast,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    /// <summary>
    /// <c>true</c> when the AST was produced and no error-severity diagnostics exist.
    /// <see cref="Program"/> may still be <c>null</c> until the bytecode emitter is wired in.
    /// </summary>
    public bool Success =>
        Ast is not null &&
        !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}
