using System.Diagnostics;

namespace CardValueOverlay.Modeling.Extraction;

public sealed class IlSpyDecompiler
{
    public async Task<string> EnsureProjectDecompiledAsync(
        ExtractionPaths paths,
        bool refresh,
        CancellationToken cancellationToken = default)
    {
        if (refresh && Directory.Exists(paths.DecompileOutputRoot))
        {
            DeleteRefreshTarget(paths);
        }

        if (Directory.Exists(paths.DecompileOutputRoot)
            && Directory.EnumerateFiles(paths.DecompileOutputRoot, "*.cs", SearchOption.AllDirectories).Any())
        {
            return paths.DecompileOutputRoot;
        }

        Directory.CreateDirectory(paths.DecompileOutputRoot);

        ProcessStartInfo startInfo = new()
        {
            FileName = paths.IlSpyPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("--nested-directories");
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(paths.DecompileOutputRoot);
        startInfo.ArgumentList.Add("-r");
        startInfo.ArgumentList.Add(paths.Sts2DataDir);
        startInfo.ArgumentList.Add(paths.Sts2DllPath);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start ilspycmd at {paths.IlSpyPath}");

        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ilspycmd decompile failed with exit code {process.ExitCode}: {error}{Environment.NewLine}{output}");
        }

        return paths.DecompileOutputRoot;
    }

    private static void DeleteRefreshTarget(ExtractionPaths paths)
    {
        string target = Path.GetFullPath(paths.DecompileOutputRoot);
        string allowedRoot = Path.GetFullPath(Path.Combine(paths.GeneratedOutputRoot, "decompiled"));

        if (!IsSameOrChildPath(target, allowedRoot))
        {
            throw new InvalidOperationException(
                $"Refusing to refresh decompile output outside generated decompile cache: {target}");
        }

        Directory.Delete(target, recursive: true);
    }

    private static bool IsSameOrChildPath(string path, string root)
    {
        string normalizedRoot = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
