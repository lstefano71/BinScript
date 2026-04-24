namespace BinScript.Core.Api;

using BinScript.Core.Compiler;
using BinScript.Core.Compiler.Ast;
using BinScript.Core.Interfaces;
using BinScript.Core.Model;

/// <summary>
/// Public entry point for compiling BinScript source into a
/// <see cref="CompilationResult"/> (AST + diagnostics, and later bytecode).
/// </summary>
public sealed class BinScriptCompiler
{
    private readonly DictionaryModuleResolver _moduleResolver = new();

    /// <summary>Register an importable module by name.</summary>
    public void AddModule(string name, string source) => _moduleResolver.AddModule(name, source);

    /// <summary>
    /// Run the full compilation pipeline: Lex → Parse → TypeResolve → SemanticAnalyze.
    /// </summary>
    public CompilationResult Compile(
        string source,
        string filePath = "",
        Dictionary<string, object>? parameters = null)
    {
        var allDiagnostics = new List<Diagnostic>();

        // ── 1. Lex ──────────────────────────────────────────────────
        var lexer = new Lexer(source, filePath);
        var tokens = lexer.TokenizeAll();

        // ── 2. Parse ────────────────────────────────────────────────
        var parser = new Parser(tokens, filePath);
        var (file, parseDiags) = parser.Parse();
        allDiagnostics.AddRange(parseDiags);

        // Bail early if there are parse errors.
        if (allDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return new CompilationResult(null, file, allDiagnostics);

        // ── 3. Type resolution ──────────────────────────────────────
        var resolver = new TypeResolver();
        resolver.Resolve(file, _moduleResolver);
        allDiagnostics.AddRange(resolver.Diagnostics);

        // ── 4. Semantic analysis ────────────────────────────────────
        var analyzer = new SemanticAnalyzer();
        var semDiags = analyzer.Analyze(file, resolver);
        allDiagnostics.AddRange(semDiags);

        // ── 5. Bytecode emission ────────────────────────────────────
        if (allDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return new CompilationResult(null, file, allDiagnostics);

        var emitter = new BytecodeEmitter(file, resolver, parameters);
        var program = emitter.Emit();
        return new CompilationResult(program, file, allDiagnostics);
    }
}
