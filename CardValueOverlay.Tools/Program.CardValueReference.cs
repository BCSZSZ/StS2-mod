using System.Diagnostics;

namespace CardValueOverlay.Tools;

internal static partial class Program
{
    // Regenerates the card-value reference table (Markdown + XLSX) from the runtime overlay values
    // and the extracted EN/中文 localization. The XLSX side needs openpyxl, so the actual generation
    // lives in a PEP 723 single-file Python script run through uv (dependencies auto-provisioned; no
    // manual pip/venv, and it re-provisions on a fresh machine as long as uv is on PATH). This command
    // is a thin wrapper: it resolves defaults and shells out to `uv run`.
    private static int WriteCardValueReference(string[] args)
    {
        string config = GetOption(args, "--config") ?? DefaultConfigPath;
        string localization = GetOption(args, "--localization")
            ?? Path.Combine("history-analysis", "data", "localized_names_en_zhs.json");
        string outputDir = GetOption(args, "--output-dir")
            ?? Path.Combine("data", "manual-tags");
        string date = GetOption(args, "--date") ?? DateTime.Now.ToString("yyyy-MM-dd");
        string script = GetOption(args, "--script")
            ?? Path.Combine("scripts", "generate_card_value_reference.py");

        if (!File.Exists(config))
        {
            return Fail($"Missing config at {config}.");
        }

        if (!File.Exists(localization))
        {
            return Fail($"Missing localization at {localization}.");
        }

        if (!File.Exists(script))
        {
            return Fail($"Missing generator script at {script}.");
        }

        ProcessStartInfo psi = new()
        {
            FileName = "uv",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add("--config");
        psi.ArgumentList.Add(config);
        psi.ArgumentList.Add("--localization");
        psi.ArgumentList.Add(localization);
        psi.ArgumentList.Add("--output-dir");
        psi.ArgumentList.Add(outputDir);
        psi.ArgumentList.Add("--date");
        psi.ArgumentList.Add(date);

        Process process;
        try
        {
            process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start 'uv'.");
        }
        catch (Exception ex)
        {
            return Fail($"Could not launch 'uv run' (is uv installed and on PATH?): {ex.Message}");
        }

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            Console.Write(stdout);
        }

        if (process.ExitCode != 0)
        {
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Console.Error.Write(stderr);
            }

            return Fail($"uv run failed with exit code {process.ExitCode}.");
        }

        return 0;
    }
}
