namespace BinScript.Core.Api;

using BinScript.Core.Interfaces;

public sealed class DictionaryModuleResolver : IModuleResolver
{
    private readonly Dictionary<string, string> _modules = new();

    public void AddModule(string name, string source) => _modules[name] = source;

    public string? ResolveModule(string moduleName) =>
        _modules.GetValueOrDefault(moduleName);
}
