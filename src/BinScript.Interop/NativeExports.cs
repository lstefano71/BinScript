namespace BinScript.Interop;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using BinScript.Core.Api;
using BinScript.Core.Bytecode;
using BinScript.Core.Model;
using BinScript.Emitters.Json;

/// <summary>
/// <c>[UnmanagedCallersOnly]</c> static methods implementing the BinScript C-ABI surface.
/// All returned strings/buffers are allocated with <see cref="NativeMemory"/> and
/// must be freed by the caller via <c>binscript_mem_free</c>, except for
/// <c>binscript_last_error</c> and <c>binscript_version</c> which return
/// pointers to managed memory that must NOT be freed.
/// </summary>
public static unsafe class NativeExports
{
    private const string Version = "0.1.0";

    // Pinned version string — lives for the process lifetime.
    private static readonly byte[] VersionUtf8 = Encoding.UTF8.GetBytes(Version + '\0');

    // Thread-static pinned buffer for binscript_last_error return value.
    [ThreadStatic]
    private static byte[]? _lastErrorBuffer;

    // ── Compiler lifecycle ────────────────────────────────────────

    [UnmanagedCallersOnly(EntryPoint = "binscript_compiler_new")]
    public static IntPtr CompilerNew()
    {
        try
        {
            ErrorState.Clear();
            var compiler = new BinScriptCompiler();
            return HandleTable.Alloc(compiler);
        }
        catch (Exception ex)
        {
            ErrorState.Set(ex.Message);
            return IntPtr.Zero;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "binscript_compiler_add_module")]
    public static int CompilerAddModule(IntPtr compilerHandle, byte* name, byte* script)
    {
        try
        {
            ErrorState.Clear();
            var compiler = HandleTable.Get<BinScriptCompiler>(compilerHandle);
            if (compiler is null) { ErrorState.Set("Invalid compiler handle"); return -1; }

            string nameStr = Marshal.PtrToStringUTF8((IntPtr)name) ?? "";
            string scriptStr = Marshal.PtrToStringUTF8((IntPtr)script) ?? "";
            compiler.AddModule(nameStr, scriptStr);
            return 0;
        }
        catch (Exception ex)
        {
            ErrorState.Set(ex.Message);
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "binscript_compiler_compile")]
    public static IntPtr CompilerCompile(IntPtr compilerHandle, byte* script, byte* paramsJson)
    {
        try
        {
            ErrorState.Clear();
            var compiler = HandleTable.Get<BinScriptCompiler>(compilerHandle);
            if (compiler is null) { ErrorState.Set("Invalid compiler handle"); return IntPtr.Zero; }

            string source = Marshal.PtrToStringUTF8((IntPtr)script) ?? "";
            Dictionary<string, object>? parameters = ParseParametersJson(paramsJson);

            var result = compiler.Compile(source, "", parameters);
            if (!result.Success)
            {
                var errors = string.Join("; ", result.Diagnostics
                    .Where(d => d.Severity == Core.Model.DiagnosticSeverity.Error)
                    .Select(d => d.Message));
                ErrorState.Set($"Compilation failed: {errors}");
                return IntPtr.Zero;
            }

            var program = new BinScriptProgram(result.Program!);
            return HandleTable.Alloc(program);
        }
        catch (Exception ex)
        {
            ErrorState.Set(ex.Message);
            return IntPtr.Zero;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "binscript_compiler_free")]
    public static void CompilerFree(IntPtr compilerHandle)
    {
        HandleTable.Free(compilerHandle);
    }

    // ── Program persistence ──────────────────────────────────────

    [UnmanagedCallersOnly(EntryPoint = "binscript_save")]
    public static long Save(IntPtr scriptHandle, byte* buf, nuint cap)
    {
        try
        {
            ErrorState.Clear();
            var program = HandleTable.Get<BinScriptProgram>(scriptHandle);
            if (program is null) { ErrorState.Set("Invalid program handle"); return -1; }

            byte[] serialized = BytecodeSerializer.Serialize(program.Bytecode);

            if (buf == null)
                return serialized.Length;

            if ((int)cap < serialized.Length)
                return serialized.Length; // caller needs to allocate more

            serialized.CopyTo(new Span<byte>(buf, serialized.Length));
            return serialized.Length;
        }
        catch (Exception ex)
        {
            ErrorState.Set(ex.Message);
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "binscript_load")]
    public static IntPtr Load(byte* data, nuint len)
    {
        try
        {
            ErrorState.Clear();
            if (data == null || len == 0)
            {
                ErrorState.Set("Null or empty data");
                return IntPtr.Zero;
            }

            var span = new ReadOnlySpan<byte>(data, (int)len);
            var bytecodeProgram = BytecodeDeserializer.Deserialize(span);
            var program = new BinScriptProgram(bytecodeProgram);
            return HandleTable.Alloc(program);
        }
        catch (Exception ex)
        {
            ErrorState.Set(ex.Message);
            return IntPtr.Zero;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "binscript_free")]
    public static void Free(IntPtr scriptHandle)
    {
        HandleTable.Free(scriptHandle);
    }

