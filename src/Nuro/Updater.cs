using System.Diagnostics;
using System.Text.Json;

namespace Nuro;

/// <summary>Prüft das neueste Release im eigenen Repo und tauscht die laufende Nuro.exe dagegen aus.</summary>
public static class Updater
{
    public static async Task<object> CheckAsync(GitHubTools gh)
    {
        try
        {
            var json = await gh.GetRawAsync("releases/latest");
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var latest = tag.TrimStart('v', 'V');
            var url = doc.RootElement.GetProperty("html_url").GetString();
            var hasAsset = doc.RootElement.GetProperty("assets").EnumerateArray()
                .Any(a => string.Equals(a.GetProperty("name").GetString(), "Nuro.exe", StringComparison.OrdinalIgnoreCase));
            return new { ok = true, current = LocalServer.Version, latest, newer = hasAsset && IsNewer(latest, LocalServer.Version), url };
        }
        catch (Exception ex)
        {
            return new { ok = false, error = ex.Message, current = LocalServer.Version };
        }
    }

    private static bool IsNewer(string a, string b) =>
        Version.TryParse(a, out var va) && Version.TryParse(b, out var vb) && va > vb;

    public static async Task<string> InstallAsync(GitHubTools gh)
    {
        var json = await gh.GetRawAsync("releases/latest");
        using var doc = JsonDocument.Parse(json);
        var asset = doc.RootElement.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(a => string.Equals(a.GetProperty("name").GetString(), "Nuro.exe", StringComparison.OrdinalIgnoreCase));
        if (asset.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Im neuesten Release gibt es keine Nuro.exe.");
        var assetUrl = asset.GetProperty("url").GetString()!;

        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Pfad der exe unbekannt.");
        var dir = Path.GetDirectoryName(exe)!;
        var newExe = Path.Combine(dir, "Nuro.update.exe");
        await gh.DownloadAssetAsync(assetUrl, newExe);

        var bat = Path.Combine(Path.GetTempPath(), "nuro-update.cmd");
        File.WriteAllText(bat,
            "@echo off\r\n" +
            "set n=0\r\n" +
            ":retry\r\n" +
            "set /a n+=1\r\n" +
            "timeout /t 1 /nobreak >nul\r\n" +
            $"move /y \"{newExe}\" \"{exe}\" >nul 2>&1 && goto ok\r\n" +
            "if %n% lss 30 goto retry\r\n" +
            "exit /b 1\r\n" +
            ":ok\r\n" +
            $"start \"\" \"{exe}\"\r\n" +
            "(goto) 2>nul & del \"%~f0\"\r\n");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        return "Update wird installiert, Nuro startet gleich neu.";
    }
}
