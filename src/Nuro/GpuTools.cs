using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Nuro;

public record GpuInfo(string Name, int MemUsedMb, int MemTotalMb, int UtilPercent, int TempC, double PowerW, string Driver);
public record LoadedModel(string Name, long SizeBytes, long VramBytes);

/// <summary>Status der NVIDIA-Grafikkarte (über nvidia-smi) und der in Ollama geladenen Modelle.</summary>
public static class GpuTools
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public static async Task<List<GpuInfo>> QueryAsync()
    {
        var list = new List<GpuInfo>();
        try
        {
            var psi = new ProcessStartInfo("nvidia-smi",
                "--query-gpu=name,memory.used,memory.total,utilization.gpu,temperature.gpu,power.draw,driver_version --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return list;
            var output = await p.StandardOutput.ReadToEndAsync();
            using var cts = new CancellationTokenSource(5000);
            await p.WaitForExitAsync(cts.Token);
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var f = line.Split(',').Select(x => x.Trim()).ToArray();
                if (f.Length < 7) continue;
                list.Add(new GpuInfo(f[0], Int(f[1]), Int(f[2]), Int(f[3]), Int(f[4]),
                    double.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : 0, f[6]));
            }
        }
        catch { /* keine NVIDIA-Karte oder nvidia-smi fehlt */ }
        return list;
    }

    private static int Int(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d) : 0;

    public static async Task<List<LoadedModel>> LoadedModelsAsync()
    {
        var list = new List<LoadedModel>();
        try
        {
            var s = await Http.GetStringAsync(OllamaHelper.BaseUrl + "/api/ps");
            foreach (var m in JsonDocument.Parse(s).RootElement.GetProperty("models").EnumerateArray())
                list.Add(new LoadedModel(m.GetProperty("name").GetString() ?? "",
                    m.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0,
                    m.TryGetProperty("size_vram", out var sv) ? sv.GetInt64() : 0));
        }
        catch { }
        return list;
    }

    public static async Task<string> StatusTextAsync()
    {
        var sb = new StringBuilder();
        var gpus = await QueryAsync();
        if (gpus.Count == 0) sb.AppendLine("Keine NVIDIA-Grafikkarte gefunden (nvidia-smi nicht verfügbar).");
        foreach (var g in gpus)
            sb.AppendLine($"{g.Name}: VRAM {g.MemUsedMb} / {g.MemTotalMb} MB belegt, Auslastung {g.UtilPercent} %, {g.TempC} °C, {g.PowerW:0} W, Treiber {g.Driver}");
        var models = await LoadedModelsAsync();
        if (models.Count == 0) sb.AppendLine("In Ollama ist gerade kein Modell geladen.");
        foreach (var m in models)
        {
            var pct = m.SizeBytes > 0 ? 100.0 * m.VramBytes / m.SizeBytes : 0;
            sb.AppendLine($"Geladen: {m.Name} – {m.SizeBytes / 1e9:0.0} GB, davon {pct:0} % im Grafikspeicher");
        }
        return sb.ToString();
    }
}
