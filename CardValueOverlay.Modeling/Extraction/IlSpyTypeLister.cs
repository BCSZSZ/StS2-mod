using System.Diagnostics;

namespace CardValueOverlay.Modeling.Extraction;

public sealed class IlSpyTypeLister
{
    public async Task<IReadOnlyList<string>> ListClassesAsync(ExtractionPaths paths, CancellationToken cancellationToken = default)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = paths.IlSpyPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("-l");
        startInfo.ArgumentList.Add("c");
        startInfo.ArgumentList.Add(paths.Sts2DllPath);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start ilspycmd at {paths.IlSpyPath}");

        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ilspycmd failed with exit code {process.ExitCode}: {error}");
        }

        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("Class ", StringComparison.Ordinal))
            .Select(line => line["Class ".Length..])
            .ToArray();
    }
}
