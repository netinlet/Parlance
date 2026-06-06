namespace Parlance.Cli;

internal static class PathExtensions
{
    extension(string path)
    {
        public bool IsSolution =>
            path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

        public bool IsProject => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

        public bool IsLoadableProject => path.IsSolution || path.IsProject;
    }
}
