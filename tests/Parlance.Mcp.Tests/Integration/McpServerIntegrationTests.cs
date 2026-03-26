using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Parlance.Mcp.Tests.Integration;

public sealed class McpServerIntegrationTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string SolutionPath = Path.Combine(RepoRoot, "Parlance.sln");

    [Fact]
    public async Task WorkspaceStatus_ReturnsLoadedProjects()
    {
        await using var client = await CreateClientAsync(SolutionPath);

        var tools = await client.ListToolsAsync();
        var workspaceStatusTool = Assert.Single(tools, t => t.Name == "workspace-status");
        Assert.Contains("workspace health", workspaceStatusTool.Description, StringComparison.OrdinalIgnoreCase);

        var result = await client.CallToolAsync("workspace-status");

        Assert.True(result.IsError is not true);
        var textBlock = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));

        using var doc = JsonDocument.Parse(textBlock.Text!);
        var root = doc.RootElement;

        var status = root.GetProperty("status").GetString();
        Assert.True(status is "Loaded" or "Degraded", $"Expected Loaded or Degraded, got {status}");
        Assert.Equal(SolutionPath, root.GetProperty("solutionPath").GetString());
        Assert.True(root.GetProperty("snapshotVersion").GetInt64() >= 1);
        Assert.True(root.GetProperty("projectCount").GetInt32() > 0);

        var projects = root.GetProperty("projects");
        Assert.True(projects.GetArrayLength() > 0);

        var firstProject = projects[0];
        Assert.False(string.IsNullOrEmpty(firstProject.GetProperty("name").GetString()));
        Assert.False(string.IsNullOrEmpty(firstProject.GetProperty("path").GetString()));
        Assert.Equal("Loaded", firstProject.GetProperty("status").GetString());
    }

    [Fact]
    public async Task WorkspaceStatus_InvalidSolutionPath_ReturnsFailedStatus()
    {
        var bogusPath = Path.Combine(Path.GetTempPath(), "nonexistent.sln");
        await using var client = await CreateClientAsync(bogusPath);

        var result = await client.CallToolAsync("workspace-status");

        Assert.True(result.IsError is not true);
        var textBlock = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));

        using var doc = JsonDocument.Parse(textBlock.Text!);
        var root = doc.RootElement;

        Assert.Equal("Failed", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("diagnostics").GetArrayLength() > 0);
    }

    [Fact]
    public async Task ListTools_ReturnsAllSemanticTools()
    {
        await using var client = await CreateClientAsync(SolutionPath);
        var tools = await client.ListToolsAsync();
        var toolNames = tools.Select(t => t.Name).ToHashSet();

        Assert.Contains("workspace-status", toolNames);
        Assert.Contains("describe-type", toolNames);
        Assert.Contains("find-implementations", toolNames);
        Assert.Contains("find-references", toolNames);
        Assert.Contains("get-type-at", toolNames);
        Assert.Contains("outline-file", toolNames);
        Assert.Contains("get-symbol-docs", toolNames);
        Assert.Contains("call-hierarchy", toolNames);
        Assert.Contains("get-type-dependencies", toolNames);
        Assert.Contains("safe-to-delete", toolNames);
        Assert.Contains("decompile-type", toolNames);
    }

    [Fact]
    public async Task DescribeType_ReturnsTypeInfo()
    {
        await using var client = await CreateClientAsync(SolutionPath);
        var result = await client.CallToolAsync("describe-type",
            new Dictionary<string, object?> { ["typeName"] = "CSharpWorkspaceSession" });

        Assert.True(result.IsError is not true);
        var textBlock = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        using var doc = JsonDocument.Parse(textBlock.Text!);
        var root = doc.RootElement;
        Assert.Equal("found", root.GetProperty("status").GetString());
        Assert.Equal("Parlance.CSharp.Workspace.CSharpWorkspaceSession",
            root.GetProperty("fullyQualifiedName").GetString());
        Assert.True(root.GetProperty("line").GetInt32() > 0,
            "Expected 1-based line number > 0");
    }

    [Fact]
    public async Task DescribeType_AmbiguousName_ReturnsAmbiguous()
    {
        await using var client = await CreateClientAsync(SolutionPath);
        // "Diagnostic" exists in both Parlance.Abstractions and Microsoft.CodeAnalysis
        var result = await client.CallToolAsync("describe-type",
            new Dictionary<string, object?> { ["typeName"] = "Diagnostic" });

        Assert.True(result.IsError is not true);
        var textBlock = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        using var doc = JsonDocument.Parse(textBlock.Text!);
        var status = doc.RootElement.GetProperty("status").GetString();
        // Either found (solution-first ordering selected one) or ambiguous — both are acceptable;
        // the key assertion is that it does NOT silently return a wrong symbol.
        Assert.True(status is "found" or "ambiguous",
            $"Expected 'found' or 'ambiguous', got '{status}'");
        if (status == "found")
            Assert.Equal("Parlance.Abstractions.Diagnostic",
                doc.RootElement.GetProperty("fullyQualifiedName").GetString());
    }

    [Fact]
    public async Task FindImplementations_ReturnsResults()
    {
        await using var client = await CreateClientAsync(SolutionPath);
        var result = await client.CallToolAsync("find-implementations",
            new Dictionary<string, object?> { ["typeName"] = "IAnalysisEngine" });

        Assert.True(result.IsError is not true);
        var textBlock = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        using var doc = JsonDocument.Parse(textBlock.Text!);
        var root = doc.RootElement;
        Assert.Equal("found", root.GetProperty("status").GetString());
        Assert.Equal("Parlance.Abstractions.IAnalysisEngine", root.GetProperty("targetType").GetString());
        Assert.True(root.GetProperty("count").GetInt32() > 0);
        var impls = root.GetProperty("implementations");
        Assert.True(impls.GetArrayLength() > 0);
        Assert.All(Enumerable.Range(0, impls.GetArrayLength()).Select(i => impls[i]),
            impl => Assert.True(impl.GetProperty("line").GetInt32() > 0, "Expected 1-based line"));
    }

    [Fact]
    public async Task FindReferences_ReturnsResults()
    {
        await using var client = await CreateClientAsync(SolutionPath);
        var result = await client.CallToolAsync("find-references",
            new Dictionary<string, object?> { ["symbolName"] = "IAnalysisEngine" });

        Assert.True(result.IsError is not true);
        var textBlock = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        using var doc = JsonDocument.Parse(textBlock.Text!);
        Assert.Equal("found", doc.RootElement.GetProperty("status").GetString());
        Assert.True(doc.RootElement.GetProperty("totalCount").GetInt32() > 0);
    }

    [Fact]
    public async Task OutlineFile_ReturnsTypes()
    {
        await using var client = await CreateClientAsync(SolutionPath);
        var filePath = Path.Combine(RepoRoot, "src", "Parlance.Abstractions", "IAnalysisEngine.cs");
        var result = await client.CallToolAsync("outline-file",
            new Dictionary<string, object?> { ["filePath"] = filePath });

        Assert.True(result.IsError is not true);
        var textBlock = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        using var doc = JsonDocument.Parse(textBlock.Text!);
        Assert.Equal("found", doc.RootElement.GetProperty("status").GetString());
        Assert.True(doc.RootElement.GetProperty("types").GetArrayLength() > 0);
    }

    [Fact]
    public async Task SafeToDelete_ReturnsResult()
    {
        await using var client = await CreateClientAsync(SolutionPath);
        var result = await client.CallToolAsync("safe-to-delete",
            new Dictionary<string, object?> { ["symbolName"] = "IAnalysisEngine" });

        Assert.True(result.IsError is not true);
        var textBlock = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        using var doc = JsonDocument.Parse(textBlock.Text!);
        Assert.Equal("found", doc.RootElement.GetProperty("status").GetString());
        Assert.True(doc.RootElement.GetProperty("referenceCount").GetInt32() > 0);
    }

    private static async Task<McpClient> CreateClientAsync(string solutionPath)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = ["run", "--no-build", "--configuration", GetConfiguration(), "--project",
                Path.Combine(RepoRoot, "src", "Parlance.Mcp", "Parlance.Mcp.csproj"),
                "--", "--solution-path", solutionPath],
            Name = "parlance-test"
        });

        return await McpClient.CreateAsync(transport);
    }

    private static string GetConfiguration()
    {
        var baseDir = AppContext.BaseDirectory;
        return baseDir.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
    }

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Parlance.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Cannot find Parlance.sln in parent directories");
    }
}
