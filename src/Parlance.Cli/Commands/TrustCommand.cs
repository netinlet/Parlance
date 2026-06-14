using System.CommandLine;
using Parlance.Analyzers.Upstream;

namespace Parlance.Cli.Commands;

internal static class TrustCommand
{
    public static Command Create()
    {
        var pathArg = new Argument<string?>("path") { Description = "Path to a .dll file or directory", Arity = ArgumentArity.ZeroOrOne };
        var globalOption = new Option<bool>("--global", "-g") { Description = "Use the global trust file (~/.parlance/trusted_analyzers.json)" };
        var yesOption = new Option<bool>("--yes", "-y") { Description = "Skip confirmation prompt" };
        var listOption = new Option<bool>("--list") { Description = "List trusted entries" };
        var revokeOption = new Option<string?>("--revoke") { Description = "Path to a .dll file or directory to revoke" };

        var command = new Command("trust", "Manage trusted external analyzer DLLs");
        command.Add(pathArg);
        command.Add(globalOption);
        command.Add(yesOption);
        command.Add(listOption);
        command.Add(revokeOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var path = parseResult.GetValue(pathArg);
            var global = parseResult.GetValue(globalOption);
            var yes = parseResult.GetValue(yesOption);
            var list = parseResult.GetValue(listOption);
            var revoke = parseResult.GetValue(revokeOption);

            var trustFilePath = global
                ? AnalyzerTrustFile.GlobalPath
                : AnalyzerTrustFile.ProjectPath(Directory.GetCurrentDirectory());
            var trustFile = new AnalyzerTrustFile(trustFilePath);

            if (list)
            {
                var entries = trustFile.List();
                if (entries.IsEmpty)
                {
                    Console.WriteLine("No trusted analyzers.");
                }
                else
                {
                    foreach (var entry in entries)
                        Console.WriteLine($"{entry.DllPath}  {ShortHash(entry.Hash)}");
                }
                return;
            }

            if (revoke is not null)
            {
                await RevokeAsync(trustFile, revoke, ct);
                return;
            }

            if (path is null)
            {
                await Console.Error.WriteLineAsync("Provide a path to a .dll file or directory, or use --list / --revoke.");
                Environment.ExitCode = 2;
                return;
            }

            await TrustAsync(trustFile, path, yes, ct);
        });

        return command;
    }

    private static async Task TrustAsync(AnalyzerTrustFile trustFile, string path, bool yes, CancellationToken ct)
    {
        if (Directory.Exists(path))
        {
            var dlls = AnalyzerTrustFile.EnumerateAnalyzerDlls(path).ToList();
            if (dlls.Count == 0)
            {
                await Console.Error.WriteLineAsync($"No .dll files found in {path}");
                Environment.ExitCode = 2;
                return;
            }

            if (!yes)
            {
                Console.WriteLine($"About to trust {dlls.Count} DLL(s) in {path}");
                Console.Write("[y/N]: ");
                var input = Console.ReadLine();
                if (!string.Equals(input, "y", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Aborted.");
                    return;
                }
            }

            trustFile.TrustDirectory(path);
            var entries = trustFile.List();
            var entryMap = entries.ToDictionary(e => e.DllPath, e => e.Hash);
            foreach (var dll in dlls)
            {
                var canonical = Path.GetFullPath(dll);
                var hash = entryMap.TryGetValue(canonical, out var h) ? h : "(unknown)";
                Console.WriteLine($"Trusted: {canonical}  {ShortHash(hash)}");
            }
        }
        else if (File.Exists(path))
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                await Console.Error.WriteLineAsync("Path must be a .dll file or a directory.");
                Environment.ExitCode = 2;
                return;
            }

            if (!yes)
            {
                Console.WriteLine($"About to trust 1 DLL(s) in {path}");
                Console.Write("[y/N]: ");
                var input = Console.ReadLine();
                if (!string.Equals(input, "y", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Aborted.");
                    return;
                }
            }

            trustFile.Trust(path);
            var canonical = Path.GetFullPath(path);
            var entries = trustFile.List();
            var entry = entries.FirstOrDefault(e => e.DllPath == canonical);
            Console.WriteLine($"Trusted: {canonical}  {ShortHash(entry?.Hash ?? "(unknown)")}");
        }
        else
        {
            await Console.Error.WriteLineAsync($"Path not found: {path}");
            Environment.ExitCode = 2;
        }
    }

    private static async Task RevokeAsync(AnalyzerTrustFile trustFile, string path, CancellationToken ct)
    {
        if (Directory.Exists(path))
        {
            var dlls = AnalyzerTrustFile.EnumerateAnalyzerDlls(path).ToList();
            if (dlls.Count == 0)
            {
                await Console.Error.WriteLineAsync($"No .dll files found in {path}");
                Environment.ExitCode = 2;
                return;
            }

            trustFile.RevokeDirectory(path);
            foreach (var dll in dlls)
                Console.WriteLine($"Revoked: {Path.GetFullPath(dll)}");
        }
        else if (File.Exists(path))
        {
            trustFile.Revoke(path);
            Console.WriteLine($"Revoked: {Path.GetFullPath(path)}");
        }
        else
        {
            await Console.Error.WriteLineAsync($"Path not found: {path}");
            Environment.ExitCode = 2;
        }
    }

    private static string ShortHash(string hash)
    {
        const int PrefixLen = 7; // "sha256:" length
        const int ShortLen = 12;
        if (hash.StartsWith("sha256:", StringComparison.Ordinal) && hash.Length > PrefixLen + ShortLen)
            return hash[..(PrefixLen + ShortLen)] + "...";
        return hash;
    }
}
