namespace BinScript.Core.Compiler;

using BinScript.Core.Compiler.Ast;
using BinScript.Core.Model;

/// <summary>
/// Validates a typed AST after type resolution.
/// Checks structural rules such as @root uniqueness, @derived forward references,
/// and match exhaustiveness.
/// </summary>
public sealed class SemanticAnalyzer
{
    public List<Diagnostic> Analyze(ScriptFile file, TypeResolver typeEnv)
    {
        var diagnostics = new List<Diagnostic>();

        ValidateRootUniqueness(file, diagnostics);
        ValidateStructMembers(file, diagnostics);

        return diagnostics;
    }

    // ─── @root uniqueness ───────────────────────────────────────────

    private static void ValidateRootUniqueness(ScriptFile file, List<Diagnostic> diagnostics)
    {
        var roots = file.Structs.Where(s => s.IsRoot).ToList();
        if (roots.Count > 1)
        {
            foreach (var root in roots.Skip(1))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error, "BSC300",
                    $"Multiple @root declarations: \"{root.Name}\" — only one struct may be @root",
                    root.Span));
            }
        }
    }

    // ─── Per-struct member validation ───────────────────────────────

    private static void ValidateStructMembers(ScriptFile file, List<Diagnostic> diagnostics)
    {
        foreach (var structDecl in file.Structs)
        {
            var knownFields = new HashSet<string>(StringComparer.Ordinal);

            // Add struct parameters as known names.
            foreach (var p in structDecl.Parameters)
                knownFields.Add(p);

            ValidateMembers(structDecl.Members, knownFields, diagnostics);
        }
    }

    private static void ValidateMembers(
        IReadOnlyList<MemberDecl> members,
        HashSet<string> knownFields,
        List<Diagnostic> diagnostics)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case FieldDecl field:
                    ValidateField(field, knownFields, diagnostics);
                    knownFields.Add(field.Name);
                    break;

                case LetBinding let:
                    ValidateExpressionReferences(let.Value, knownFields, let.Span, diagnostics);
                    knownFields.Add(let.Name);
                    break;

                case AtBlock atBlock:
                    ValidateMembers(atBlock.Members, knownFields, diagnostics);
                    break;
            }
        }
    }

    private static void ValidateField(
        FieldDecl field,
        HashSet<string> knownFields,
        List<Diagnostic> diagnostics)
    {
        // Validate @derived forward references.
        if (field.Modifiers.IsDerived && field.Modifiers.DerivedExpression is not null)
        {
            ValidateExpressionReferences(
                field.Modifiers.DerivedExpression, knownFields, field.Span, diagnostics);
        }

        // Check match exhaustiveness (type-level match).
        if (field.Type is MatchTypeRef matchRef)
        {
            ValidateMatchExhaustiveness(matchRef.Match, diagnostics);
        }
    }

    // ─── Match exhaustiveness ───────────────────────────────────────

    private static void ValidateMatchExhaustiveness(MatchExpr match, List<Diagnostic> diagnostics)
    {
        bool hasDefault = match.Arms.Any(a => a.Pattern is WildcardPattern);
        if (!hasDefault)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Warning, "BSC301",
                "Match expression has no default arm (_); unmatched values will cause a runtime error",
                match.Span));
        }
    }

    // ─── Expression reference validation ────────────────────────────

    /// <summary>
    /// Walk an expression and warn about identifiers that reference fields
    /// not yet declared (forward references in @derived / @let).
    /// Only checks top-level identifiers — nested field access (a.b) checks
    /// the root identifier only.
    /// </summary>
    private static void ValidateExpressionReferences(
        Expression expr,
        HashSet<string> knownFields,
        SourceSpan contextSpan,
        List<Diagnostic> diagnostics)
    {
        switch (expr)
        {
            case IdentifierExpr id:
                if (!knownFields.Contains(id.Name))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error, "BSC302",
                        $"@derived/@let expression references \"{id.Name}\" which is not declared before this point",
                        id.Span));
                }
                break;

            case FieldAccessExpr fa:
                ValidateExpressionReferences(fa.Object, knownFields, contextSpan, diagnostics);
                break;

            case IndexAccessExpr ia:
                ValidateExpressionReferences(ia.Object, knownFields, contextSpan, diagnostics);
                ValidateExpressionReferences(ia.Index, knownFields, contextSpan, diagnostics);
                break;

            case BinaryExpr bin:
                ValidateExpressionReferences(bin.Left, knownFields, contextSpan, diagnostics);
                ValidateExpressionReferences(bin.Right, knownFields, contextSpan, diagnostics);
                break;

            case UnaryExpr un:
                ValidateExpressionReferences(un.Operand, knownFields, contextSpan, diagnostics);
                break;

            case FunctionCallExpr fc:
                foreach (var arg in fc.Args)
                    ValidateExpressionReferences(arg, knownFields, contextSpan, diagnostics);
                break;

            case MethodCallExpr mc:
                ValidateExpressionReferences(mc.Object, knownFields, contextSpan, diagnostics);
                foreach (var arg in mc.Args)
                    ValidateExpressionReferences(arg, knownFields, contextSpan, diagnostics);
                break;

            case BlockExpr block:
                // Let bindings introduce new names visible within the block
                var blockScope = new HashSet<string>(knownFields);
                foreach (var binding in block.Bindings)
                {
                    ValidateExpressionReferences(binding.Value, blockScope, contextSpan, diagnostics);
                    blockScope.Add(binding.Name);
                }
                ValidateExpressionReferences(block.Result, blockScope, contextSpan, diagnostics);
                break;

            case LambdaExpr lambda:
                // Lambda parameter is in scope for the body
                var lambdaScope = new HashSet<string>(knownFields) { lambda.Parameter };
                ValidateExpressionReferences(lambda.Body, lambdaScope, contextSpan, diagnostics);
                break;

            // Literals — nothing to validate.
            case IntLiteralExpr:
            case StringLiteralExpr:
            case BoolLiteralExpr:
            case NullLiteralExpr:
                break;
        }
    }
}
