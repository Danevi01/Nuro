using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace Nuro;

public sealed class CommandTools
{
    public async Task<string> ExecuteAsync(string command, string? args, string? workingDirectory)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args ?? "",
                WorkingDirectory = workingDirectory ?? "",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = new Process { StartInfo = processInfo };
            var output = new StringBuilder();
            var error = new StringBuilder();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) error.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputRedirection();
            process.BeginErrorRedirection();

            process. WaitForExit();

            string result = output.ToString();
            if (process.ExitCode != 0)
            {
                result += $"\n[ERROR] ExitCode: {process.ExitCode}\n";
                result += error.ToString();
            }

            return result;
        }
        catch (Exception ex)
        {
            return $"[EXCEPTION] {ex.Message}";
        }
    }
}
