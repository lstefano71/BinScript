namespace BinScript.Emitters.Json;

using BinScript.Core.Api;
using BinScript.Core.Interfaces;
using BinScript.Core.Model;
using BinScript.Core.Runtime;

/// <summary>
/// Extension methods for <see cref="BinScriptProgram"/> to produce JSON output.
/// </summary>
public static class BinScriptProgramJsonExtensions
{
    /// <summary>Parse and return JSON directly (convenience).</summary>
    public static string ToJson(this BinScriptProgram program, ReadOnlyMemory<byte> input)
        => ToJson(program, input, (ParseOptions?)null);

    /// <summary>Parse and return JSON with runtime options (e.g., @param values).</summary>
    public static string ToJson(this BinScriptProgram program, ReadOnlyMemory<byte> input, ParseOptions? options)
    {
        using var emitter = new JsonResultEmitter();
        var result = program.Parse(input, emitter, options);
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

    /// <summary>Produce binary from a JSON string (convenience).</summary>
    public static byte[] FromJson(this BinScriptProgram program, string json)
    {
        using var dataSource = new JsonDataSource(json);
        var result = program.ProduceAlloc(dataSource);
        if (!result.Success)
        {
            var errors = string.Join("; ", result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new ProduceException($"Produce failed: {errors}");
        }
        return result.OutputBytes!;
    }

    /// <summary>Produce binary from a JSON string using a named entry point.</summary>
    public static byte[] FromJson(this BinScriptProgram program, string json, string entryPoint)
    {
        using var dataSource = new JsonDataSource(json);
        var result = program.ProduceAlloc(dataSource, entryPoint);
        if (!result.Success)
        {
            var errors = string.Join("; ", result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new ProduceException($"Produce failed: {errors}");
        }
        return result.OutputBytes!;
    }
}
