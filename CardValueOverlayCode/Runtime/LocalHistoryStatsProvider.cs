using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CardValueOverlay.Core.Adoption;
using CardValueOverlay.Core.History;
using Microsoft.Win32;

namespace CardValueOverlay.CardValueOverlayCode.Runtime;

public static class LocalHistoryStatsProvider
{
    private const string HistoryRootOverrideVariable = "STS2_RUN_HISTORY_ROOT";
    private const string Sts2SteamAppId = "2868840";
    private static bool initializationAttempted;

    public static void Initialize()
    {
        if (initializationAttempted)
        {
            return;
        }
        initializationAttempted = true;

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            CardAdoptionCatalog? referenceCatalog = CardAdoptionStatsProvider.ReferenceCatalog;
            if (referenceCatalog is null)
            {
                MainFile.Logger.Warn(
                    "Local history statistics were not calculated because the global adoption catalog failed to load.",
                    0);
                return;
            }

            IReadOnlyList<string> paths = FindRunHistoryFiles();
            HashSet<string> contentHashes = new(StringComparer.Ordinal);
            List<LocalRunHistorySource> sources = new(paths.Count);
            int duplicateFiles = 0;
            int unreadableFiles = 0;
            foreach (string path in paths)
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    string hash = Convert.ToHexString(SHA256.HashData(bytes));
                    if (!contentHashes.Add(hash))
                    {
                        duplicateFiles++;
                        continue;
                    }
                    sources.Add(new LocalRunHistorySource(
                        System.IO.Path.GetFileNameWithoutExtension(path),
                        Encoding.UTF8.GetString(bytes)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    unreadableFiles++;
                }
            }

            LocalHistoryStatsBuildResult result = LocalRunHistoryStatsBuilder.Build(
                sources,
                referenceCatalog);
            CardAdoptionStatsProvider.SetLocalCatalog(result.CardAdoption);
            AncientChoiceStatsProvider.SetLocalCatalog(result.AncientChoices);
            stopwatch.Stop();
            int ancientScreens = result.AncientChoices.Characters.Values
                .Sum(character => character.TotalChoiceScreens);

            MainFile.Logger.Info(
                "Calculated local history statistics at startup. "
                + $"files={paths.Count}, unique={sources.Count}, duplicates={duplicateFiles}, "
                + $"unreadable={unreadableFiles}, parsed={result.ParsedRuns}, "
                + $"included={result.IncludedRuns}, outcomeIncluded={result.OutcomeIncludedRuns}, "
                + $"filtered={result.FilteredRuns}, "
                + $"invalid={result.InvalidRuns}, cards={result.CardAdoption.Cards.Count}, "
                + $"ancientScreens={ancientScreens}, "
                + $"elapsedMs={stopwatch.ElapsedMilliseconds}.",
                0);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Failed to calculate local history statistics: {ex}", 0);
        }
    }

    private static IReadOnlyList<string> FindRunHistoryFiles()
    {
        string? overrideRoot = Environment.GetEnvironmentVariable(HistoryRootOverrideVariable);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return EnumerateRunFiles(overrideRoot);
        }

        HashSet<string> userdataRoots = new(StringComparer.OrdinalIgnoreCase);
        AddSteamUserdataRoot(userdataRoots, TryReadSteamInstallPath());
        AddSteamUserdataRoot(userdataRoots, Environment.GetEnvironmentVariable("ProgramFiles(x86)"));
        AddSteamUserdataRoot(userdataRoots, Environment.GetEnvironmentVariable("ProgramFiles"));

        HashSet<string> files = new(StringComparer.OrdinalIgnoreCase);
        foreach (string userdataRoot in userdataRoots)
        {
            try
            {
                foreach (string accountRoot in Directory.EnumerateDirectories(userdataRoot))
                {
                    string appRemoteRoot = System.IO.Path.Combine(
                        accountRoot,
                        Sts2SteamAppId,
                        "remote");
                    foreach (string file in EnumerateRunFiles(appRemoteRoot))
                    {
                        files.Add(file);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Another configured Steam account/root may still be readable.
            }
        }

        return files.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> EnumerateRunFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }
        try
        {
            EnumerationOptions options = new()
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive
            };
            return Directory.EnumerateFiles(root, "*.run", options)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void AddSteamUserdataRoot(HashSet<string> roots, string? steamOrProgramFilesRoot)
    {
        if (string.IsNullOrWhiteSpace(steamOrProgramFilesRoot))
        {
            return;
        }

        string directUserdata = System.IO.Path.Combine(steamOrProgramFilesRoot, "userdata");
        string nestedUserdata = System.IO.Path.Combine(steamOrProgramFilesRoot, "Steam", "userdata");
        if (Directory.Exists(directUserdata))
        {
            roots.Add(System.IO.Path.GetFullPath(directUserdata));
        }
        if (Directory.Exists(nestedUserdata))
        {
            roots.Add(System.IO.Path.GetFullPath(nestedUserdata));
        }
    }

    private static string? TryReadSteamInstallPath()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }
        try
        {
            using RegistryKey? steamKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return steamKey?.GetValue("SteamPath") as string;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
