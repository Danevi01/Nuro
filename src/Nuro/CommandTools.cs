using System.Diagnostics;
using System.Text;

namespace Nuro;

/// <summary>
/// Führt Befehle über cmd.exe aus. Die Oberfläche fragt vor JEDEM Aufruf beim Nutzer nach;
/// das Arbeitsverzeichnis muss in einem freigegebenen Ordner liegen.
/// </summary>
public sealed class CommandTools
{
    private readonly FileTools _files;
    public CommandTools(FileTools files) => _files = files;

    public async Task<string> RunAsync(string command, string workingDirectory, int timeoutSeconds)
    {
        if (string.IsNullOrWhiteSpace(command)) throw new ArgumentException("command fehlt.");
        var wd = _files.Resolve(workingDirectory);
        if (!Directory.Exists(wd)) throw new DirectoryNotFoundException($"Arbeitsordner nicht gefunden: {wd}");
        timeoutSeconds = Math.Clamp(timeoutSeconds, 5, 900);

        var psi = new ProcessStartInfo("cmd.exe", "/d /s /c \"chcp 65001 >nul & " + command + "\"")
        {
            WorkingDirectory = wd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var p = new Process { StartInfo = psi };
        var sw = Stopwatch.StartNew();
        p.Start();
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();

        bool timedOut = false;
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                timedOut = true;
                try { p.Kill(entireProcessTree: true); } catch { }
            }
        }

        var stdout = await outTask;
        var stderr = await errTask;
        var sb = new StringBuilder();
        sb.AppendLine(timedOut
            ? $"Abgebrochen nach {timeoutSeconds} s (Zeitlimit)."
            : $"Exit-Code {p.ExitCode} nach {sw.Elapsed.TotalSeconds:0.0} s in {wd}");
        if (stdout.Length > 0) sb.AppendLine("--- Ausgabe ---").AppendLine(stdout.TrimEnd());
        if (stderr.Length > 0) sb.AppendLine("--- Fehlerausgabe ---").AppendLine(stderr.TrimEnd());
        if (stdout.Length == 0 && stderr.Length == 0) sb.AppendLine("(keine Ausgabe)");
        return sb.ToString();
    }
}
