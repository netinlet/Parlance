using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Parlance.CSharp.Analyzers.Metrics;

namespace Parlance.CSharp.Analyzers.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PARL3001_CognitiveComplexityThreshold : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PARL3001";

    /// <summary>
    /// Fallback cognitive-complexity threshold when the
    /// <see cref="ThresholdOption"/> <c>.editorconfig</c> value is unset or
    /// invalid. Kept as a <c>public const</c> here (and not collapsed into
    /// <see cref="ComplexityDefaults.MethodThreshold"/>) because existing
    /// analyzer tests and contract docs refer to <c>PARL3001.DefaultThreshold</c>
    /// by name.
    /// </summary>
    public const int DefaultThreshold = ComplexityDefaults.MethodThreshold;

    /// <summary>
    /// Fallback threshold for property-shaped declarations (property/indexer
    /// arrow bodies and all accessor kinds). Accessors should be trivial, so
    /// the default is intentionally much smaller than
    /// <see cref="DefaultThreshold"/> — a five-line accessor that crosses 3 is
    /// already a smell worth surfacing.
    /// </summary>
    public const int DefaultPropertyThreshold = ComplexityDefaults.PropertyThreshold;

    public const string ThresholdOption = "dotnet_code_quality.PARL3001.max_cognitive_complexity";
    public const string PropertyThresholdOption = "dotnet_code_quality.PARL3001.max_cognitive_complexity.property";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Cognitive complexity exceeds threshold",
        messageFormat: "'{0}' has cognitive complexity {1}, exceeding threshold {2}",
        category: "Maintainability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports declarations whose control-flow nesting and interruption make the code harder to understand.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(
            AnalyzeDeclaration,
            SyntaxKind.MethodDeclaration,
            SyntaxKind.ConstructorDeclaration,
            SyntaxKind.DestructorDeclaration,
            SyntaxKind.OperatorDeclaration,
            SyntaxKind.ConversionOperatorDeclaration,
            SyntaxKind.LocalFunctionStatement,
            SyntaxKind.PropertyDeclaration,
            SyntaxKind.IndexerDeclaration,
            SyntaxKind.GetAccessorDeclaration,
            SyntaxKind.SetAccessorDeclaration,
            SyntaxKind.InitAccessorDeclaration,
            SyntaxKind.AddAccessorDeclaration,
            SyntaxKind.RemoveAccessorDeclaration);
    }

    private static void AnalyzeDeclaration(SyntaxNodeAnalysisContext context)
    {
        var declaration = context.Node;

        // Non-static local functions are folded into their containing
        // declaration's score (see CognitiveComplexityMetric.VisitLocalFunctionStatement).
        // Reporting them as independent analysis targets here would double-count
        // their body: once inside the parent's score and once on their own. Only
        // static local functions get their own diagnostic, matching the scoping
        // split documented in the PARL3001 contract.
        if (declaration is LocalFunctionStatementSyntax localFunction
            && !localFunction.Modifiers.Any(SyntaxKind.StaticKeyword))
        {
            return;
        }

        var bodyOrExpression = GetBodyOrExpression(declaration);
        if (bodyOrExpression is null)
            return;

        var threshold = GetThreshold(context, declaration);
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) as IMethodSymbol;
        var result = CognitiveComplexityMetric.Calculate(bodyOrExpression, context.SemanticModel, methodSymbol);
        if (result.Score <= threshold)
            return;

        var name = GetDeclarationName(declaration);
        var location = GetDiagnosticLocation(declaration);
        var additionalLocations = result.Increments.Select(i => i.Location).ToImmutableArray();
        var properties = BuildIncrementProperties(result.Increments);

        context.ReportDiagnostic(Diagnostic.Create(
            descriptor: Rule,
            location: location,
            additionalLocations: additionalLocations,
            properties: properties,
            messageArgs: [name, result.Score, threshold]));
    }

    /// <summary>
    /// Encodes per-increment reason strings into the diagnostic's Properties
    /// dictionary as <c>increment.count</c> and <c>increment.N.reason</c>. The
    /// Nth entry's <see cref="Location"/> lives in
    /// <see cref="Diagnostic.AdditionalLocations"/> at the same index. This is
    /// the only cross-process channel Roslyn gives us for per-secondary-location
    /// metadata, so every consumer (test, MCP, IDE) decodes using this pair.
    /// </summary>
    private static ImmutableDictionary<string, string?> BuildIncrementProperties(
        ImmutableList<ComplexityIncrement> increments)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string?>();
        builder["increment.count"] = increments.Count.ToString(CultureInfo.InvariantCulture);
        for (var i = 0; i < increments.Count; i++)
        {
            builder[string.Format(CultureInfo.InvariantCulture, "increment.{0}.reason", i)] = increments[i].Reason;
        }
        return builder.ToImmutable();
    }

    private static int GetThreshold(SyntaxNodeAnalysisContext context, SyntaxNode declaration)
    {
        var options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.Node.SyntaxTree);
        var isPropertyShaped = IsPropertyShaped(declaration);
        var optionKey = isPropertyShaped ? PropertyThresholdOption : ThresholdOption;
        var fallback = isPropertyShaped ? DefaultPropertyThreshold : DefaultThreshold;

        if (options.TryGetValue(optionKey, out var rawValue)
            && int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0)
        {
            return parsed;
        }

        return fallback;
    }

    /// <summary>
    /// True when the declaration is "accessor-shaped" and should be scored
    /// against the property threshold: property / indexer arrow bodies, and
    /// any accessor (get/set/init/add/remove) regardless of whether the owner
    /// is a property, indexer, or event. Matches the Sonar S3776 scope split.
    /// </summary>
    private static bool IsPropertyShaped(SyntaxNode declaration)
    {
        return declaration is PropertyDeclarationSyntax
            or IndexerDeclarationSyntax
            or AccessorDeclarationSyntax;
    }

    private static SyntaxNode? GetBodyOrExpression(SyntaxNode declaration)
    {
        return declaration switch
        {
            BaseMethodDeclarationSyntax method => method.Body ?? (SyntaxNode?)method.ExpressionBody?.Expression,
            LocalFunctionStatementSyntax localFunction => localFunction.Body ?? (SyntaxNode?)localFunction.ExpressionBody?.Expression,
            AccessorDeclarationSyntax accessor => accessor.Body ?? (SyntaxNode?)accessor.ExpressionBody?.Expression,
            PropertyDeclarationSyntax property => property.ExpressionBody?.Expression,
            IndexerDeclarationSyntax indexer => indexer.ExpressionBody?.Expression,
            _ => null,
        };
    }

    private static string GetDeclarationName(SyntaxNode declaration)
    {
        return declaration switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            DestructorDeclarationSyntax destructor => "~" + destructor.Identifier.ValueText,
            OperatorDeclarationSyntax operatorDeclaration => "operator " + operatorDeclaration.OperatorToken.ValueText,
            ConversionOperatorDeclarationSyntax conversion => "operator " + conversion.Type,
            LocalFunctionStatementSyntax localFunction => localFunction.Identifier.ValueText,
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            IndexerDeclarationSyntax => "this[]",
            AccessorDeclarationSyntax accessor => GetAccessorName(accessor),
            _ => declaration.Kind().ToString(),
        };
    }

    private static Location GetDiagnosticLocation(SyntaxNode declaration)
    {
        return declaration switch
        {
            MethodDeclarationSyntax method => method.Identifier.GetLocation(),
            ConstructorDeclarationSyntax constructor => constructor.Identifier.GetLocation(),
            DestructorDeclarationSyntax destructor => destructor.Identifier.GetLocation(),
            OperatorDeclarationSyntax operatorDeclaration => operatorDeclaration.OperatorToken.GetLocation(),
            ConversionOperatorDeclarationSyntax conversion => conversion.OperatorKeyword.GetLocation(),
            LocalFunctionStatementSyntax localFunction => localFunction.Identifier.GetLocation(),
            PropertyDeclarationSyntax property => property.Identifier.GetLocation(),
            IndexerDeclarationSyntax indexer => indexer.ThisKeyword.GetLocation(),
            AccessorDeclarationSyntax accessor => accessor.Keyword.GetLocation(),
            _ => declaration.GetLocation(),
        };
    }

    private static string GetAccessorName(AccessorDeclarationSyntax accessor)
    {
        var suffix = accessor.Keyword.ValueText;
        var owner = accessor.Parent?.Parent;

        return owner switch
        {
            PropertyDeclarationSyntax property => property.Identifier.ValueText + "." + suffix,
            IndexerDeclarationSyntax => "this[]." + suffix,
            EventDeclarationSyntax eventDeclaration => eventDeclaration.Identifier.ValueText + "." + suffix,
            _ => suffix,
        };
    }
}
