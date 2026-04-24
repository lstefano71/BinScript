namespace BinScript.Core.Api;

using BinScript.Core.Bytecode;
using BinScript.Core.Interfaces;
using BinScript.Core.Model;
using BinScript.Core.Runtime;

/// <summary>
/// Public API that wraps <see cref="BytecodeProgram"/> and provides parse entry points.
/// </summary>
public sealed class BinScriptProgram
{
    public BytecodeProgram Bytecode { get; }

    public BinScriptProgram(BytecodeProgram bytecode)
    {
        Bytecode = bytecode ?? throw new ArgumentNullException(nameof(bytecode));
    }

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
}
