using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Parlance.Mcp.Tests.Integration;

public sealed class McpServerIntegrationTests
{
    private static readonly string SolutionPath = FindSolutionPath();

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

    private static async Task<McpClient> CreateClientAsync(string solutionPath)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = ["run", "--no-build", "--project",
                Path.Combine(FindRepoRoot(), "src", "Parlance.Mcp", "Parlance.Mcp.csproj"),
                "--", "--solution-path", solutionPath],
            Name = "parlance-test"
        });

        return await McpClient.CreateAsync(transport);
    }

    private static string FindSolutionPath()
    {
        var root = FindRepoRoot();
        return Path.Combine(root, "Parlance.sln");
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
