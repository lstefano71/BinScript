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
        ValidateIndexedFieldAccess(file, typeEnv, diagnostics);

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

            // Warn on modifiers that have no effect on derived fields
            WarnNonsensicalDerivedModifiers(field, diagnostics);
        }

        // Check match exhaustiveness (type-level match).
        if (field.Type is MatchTypeRef matchRef)
        {
            ValidateMatchExhaustiveness(matchRef.Match, diagnostics);
        }
    }

    private static void WarnNonsensicalDerivedModifiers(FieldDecl field, List<Diagnostic> diagnostics)
    {
        var mods = field.Modifiers;
        if (mods.Encoding is not null)
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "BSC304",
                $"@encoding has no effect on @derived field \"{field.Name}\"", field.Span));
        if (mods.AssertExpression is not null)
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "BSC304",
                $"@assert has no effect on @derived field \"{field.Name}\"", field.Span));
        if (mods.IsInline)
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "BSC304",
                $"@inline has no effect on @derived field \"{field.Name}\"", field.Span));
        if (mods.ShowPtr)
            diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, "BSC304",
                $"@show_ptr has no effect on @derived field \"{field.Name}\"", field.Span));
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

    // ─── Indexed field access through match arms ────────────────────

    /// <summary>
    /// Warn when <c>array[i].field</c> accesses a field that only exists inside
    /// a match arm of the element struct — the field may not be present at runtime
    /// depending on which arm was taken for element <c>i</c>.
    /// </summary>
    private static void ValidateIndexedFieldAccess(
        ScriptFile file,
        TypeResolver typeEnv,
        List<Diagnostic> diagnostics)
    {
        foreach (var structDecl in file.Structs)
            ValidateIndexedAccessInMembers(structDecl.Members, structDecl, typeEnv, diagnostics);
    }

    private static void ValidateIndexedAccessInMembers(
        IReadOnlyList<MemberDecl> members,
        StructDecl owningStruct,
        TypeResolver typeEnv,
        List<Diagnostic> diagnostics)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case FieldDecl field:
                    if (field.Modifiers.IsDerived && field.Modifiers.DerivedExpression is not null)
                        ScanExprForIndexedAccess(field.Modifiers.DerivedExpression, owningStruct, typeEnv, diagnostics);
                    if (field.Type is MatchTypeRef matchRef)
                        ScanExprForIndexedAccess(matchRef.Match.Discriminant, owningStruct, typeEnv, diagnostics);
                    // Array count expression
                    if (field.Array is CountArraySpec cas)
                        ScanExprForIndexedAccess(cas.Count, owningStruct, typeEnv, diagnostics);
                    else if (field.Array is UntilArraySpec uas)
                        ScanExprForIndexedAccess(uas.Condition, owningStruct, typeEnv, diagnostics);
                    else if (field.Array is SentinelArraySpec sas)
                        ScanExprForIndexedAccess(sas.Predicate, owningStruct, typeEnv, diagnostics);
                    break;

                case LetBinding let:
                    ScanExprForIndexedAccess(let.Value, owningStruct, typeEnv, diagnostics);
                    break;

                case AtBlock atBlock:
                    ScanExprForIndexedAccess(atBlock.Offset, owningStruct, typeEnv, diagnostics);
                    if (atBlock.Guard is not null)
                        ScanExprForIndexedAccess(atBlock.Guard, owningStruct, typeEnv, diagnostics);
                    ValidateIndexedAccessInMembers(atBlock.Members, owningStruct, typeEnv, diagnostics);
                    break;
            }
        }
    }

    /// <summary>
    /// Recursively scan an expression tree for field access chains that include
    /// an <c>IndexAccessExpr</c> (constant-index array access) and validate the
    /// accessed field path for match-arm ambiguity.
    /// </summary>
    private static void ScanExprForIndexedAccess(
        Expression expr,
        StructDecl owningStruct,
        TypeResolver typeEnv,
        List<Diagnostic> diagnostics)
    {
        // First, check if this expression is a FieldAccess chain containing an IndexAccessExpr.
        // Collect the suffix path segments after the [N] and check them.
        TryCheckIndexedChain(expr, owningStruct, typeEnv, diagnostics);

        // Then recurse into sub-expressions
        switch (expr)
        {
            case FieldAccessExpr fa:
                ScanExprForIndexedAccess(fa.Object, owningStruct, typeEnv, diagnostics);
                break;

            case IndexAccessExpr ia:
                ScanExprForIndexedAccess(ia.Object, owningStruct, typeEnv, diagnostics);
                ScanExprForIndexedAccess(ia.Index, owningStruct, typeEnv, diagnostics);
                break;

            case BinaryExpr bin:
                ScanExprForIndexedAccess(bin.Left, owningStruct, typeEnv, diagnostics);
                ScanExprForIndexedAccess(bin.Right, owningStruct, typeEnv, diagnostics);
                break;

            case UnaryExpr un:
                ScanExprForIndexedAccess(un.Operand, owningStruct, typeEnv, diagnostics);
                break;

            case FunctionCallExpr fc:
                foreach (var arg in fc.Args)
                    ScanExprForIndexedAccess(arg, owningStruct, typeEnv, diagnostics);
                break;

            case MethodCallExpr mc:
                ScanExprForIndexedAccess(mc.Object, owningStruct, typeEnv, diagnostics);
                foreach (var arg in mc.Args)
                    ScanExprForIndexedAccess(arg, owningStruct, typeEnv, diagnostics);
                break;

            case BlockExpr block:
                foreach (var binding in block.Bindings)
                    ScanExprForIndexedAccess(binding.Value, owningStruct, typeEnv, diagnostics);
                ScanExprForIndexedAccess(block.Result, owningStruct, typeEnv, diagnostics);
                break;

            case LambdaExpr lambda:
                ScanExprForIndexedAccess(lambda.Body, owningStruct, typeEnv, diagnostics);
                break;
        }
    }

    /// <summary>
    /// If <paramref name="expr"/> is a FieldAccess chain containing an IndexAccessExpr,
    /// collect the suffix path after [N] and check for match-arm fields.
    /// E.g. for <c>items[1].body.value</c> the suffix is ["body", "value"].
    /// </summary>
    private static void TryCheckIndexedChain(
        Expression expr,
        StructDecl owningStruct,
        TypeResolver typeEnv,
        List<Diagnostic> diagnostics)
    {
        // Collect the field access suffix walking backwards from the outermost FieldAccessExpr
        var suffixSegments = new List<string>();
        var current = expr;
        while (current is FieldAccessExpr fa)
        {
            suffixSegments.Add(fa.FieldName);
            current = fa.Object;
        }

        // current must be an IndexAccessExpr for this to be an indexed access
        if (current is not IndexAccessExpr indexAccess || suffixSegments.Count == 0)
            return;

        // Reverse to get segments in order: ["body", "value"]
        suffixSegments.Reverse();

        // Resolve the array field
        var arrayFieldName = ResolveRootFieldName(indexAccess.Object);
        if (arrayFieldName is null)
            return;

        var (arrayField, _) = FindFieldInHierarchy(arrayFieldName, owningStruct, typeEnv);
        if (arrayField is null)
            return;

        var elementStructName = ResolveElementStructName(arrayField.Type);
        if (elementStructName is null || !typeEnv.Structs.TryGetValue(elementStructName, out var elementStruct))
            return;

        // Walk the suffix segments through the element struct hierarchy,
        // checking at each step if we're passing through a match arm
        CheckPathForMatchArms(
            elementStruct, suffixSegments, 0,
            arrayFieldName, expr.Span, typeEnv, diagnostics);
    }

    /// <summary>
    /// Walk <paramref name="segments"/> through the struct hierarchy starting at
    /// <paramref name="currentStruct"/>, checking whether any segment is only
    /// reachable through a match arm.
    /// </summary>
    private static void CheckPathForMatchArms(
        StructDecl currentStruct,
        List<string> segments,
        int segIndex,
        string arrayFieldName,
        SourceSpan span,
        TypeResolver typeEnv,
        List<Diagnostic> diagnostics)
    {
        if (segIndex >= segments.Count)
            return;

        var fieldName = segments[segIndex];

        // Check direct fields first
        var directField = currentStruct.Members.OfType<FieldDecl>()
            .FirstOrDefault(f => f.Name == fieldName);

        if (directField is not null)
        {
            // Field is a direct member — safe at this level.
            // If there are more segments, follow into the field's type.
            if (segIndex + 1 < segments.Count)
            {
                if (directField.Type is MatchTypeRef matchType)
                {
                    // The direct field is a match — remaining segments go into arm structs.
                    // Check if the next segment exists in all arms or only some.
                    var nextFieldName = segments[segIndex + 1];
                    bool inAnyArm = false;
                    bool inAllArms = true;
                    foreach (var arm in matchType.Match.Arms)
                    {
                        if (arm.Type is NamedTypeRef armTypeRef &&
                            typeEnv.Structs.TryGetValue(armTypeRef.Name, out var armStruct))
                        {
                            bool hasField = armStruct.Members.OfType<FieldDecl>()
                                .Any(f => f.Name == nextFieldName);
                            if (hasField)
                                inAnyArm = true;
                            else
                                inAllArms = false;
                        }
                        else
                        {
                            // Non-struct arm (primitive, bytes, etc.) — field can't exist here
                            inAllArms = false;
                        }
                    }

                    if (inAnyArm && !inAllArms)
                    {
                        var fullPath = $"{arrayFieldName}[..].{string.Join(".", segments)}";
                        diagnostics.Add(new Diagnostic(
                            DiagnosticSeverity.Warning, "BSC303",
                            $"'{fullPath}' accesses a field that only exists inside " +
                            $"some match arm struct(s) of '{currentStruct.Name}.{directField.Name}' — " +
                            $"the field may not be present at runtime depending on which arm was taken",
                            span));
                    }
                }
                else
                {
                    var nextStructName = ResolveElementStructName(directField.Type);
                    if (nextStructName is not null && typeEnv.Structs.TryGetValue(nextStructName, out var nextStruct))
                        CheckPathForMatchArms(nextStruct, segments, segIndex + 1, arrayFieldName, span, typeEnv, diagnostics);
                }
            }
            return;
        }

        // Field is not a direct member — check if it's inside a match arm
        foreach (var member in currentStruct.Members)
        {
            if (member is not FieldDecl { Type: MatchTypeRef matchType } matchField)
                continue;

            foreach (var arm in matchType.Match.Arms)
            {
                if (arm.Type is not NamedTypeRef armTypeRef ||
                    !typeEnv.Structs.TryGetValue(armTypeRef.Name, out var armStruct))
                    continue;

                var armField = armStruct.Members.OfType<FieldDecl>()
                    .FirstOrDefault(f => f.Name == fieldName);
                if (armField is null)
                    continue;

                // Found the field inside a match arm — emit warning
                var fullPath = $"{arrayFieldName}[..].{string.Join(".", segments)}";
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning, "BSC303",
                    $"'{fullPath}' accesses a field that only exists inside " +
                    $"match arm struct(s) of '{currentStruct.Name}.{matchField.Name}' — " +
                    $"the field may not be present at runtime depending on which arm was taken",
                    span));
                return;
            }
        }
    }

    /// <summary>Extract the root field name from an expression (handles dotted paths like a.b.c → "a").</summary>
    private static string? ResolveRootFieldName(Expression expr)
    {
        return expr switch
        {
            IdentifierExpr id => id.Name,
            FieldAccessExpr fa => ResolveRootFieldName(fa.Object),
            _ => null,
        };
    }

    /// <summary>
    /// Find a field by name in a struct, following dotted paths across struct boundaries.
    /// Returns the field at the end of the chain (the array field) and its containing struct.
    /// For simple names like "entries", returns the field directly.
    /// For dotted paths like "header.dirs", follows to the leaf.
    /// </summary>
    private static (FieldDecl? field, StructDecl? containingStruct) FindFieldInHierarchy(
        string rootName,
        StructDecl owningStruct,
        TypeResolver typeEnv)
    {
        var field = owningStruct.Members.OfType<FieldDecl>().FirstOrDefault(f => f.Name == rootName);
        if (field is null)
            return (null, null);

        return (field, owningStruct);
    }

    /// <summary>
    /// Given a field's type reference, extract the element struct name if it's a named struct array.
    /// Handles <c>NamedTypeRef</c> (with array spec on the field) and <c>ArrayTypeRef</c>.
    /// </summary>
    private static string? ResolveElementStructName(TypeReference typeRef)
    {
        return typeRef switch
        {
            NamedTypeRef ntr => ntr.Name,
            ArrayTypeRef atr => ResolveElementStructName(atr.ElementType),
            NullableTypeRef nullable => ResolveElementStructName(nullable.InnerType),
            PtrTypeRef ptr => ResolveElementStructName(ptr.InnerType),
            _ => null,
        };
    }
}
