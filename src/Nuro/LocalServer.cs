using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nuro;

/// <summary>
/// Kleiner Webserver nur auf 127.0.0.1. Liefert die eingebettete Oberfläche aus und stellt die Werkzeuge bereit.
/// Jeder API-Aufruf braucht den zufälligen Sitzungs-Token (Header X-Nuro-Token), den nur die eigene Oberfläche kennt.
/// </summary>
public sealed class LocalServer
{
    private readonly MainForm _form;
    private readonly Config _config;
    private readonly Tools _tools;
    private HttpListener? _listener;

    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    public int Port { get; private set; }
    public string Url => $"http://127.0.0.1:{Port}/";
    public static string Version { get; } =
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0").Split('+')[0];

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public LocalServer(MainForm form, Config config)
    {
        _form = form;
        _config = config;
        _tools = new Tools(config);
    }

    public void Start()
    {
        // Fester Port, damit die gespeicherten Chats (an den Port gebunden) erhalten bleiben
        foreach (var port in new[] { 8765, 8766, 8767, 8768, 8769 })
        {
            var l = new HttpListener();
            l.Prefixes.Add($"http://127.0.0.1:{port}/");
            try { l.Start(); _listener = l; Port = port; break; }
            catch (HttpListenerException) { l.Close(); }
        }
        if (_listener == null) throw new InvalidOperationException("Kein freier Port (8765–8769) gefunden.");
        _ = Task.Run(LoopAsync);
    }

    public void Stop() { try { _listener?.Stop(); _listener?.Close(); } catch { } }

    private async Task LoopAsync()
    {
        while (_listener is { IsListening: true })
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { break; }
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        try
        {
            // Schutz gegen DNS-Rebinding: nur exakt unser Host
            if (!string.Equals(req.UserHostName, $"127.0.0.1:{Port}", StringComparison.OrdinalIgnoreCase))
            {
                await WriteText(res, 403, "Forbidden");
                return;
            }

            var path = req.Url!.AbsolutePath;
            if (path.StartsWith("/api/", StringComparison.Ordinal))
            {
                if (req.HttpMethod != "POST" || req.Headers["X-Nuro-Token"] != Token)
                {
                    await WriteText(res, 403, "Forbidden");
                    return;
                }
                string bodyText;
                using (var sr = new StreamReader(req.InputStream, Encoding.UTF8)) bodyText = await sr.ReadToEndAsync();
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(bodyText) ? "{}" : bodyText);
                var result = await ApiAsync(path, doc.RootElement);
                await WriteJson(res, result);
                return;
            }

            await ServeStatic(res, path);
        }
        catch (Exception ex)
        {
            try { await WriteJson(res, new { ok = false, error = ex.Message }, 500); } catch { }
        }
        finally
        {
            try { res.Close(); } catch { }
        }
    }

    private object State() => new
    {
        version = Version,
        folders = _config.Folders,
        repo = _config.Repo,
        branch = _config.Branch,
        hasToken = !string.IsNullOrEmpty(_config.Token)
    };

    private async Task<object> ApiAsync(string path, JsonElement body)
    {
        switch (path)
        {
            case "/api/state":
                return State();

            case "/api/config":
            {
                if (body.TryGetProperty("repo", out var repo) && repo.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(repo.GetString()))
                    _config.Repo = repo.GetString()!.Trim().Trim('/');
                if (body.TryGetProperty("branch", out var br) && br.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(br.GetString()))
                    _config.Branch = br.GetString()!.Trim();
                if (body.TryGetProperty("token", out var tok) && tok.ValueKind == JsonValueKind.String)
                    _config.Token = tok.GetString();
                if (body.TryGetProperty("folders", out var f) && f.ValueKind == JsonValueKind.Array)
                    _config.Folders = f.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                _config.Save();
                return State();
            }

            case "/api/pick-folder":
            {
                var p = await _form.PickFolderAsync();
                if (p != null && !_config.Folders.Contains(p, StringComparer.OrdinalIgnoreCase))
                {
                    _config.Folders = _config.Folders.Append(p).ToList();
                    _config.Save();
                }
                return State();
            }

            case "/api/tool":
            {
                var name = body.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var args = body.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Object ? a : JsonDocument.Parse("{}").RootElement;
                try
                {
                    var r = await _tools.RunAsync(name, args);
                    return new { ok = true, result = Tools.Clip(r) };
                }
                catch (Exception ex)
                {
                    return new { ok = false, error = ex.Message };
                }
            }

            case "/api/gpu":
                return new { gpus = await GpuTools.QueryAsync(), models = await GpuTools.LoadedModelsAsync() };

            case "/api/github-test":
                return await _tools.GitHub.TestAsync();

            case "/api/update-check":
                return await Updater.CheckAsync(_tools.GitHub);

            case "/api/update-install":
            {
                var msg = await Updater.InstallAsync(_tools.GitHub);
                _ = Task.Delay(800).ContinueWith(_ => _form.Shutdown());
                return new { ok = true, message = msg };
            }

            default:
                return new { ok = false, error = "Unbekannter Endpunkt" };
        }
    }

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8", [".js"] = "application/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8", [".svg"] = "image/svg+xml", [".png"] = "image/png",
        [".ico"] = "image/x-icon", [".json"] = "application/json"
    };

    private async Task ServeStatic(HttpListenerResponse res, string path)
    {
        var rel = Uri.UnescapeDataString(path.TrimStart('/'));
        if (rel.Length == 0) rel = "index.html";
        if (rel.Contains("..")) { await WriteText(res, 404, "Nicht gefunden"); return; }

        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("wwwroot/" + rel);
        if (stream == null) { await WriteText(res, 404, "Nicht gefunden"); return; }

        byte[] bytes;
        using (var ms = new MemoryStream()) { await stream.CopyToAsync(ms); bytes = ms.ToArray(); }

        if (rel.Equals("index.html", StringComparison.OrdinalIgnoreCase))
        {
            var cfg = JsonSerializer.Serialize(new { token = Token, version = Version });
            var html = Encoding.UTF8.GetString(bytes).Replace("</head>", $"<script>window.NURO={cfg};</script>\n</head>");
            bytes = Encoding.UTF8.GetBytes(html);
        }

        res.StatusCode = 200;
        res.ContentType = Types.TryGetValue(Path.GetExtension(rel), out var t) ? t : "application/octet-stream";
        res.Headers["Cache-Control"] = "no-store";
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
    }

    private static async Task WriteJson(HttpListenerResponse res, object o, int status = 200)
    {
        var b = JsonSerializer.SerializeToUtf8Bytes(o, Json);
        res.StatusCode = status;
        res.ContentType = "application/json; charset=utf-8";
        res.Headers["Cache-Control"] = "no-store";
        res.ContentLength64 = b.Length;
        await res.OutputStream.WriteAsync(b);
    }

    private static async Task WriteText(HttpListenerResponse res, int status, string text)
    {
        var b = Encoding.UTF8.GetBytes(text);
        res.StatusCode = status;
        res.ContentType = "text/plain; charset=utf-8";
        res.ContentLength64 = b.Length;
        await res.OutputStream.WriteAsync(b);
    }
}
