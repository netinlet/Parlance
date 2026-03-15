namespace Parlance.CSharp.Workspace.Tests.Integration;

internal static class TestPaths
{
    public static string FindSolutionPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var sln = Path.Combine(dir, "Parlance.sln");
            if (File.Exists(sln)) return sln;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            "Could not find Parlance.sln — run tests from within the repo");
    }

    public static string RepoRoot => Path.GetDirectoryName(FindSolutionPath())!;
}
