namespace BinScript.Emitters.Json;

using BinScript.Core.Api;
using BinScript.Core.Model;
using BinScript.Core.Runtime;

/// <summary>
/// Extension methods for <see cref="BinScriptProgram"/> to produce JSON output.
/// </summary>
public static class BinScriptProgramJsonExtensions
{
    /// <summary>Parse and return JSON directly (convenience).</summary>
    public static string ToJson(this BinScriptProgram program, ReadOnlyMemory<byte> input)
    {
        using var emitter = new JsonResultEmitter();
        var result = program.Parse(input, emitter);
        if (!result.Success)
        {
            var errors = string.Join("; ", result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new ParseException($"Parse failed: {errors}");
        }
        return emitter.GetJson();
    }

    /// <summary>Parse and return JSON using a named entry point.</summary>
    public static string ToJson(this BinScriptProgram program, ReadOnlyMemory<byte> input, string entryPoint)
    {
        using var emitter = new JsonResultEmitter();
        var result = program.Parse(input, entryPoint, emitter);
        if (!result.Success)
        {
            var errors = string.Join("; ", result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new ParseException($"Parse failed: {errors}");
        }
        return emitter.GetJson();
    }
}
