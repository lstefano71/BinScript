using BinScript.Core.Compiler;
using BinScript.Core.Compiler.Ast;
using BinScript.Core.Model;

namespace BinScript.NALight;

/// <summary>
/// Result of compiling a BSX-Light spec string.
/// Includes the compiled BinScript program plus metadata about the FFI call.
/// </summary>
public sealed record NACompilationResult(
    CompilationResult Inner,
    NASpec Spec,
    string PlainNA)
{
    /// <summary>Whether compilation succeeded with no errors.</summary>
    public bool Success => Inner.Success;

    /// <summary>Compilation diagnostics (errors and warnings).</summary>
    public IReadOnlyList<Diagnostic> Diagnostics => Inner.Diagnostics;
}

/// <summary>
/// Public entry point for BSX-Light compilation.
/// Compiles ⎕NA superset spec strings into BinScript bytecode by generating
/// BinScript AST nodes and feeding them through the standard compilation pipeline
/// (TypeResolver → SemanticAnalyzer → BytecodeEmitter).
/// </summary>
public static class NALightCompiler
{
    /// <summary>
    /// Compile a BSX-Light spec string.
    /// </summary>
    /// <param name="spec">The ⎕NA superset spec string, e.g.
    /// <c>"I4 shell32|SHFileOperationW* ={P U4 &lt;00T &lt;00T U2 I4 P &lt;0T}"</c>.</param>
    /// <returns>Compilation result with the compiled program and metadata.</returns>
    public static NACompilationResult Compile(string spec)
    {
        // 1. Lex
        var lexer = new NALexer(spec);
        var tokens = lexer.Tokenize();

        // 2. Parse → intermediate representation
        var parser = new NAParser(tokens);
        var naSpec = parser.Parse();

        // 3. Generate BinScript AST directly
        var codeGen = new NACodeGen(naSpec);
        var scriptFile = codeGen.ToScriptFile();
        string plainNA = codeGen.ToPlainNA();

        // 4. Run through the standard BinScript compilation pipeline
        //    (skipping Lexer and Parser — we already have the AST)
        var allDiagnostics = new List<Diagnostic>();

        // Type resolution
        var resolver = new TypeResolver();
        resolver.Resolve(scriptFile);
        allDiagnostics.AddRange(resolver.Diagnostics);

        // Semantic analysis
        var analyzer = new SemanticAnalyzer();
        var semDiags = analyzer.Analyze(scriptFile, resolver);
        allDiagnostics.AddRange(semDiags);

        // Bail on errors
        if (allDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            var failResult = new CompilationResult(null, scriptFile, allDiagnostics);
            return new NACompilationResult(failResult, naSpec, plainNA);
        }

        // Bytecode emission
        var emitter = new BytecodeEmitter(scriptFile, resolver);
        var program = emitter.Emit();

        var result = new CompilationResult(program, scriptFile, allDiagnostics);
        return new NACompilationResult(result, naSpec, plainNA);
    }

    /// <summary>
    /// Parse a BSX-Light spec string without compiling to bytecode.
    /// Useful for validation and plain ⎕NA generation.
    /// </summary>
    public static NASpec Parse(string spec)
    {
        var lexer = new NALexer(spec);
        var tokens2 = lexer.Tokenize();
        var parser = new NAParser(tokens2);
        return parser.Parse();
    }
}
