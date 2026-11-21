using System.Diagnostics;

namespace Nuro;

public static class OllamaHelper
{
    public const string BaseUrl = "http://127.0.0.1:11434";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public static async Task<bool> IsRunningAsync()
    {
        try { using var r = await Http.GetAsync(BaseUrl + "/api/version"); return r.IsSuccessStatusCode; }
        catch { return false; }
    }

    /// <summary>Startet Ollama, falls es nicht läuft.</summary>
    public static async Task EnsureRunningAsync()
    {
        if (await IsRunningAsync()) return;
        try
        {
            var app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama app.exe");
            if (File.Exists(app))
                Process.Start(new ProcessStartInfo(app) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("ollama", "serve") { UseShellExecute = false, CreateNoWindow = true });
        }
        catch { return; }
        for (int i = 0; i < 20 && !await IsRunningAsync(); i++) await Task.Delay(500);
    }
}
