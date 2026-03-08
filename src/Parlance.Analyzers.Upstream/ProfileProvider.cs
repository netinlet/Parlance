using System.Reflection;

namespace Parlance.Analyzers.Upstream;

internal static class ProfileProvider
{
    private static readonly string[] AvailableProfiles = ["default"];

    public static string GetProfileContent(string targetFramework, string profile)
    {
        if (!AvailableProfiles.Contains(profile))
            throw new ArgumentException(
                $"Unknown profile: '{profile}'. Available: {string.Join(", ", AvailableProfiles)}",
                nameof(profile));

        var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var profilePath = Path.Combine(baseDir, "profiles", targetFramework, $"{profile}.editorconfig");

        if (!File.Exists(profilePath))
            throw new ArgumentException(
                $"Profile '{profile}' not found for {targetFramework}",
                nameof(profile));

        return File.ReadAllText(profilePath);
    }

    public static IReadOnlyList<string> GetAvailableProfiles() => AvailableProfiles;
}
