namespace BinScript.Core.Compiler.Ast;

/// <summary>Endianness setting for default or explicit use.</summary>
public enum Endianness
{
    Little,
    Big,
}

/// <summary>
/// Top-level container for a parsed <c>.bsx</c> script file.
/// Holds all declarations, directives, and file-level settings.
/// </summary>
public sealed record ScriptFile(
    string FilePath,
    Endianness? DefaultEndian,
    string? DefaultEncoding,
    IReadOnlyList<ImportDecl> Imports,
    IReadOnlyList<ParamDecl> Params,
    IReadOnlyList<StructDecl> Structs,
    IReadOnlyList<BitsStructDecl> BitsStructs,
    IReadOnlyList<EnumDecl> Enums,
    IReadOnlyList<ConstDecl> Constants,
    SourceSpan Span) : AstNode(Span);
