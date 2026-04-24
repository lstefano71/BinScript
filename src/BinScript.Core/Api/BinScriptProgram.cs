namespace BinScript.Core.Api;

using BinScript.Core.Bytecode;
using BinScript.Core.Interfaces;
using BinScript.Core.Model;
using BinScript.Core.Runtime;

/// <summary>
/// Public API that wraps <see cref="BytecodeProgram"/> and provides parse and produce entry points.
/// </summary>
public sealed class BinScriptProgram
{
    public BytecodeProgram Bytecode { get; }

    public BinScriptProgram(BytecodeProgram bytecode)
    {
        Bytecode = bytecode ?? throw new ArgumentNullException(nameof(bytecode));
    }

    // ─── Parse ──────────────────────────────────────────────────────────

    /// <summary>Parse binary data using the @root struct.</summary>
    public ParseResult Parse(ReadOnlyMemory<byte> input, IResultEmitter emitter, ParseOptions? options = null)
    {
        var engine = new ParseEngine();
        return engine.Parse(Bytecode, input, emitter, options);
    }

    /// <summary>Parse binary data using a named entry point.</summary>
    public ParseResult Parse(ReadOnlyMemory<byte> input, string entryPoint, IResultEmitter emitter, ParseOptions? options = null)
    {
        int structIndex = Bytecode.FindStructIndex(entryPoint);
        if (structIndex < 0)
        {
            return new ParseResult(false, null,
                [new Diagnostic(DiagnosticSeverity.Error, "API001",
                    $"Struct '{entryPoint}' not found.", default)],
                0, input.Length);
        }

        var engine = new ParseEngine();
        return engine.Parse(Bytecode, input, emitter, options, structIndex);
    }

    // ─── Produce ────────────────────────────────────────────────────────

    /// <summary>Produce binary data using the @root struct.</summary>
    public ProduceResult Produce(IDataSource dataSource, Span<byte> output, ParseOptions? options = null)
    {
        var engine = new ProduceEngine();
        return engine.Produce(Bytecode, dataSource, output, options);
    }

    /// <summary>Produce binary data using a named entry point.</summary>
    public ProduceResult Produce(IDataSource dataSource, string entryPoint, Span<byte> output, ParseOptions? options = null)
    {
        int structIndex = Bytecode.FindStructIndex(entryPoint);
        if (structIndex < 0)
        {
            return new ProduceResult(false, 0,
                [new Diagnostic(DiagnosticSeverity.Error, "API001",
                    $"Struct '{entryPoint}' not found.", default)]);
        }

        var engine = new ProduceEngine();
        return engine.Produce(Bytecode, dataSource, output, options, structIndex);
    }

    /// <summary>Produce binary data, auto-allocating the output buffer.</summary>
    public ProduceResult ProduceAlloc(IDataSource dataSource, ParseOptions? options = null)
    {
        var engine = new ProduceEngine();
        var (result, data) = engine.ProduceAlloc(Bytecode, dataSource, options);
        result = result with { OutputBytes = data };
        return result;
    }

    /// <summary>Produce binary data from a named entry point, auto-allocating the output buffer.</summary>
    public ProduceResult ProduceAlloc(IDataSource dataSource, string entryPoint, ParseOptions? options = null)
    {
        int structIndex = Bytecode.FindStructIndex(entryPoint);
        if (structIndex < 0)
        {
            return new ProduceResult(false, 0,
                [new Diagnostic(DiagnosticSeverity.Error, "API001",
                    $"Struct '{entryPoint}' not found.", default)]);
        }

        var engine = new ProduceEngine();
        var (result, data) = engine.ProduceAlloc(Bytecode, dataSource, options, structIndex);
        result = result with { OutputBytes = data };
        return result;
    }
}