    // ── Parse: Binary → JSON ─────────────────────────────────────

    [UnmanagedCallersOnly(EntryPoint = "binscript_to_json")]
    public static byte* ToJson(IntPtr scriptHandle, byte* data, nuint len, byte* paramsJson)
    {
        try
        {
            ErrorState.Clear();
            var program = HandleTable.Get<BinScriptProgram>(scriptHandle);
            if (program is null) { ErrorState.Set("Invalid program handle"); return null; }

            var input = new ReadOnlyMemory<byte>(new ReadOnlySpan<byte>(data, (int)len).ToArray());
            var options = BuildParseOptions(paramsJson);
            string json = program.ToJson(input, options);
            return AllocUtf8(json);
        }
        catch (Exception ex)
        {
            ErrorState.Set(ex.Message);
            return null;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "binscript_to_json_entry")]
    public static byte* ToJsonEntry(IntPtr scriptHandle, byte* entry, byte* data, nuint len, byte* paramsJson)
    {
        try
        {
            ErrorState.Clear();
            var program = HandleTable.Get<BinScriptProgram>(scriptHandle);
            if (program is null) { ErrorState.Set("Invalid program handle"); return null; }

            string entryPoint = Marshal.PtrToStringUTF8((IntPtr)entry) ?? "";
            var input = new ReadOnlyMemory<byte>(new ReadOnlySpan<byte>(data, (int)len).ToArray());
            var options = BuildParseOptions(paramsJson);
            string json = program.ToJson(input, entryPoint, options);
            return AllocUtf8(json);
        }
        catch (Exception ex)
        {
            ErrorState.Set(ex.Message);
            return null;
        }
    }

    // ── Produce: JSON → Binary ──────────────────────────────────

