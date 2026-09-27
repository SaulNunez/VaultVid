using System.Diagnostics;
using System.Text;

namespace VideoHostingService.Worker;

public record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;

    /// <summary>The last few lines of stderr, which is where ffmpeg puts the actual reason.</summary>
    public string ErrorSummary
    {
        get
        {
            var lines = StandardError
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .TakeLast(3);

            var summary = string.Join(" / ", lines);
            return string.IsNullOrWhiteSpace(summary) ? $"exit code {ExitCode}" : summary;
        }
    }
}

public static class ProcessRunner
{
    /// <summary>
    /// Runs a command to completion, capturing both streams. Both are drained concurrently with
    /// the wait: ffmpeg writes progress to stderr continuously and will block on a full pipe if
    /// nothing is reading it.
    /// </summary>
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        logger.LogDebug("Running {FileName} {Arguments}", fileName, string.Join(' ', startInfo.ArgumentList));

        using var process = new Process { StartInfo = startInfo };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start {fileName}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process, logger);
            throw;
        }

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static void TryKill(Process process, ILogger logger)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not kill the child process after cancellation");
        }
    }
}
