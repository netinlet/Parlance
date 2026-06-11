using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Parlance.Analyzers.Upstream;

namespace Parlance.Analysis.Tests;

// Regression guard for the empty-AnalyzerOptions bug: analyzers used to run through the parameterless
// WithAnalyzers(analyzers) overload, which drops the project's .editorconfig-derived options. Every IDE
// style preference (csharp_style_var_*, expression-bodied, …) then fell back to Roslyn's built-in defaults
// regardless of the project's .editorconfig. These tests drive the analyzer pipeline through the production
// seam (WithProjectAnalyzers) against an in-memory project whose .editorconfig flips the "prefer var"
// preference, and prove the chosen option actually reaches the analyzers:
//   prefer var (true)  + explicit-typed local → IDE0007 ("use 'var'")        fires, IDE0008 does not
//   prefer explicit (false) + var local       → IDE0008 ("use explicit type") fires, IDE0007 does not
// This is the .editorconfig *input* stage; it is independent of CurationSet, which is a pure post-filter
// over already-emitted diagnostics.
public sealed class EditorConfigOptionsFlowTests
{
    [Fact]
    public async Task PreferVar_ExplicitLocal_ReportsUseVar_NotUseExplicitType()
    {
        const string source = """
            using System.Text;
            class C
            {
                StringBuilder Make() => new StringBuilder();
                void M()
                {
                    StringBuilder x = Make();
                    System.Console.WriteLine(x);
                }
            }
            """;

        var ids = await RunAnalyzersAsync(source, preferVar: true);

        Assert.Contains("IDE0007", ids);   // "Use 'var' instead of explicit type"
        Assert.DoesNotContain("IDE0008", ids);
    }

    [Fact]
    public async Task PreferExplicit_VarLocal_ReportsUseExplicitType_NotUseVar()
    {
        const string source = """
            using System.Text;
            class C
            {
                StringBuilder Make() => new StringBuilder();
                void M()
                {
                    var x = Make();
                    System.Console.WriteLine(x);
                }
            }
            """;

        var ids = await RunAnalyzersAsync(source, preferVar: false);

        Assert.Contains("IDE0008", ids);   // "Use explicit type instead of 'var'"
        Assert.DoesNotContain("IDE0007", ids);
    }

    // Builds an in-memory C# project with a /repo/.editorconfig that sets the var preference, then runs the
    // bundled analyzers through the production seam and returns the set of emitted diagnostic ids.
    private static async Task<HashSet<string>> RunAnalyzersAsync(string source, bool preferVar)
    {
        var v = preferVar ? "true" : "false";
        var editorConfig = $"""
            root = true

            [*.cs]
            csharp_style_var_for_built_in_types = {v}:suggestion
            csharp_style_var_when_type_is_apparent = {v}:suggestion
            csharp_style_var_elsewhere = {v}:suggestion
            """;

        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

        var projectInfo = ProjectInfo.Create(projectId, VersionStamp.Create(), "P", "P", LanguageNames.CSharp)
            .WithMetadataReferences(references)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var solution = workspace.CurrentSolution.AddProject(projectInfo);
        var docId = DocumentId.CreateNewId(projectId);
        solution = solution.AddDocument(docId, "A.cs", SourceText.From(source), filePath: "/repo/A.cs");
        var configId = DocumentId.CreateNewId(projectId);
        solution = solution.AddAnalyzerConfigDocument(
            configId, ".editorconfig", SourceText.From(editorConfig), filePath: "/repo/.editorconfig");

        var project = solution.GetProject(projectId)!;
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);

        var analyzers = AnalyzerLoader.LoadAll("net10.0");
        var diagnostics = await compilation!
            .WithProjectAnalyzers(analyzers, project)
            .GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        return diagnostics.Select(d => d.Id).ToHashSet();
    }
}