    [UnmanagedCallersOnly(EntryPoint = "binscript_from_json_static_size")]
    public static long FromJsonStaticSize(IntPtr scriptHandle, byte* entry)
    {
        try
        {
            ErrorState.Clear();
            var program = HandleTable.Get<BinScriptProgram>(scriptHandle);
            if (program is null) { ErrorState.Set("Invalid program handle"); return -1; }

            int structIndex;
            if (entry != null)
            {
                string entryName = Marshal.PtrToStringUTF8((IntPtr)entry) ?? "";
                structIndex = program.Bytecode.FindStructIndex(entryName);
                if (structIndex < 0) { ErrorState.Set($"Struct '{entryName}' not found."); return -1; }
            }
            else
            {
                structIndex = program.Bytecode.RootStructIndex;
                if (structIndex < 0) { ErrorState.Set("No @root struct found."); return -1; }
            }

            return program.Bytecode.Structs[structIndex].StaticSize;
        }
        catch (Exception ex)
        {
            ErrorState.Set(ex.Message);
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "binscript_from_json_calc_size")]
    public static long FromJsonCalcSize(IntPtr scriptHandle, byte* json, byte* paramsJson)
    {
        try
        {
            ErrorState.Clear();
            var program = HandleTable.Get<BinScriptProgram>(scriptHandle);
            if (program is null) { ErrorState.Set("Invalid program handle"); return -1; }

            string jsonStr = Marshal.PtrToStringUTF8((IntPtr)json) ?? "";
            var options = BuildParseOptions(paramsJson);

            using var dataSource = new JsonDataSource(jsonStr);
            var result = program.ProduceAlloc(dataSource, options);
            if (!result.Success)
            {
                var errors = string.Join("; ", result.Diagnostics
                    .Where(d => d.Severity == Core.Model.DiagnosticSeverity.Error)
                    .Select(d => d.Message));
                ErrorState.Set($"Produce failed: {errors}");
                return -1;
            }

            return result.BytesWritten;
        }
        catch (Exception ex)
        {
            ErrorState.Set(ex.Message);
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "binscript_from_json_into")]
    public static long FromJsonInto(IntPtr scriptHandle, byte* buf, nuint bufLen, byte* json, byte* paramsJson)
    {
        try
        {
            ErrorState.Clear();
            var program = HandleTable.Get<BinScriptProgram>(scriptHandle);
            if (program is null) { ErrorState.Set("Invalid program handle"); return -1; }

            string jsonStr = Marshal.PtrToStringUTF8((IntPtr)json) ?? "";
            var options = BuildParseOptions(paramsJson);

            using var dataSource = new JsonDataSource(jsonStr);
            var result = program.ProduceAlloc(dataSource, options);
            if (!result.Success)
            {
                var errors = string.Join("; ", result.Diagnostics
                    .Where(d => d.Severity == Core.Model.DiagnosticSeverity.Error)
                    .Select(d => d.Message));
                ErrorState.Set($"Produce failed: {errors}");
                return -1;
            }

            if ((long)bufLen < result.BytesWritten)
                return -2; // buffer too small

            result.OutputBytes!.CopyTo(new Span<byte>(buf, (int)result.BytesWritten));
            return result.BytesWritten;
        }
        catch (Exception ex)
        {
            ErrorState.Set(ex.Message);
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "binscript_from_json")]
    public static byte* FromJson(IntPtr scriptHandle, byte* json, nuint* outLen, byte* paramsJson)
    {
        try
        {
            ErrorState.Clear();
            var program = HandleTable.Get<BinScriptProgram>(scriptHandle);
            if (program is null) { ErrorState.Set("Invalid program handle"); if (outLen != null) *outLen = 0; return null; }

            string jsonStr = Marshal.PtrToStringUTF8((IntPtr)json) ?? "";
            var options = BuildParseOptions(paramsJson);

            using var dataSource = new JsonDataSource(jsonStr);
            var result = program.ProduceAlloc(dataSource, options);
            if (!result.Success)
            {
                var errors = string.Join("; ", result.Diagnostics
                    .Where(d => d.Severity == Core.Model.DiagnosticSeverity.Error)
                    .Select(d => d.Message));
                ErrorState.Set($"Produce failed: {errors}");
                if (outLen != null) *outLen = 0;
                return null;
            }

            var data = result.OutputBytes!;
            byte* native = (byte*)NativeMemory.Alloc((nuint)data.Length);
            data.CopyTo(new Span<byte>(native, data.Length));
            if (outLen != null) *outLen = (nuint)data.Length;
            return native;
        }
        catch (Exception ex)
        {
            ErrorState.Set(ex.Message);
            if (outLen != null) *outLen = 0;
            return null;
        }
    }

    // ── Memory management ────────────────────────────────────────

    [UnmanagedCallersOnly(EntryPoint = "binscript_mem_free")]
    public static void MemFree(void* ptr)
    {
        if (ptr != null)
            NativeMemory.Free(ptr);
    }

    // ── Error reporting ──────────────────────────────────────────

    [UnmanagedCallersOnly(EntryPoint = "binscript_last_error")]
    public static byte* LastError()
    {
        string? error = ErrorState.Get();
        if (error is null)
            return null;

        // Pin in a thread-static GCHandle so the pointer stays valid
        // until the next call on this thread. Caller must NOT free this.
        if (_lastErrorPin.IsAllocated)
            _lastErrorPin.Free();

        _lastErrorBuffer = Encoding.UTF8.GetBytes(error + '\0');
        _lastErrorPin = GCHandle.Alloc(_lastErrorBuffer, GCHandleType.Pinned);
        return (byte*)_lastErrorPin.AddrOfPinnedObject();
    }

    [ThreadStatic]
    private static GCHandle _lastErrorPin;

    // ── Version ──────────────────────────────────────────────────

    // Pin the version string once at static init.
    private static readonly GCHandle VersionPin = GCHandle.Alloc(VersionUtf8, GCHandleType.Pinned);

    [UnmanagedCallersOnly(EntryPoint = "binscript_version")]
    public static byte* GetVersion() => (byte*)VersionPin.AddrOfPinnedObject();

    // ── Helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Allocate a null-terminated UTF-8 string in native memory.
    /// Caller is responsible for freeing via <c>binscript_mem_free</c>.
    /// </summary>
    public static byte* AllocUtf8(string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        byte* result = (byte*)NativeMemory.Alloc((nuint)(utf8.Length + 1));
        utf8.CopyTo(new Span<byte>(result, utf8.Length));
        result[utf8.Length] = 0;
        return result;
    }

    /// <summary>
    /// Minimal JSON parameter parser for <c>params_json</c>.
    /// Handles <c>null</c>, empty string, or a simple flat JSON object
    /// with string/number/bool values.
    /// </summary>
    public static Dictionary<string, object>? ParseParametersJson(byte* paramsJson)
    {
        if (paramsJson == null)
            return null;

        string json = Marshal.PtrToStringUTF8((IntPtr)paramsJson) ?? "";
        if (string.IsNullOrWhiteSpace(json) || json == "null")
            return null;

        return ParseFlatJsonObject(json);
    }

    /// <summary>
    /// Build a <see cref="ParseOptions"/> from a native <c>params_json</c> pointer.
    /// Returns null when no parameters are provided.
    /// </summary>
    private static ParseOptions? BuildParseOptions(byte* paramsJson)
    {
        var raw = ParseParametersJson(paramsJson);
        if (raw is null || raw.Count == 0)
            return null;

        var runtimeParams = new Dictionary<string, long>();
        foreach (var (key, value) in raw)
        {
            runtimeParams[key] = value switch
            {
                long l => l,
                int i => i,
                double d => (long)d,
                _ => Convert.ToInt64(value),
            };
        }
        return new ParseOptions { RuntimeParameters = runtimeParams };
    }

    /// <summary>
    /// Lightweight flat JSON object parser. Supports string, integer, float, bool values.
    /// Not a full JSON parser — designed for simple <c>{"key": value, ...}</c> objects.
    /// </summary>
    public static Dictionary<string, object>? ParseFlatJsonObject(string json)
    {
        json = json.Trim();
        if (!json.StartsWith('{') || !json.EndsWith('}'))
            return null;

        var result = new Dictionary<string, object>();
        json = json[1..^1].Trim(); // strip outer braces

        if (string.IsNullOrWhiteSpace(json))
            return result;

        int i = 0;
        while (i < json.Length)
        {
            SkipWhitespace(json, ref i);
            if (i >= json.Length) break;

            // Parse key
            string? key = ParseJsonString(json, ref i);
            if (key is null) break;

            SkipWhitespace(json, ref i);
            if (i >= json.Length || json[i] != ':') break;
            i++; // skip ':'
            SkipWhitespace(json, ref i);

            // Parse value
            object? value = ParseJsonValue(json, ref i);
            if (value is not null)
                result[key] = value;

            SkipWhitespace(json, ref i);
            if (i < json.Length && json[i] == ',')
                i++; // skip ','
        }

        return result.Count > 0 ? result : null;
    }

    private static void SkipWhitespace(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    private static string? ParseJsonString(string s, ref int i)
    {
        if (i >= s.Length || s[i] != '"') return null;
        i++; // skip opening quote
        var sb = new StringBuilder();
        while (i < s.Length && s[i] != '"')
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                i++;
                sb.Append(s[i]);
            }
            else
            {
                sb.Append(s[i]);
            }
            i++;
        }
        if (i < s.Length) i++; // skip closing quote
        return sb.ToString();
    }

    private static object? ParseJsonValue(string s, ref int i)
    {
        if (i >= s.Length) return null;

        // String
        if (s[i] == '"')
            return ParseJsonString(s, ref i);

        // Bool / null
        if (s[i..].StartsWith("true", StringComparison.Ordinal)) { i += 4; return true; }
        if (s[i..].StartsWith("false", StringComparison.Ordinal)) { i += 5; return false; }
        if (s[i..].StartsWith("null", StringComparison.Ordinal)) { i += 4; return null; }

        // Number
        int start = i;
        bool hasDecimal = false;
        if (i < s.Length && s[i] == '-') i++;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.'))
        {
            if (s[i] == '.') hasDecimal = true;
            i++;
        }

        string numStr = s[start..i];
        if (hasDecimal && double.TryParse(numStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double d))
            return d;
        if (long.TryParse(numStr, out long l))
            return l;

        return null;
    }
}
