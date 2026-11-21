using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Nuro;

/// <summary>Zugriff auf das eigene GitHub-Repository (Standard: Danevi01/Nuro) über die REST-API.</summary>
public sealed class GitHubTools
{
    private readonly Config _c;
    private static readonly HttpClient Http = CreateClient();

    public GitHubTools(Config c) => _c = c;

    private static HttpClient CreateClient()
    {
        var h = new HttpClient { BaseAddress = new Uri("https://api.github.com/"), Timeout = TimeSpan.FromSeconds(90) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Nuro/" + LocalServer.Version);
        h.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return h;
    }

    private string Repo => _c.Repo.Trim().Trim('/');
    private string Branch => _c.Branch;

    private static string P(string? path) =>
        string.Join("/", (path ?? "").Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

    private HttpRequestMessage Req(HttpMethod m, string url, object? body = null, string accept = "application/vnd.github+json")
    {
        var token = _c.Token;
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("Kein GitHub-Token hinterlegt. Der Nutzer kann ihn in den Einstellungen eintragen.");
        var r = new HttpRequestMessage(m, url);
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        r.Headers.Accept.ParseAdd(accept);
        if (body != null) r.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return r;
    }

    private static async Task<string?> SendAsync(HttpRequestMessage r, bool allow404 = false)
    {
        using (r)
        using (var res = await Http.SendAsync(r))
        {
            var s = await res.Content.ReadAsStringAsync();
            if (allow404 && res.StatusCode == HttpStatusCode.NotFound) return null;
            if (!res.IsSuccessStatusCode)
            {
                string msg = s;
                try { msg = JsonDocument.Parse(s).RootElement.GetProperty("message").GetString() ?? s; } catch { }
                var hint = res.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => " (Token ungültig oder abgelaufen)",
                    HttpStatusCode.Forbidden => " (Token hat nicht die nötigen Rechte)",
                    HttpStatusCode.NotFound => " (nicht gefunden – oder der Token hat keinen Zugriff auf dieses Repo)",
                    _ => ""
                };
                throw new HttpRequestException($"GitHub {(int)res.StatusCode}: {msg}{hint}");
            }
            return s;
        }
    }

    /// <summary>GET auf repos/{repo}/{rel} und rohe JSON-Antwort.</summary>
    public async Task<string> GetRawAsync(string rel) =>
        (await SendAsync(Req(HttpMethod.Get, $"repos/{Repo}/{rel}")))!;

    public async Task DownloadAssetAsync(string assetApiUrl, string target)
    {
        using var r = Req(HttpMethod.Get, assetApiUrl, accept: "application/octet-stream");
        using var res = await Http.SendAsync(r, HttpCompletionOption.ResponseHeadersRead);
        res.EnsureSuccessStatusCode();
        await using var fs = File.Create(target);
        await res.Content.CopyToAsync(fs);
    }

    public async Task<object> TestAsync()
    {
        try
        {
            var s = await SendAsync(Req(HttpMethod.Get, $"repos/{Repo}"));
            var root = JsonDocument.Parse(s!).RootElement;
            bool push = root.TryGetProperty("permissions", out var p) && p.TryGetProperty("push", out var pp) && pp.GetBoolean();
            return new { ok = true, name = root.GetProperty("full_name").GetString(), isPrivate = root.GetProperty("private").GetBoolean(), push };
        }
        catch (Exception ex) { return new { ok = false, error = ex.Message }; }
    }

    public async Task<string> ListAsync(string path, bool recursive)
    {
        var sb = new StringBuilder();
        if (recursive)
        {
            var s = await SendAsync(Req(HttpMethod.Get, $"repos/{Repo}/git/trees/{Uri.EscapeDataString(Branch)}?recursive=1"));
            var root = JsonDocument.Parse(s!).RootElement;
            var prefix = (path ?? "").Replace('\\', '/').Trim('/');
            int n = 0;
            foreach (var e in root.GetProperty("tree").EnumerateArray())
            {
                var p = e.GetProperty("path").GetString()!;
                if (prefix.Length > 0 && !p.StartsWith(prefix + "/", StringComparison.Ordinal) && p != prefix) continue;
                var isDir = e.GetProperty("type").GetString() == "tree";
                sb.AppendLine(isDir ? p + "/" : $"{p}  ({(e.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0)} B)");
                if (++n >= 1500) { sb.AppendLine("… (gekürzt)"); break; }
            }
            return sb.Length == 0 ? "(leer)" : $"{Repo}@{Branch}\n" + sb;
        }
        var json = await SendAsync(Req(HttpMethod.Get, $"repos/{Repo}/contents/{P(path)}?ref={Uri.EscapeDataString(Branch)}"));
        var el = JsonDocument.Parse(json!).RootElement;
        if (el.ValueKind != JsonValueKind.Array) return $"„{path}“ ist eine Datei – zum Lesen gh_read verwenden.";
        foreach (var e in el.EnumerateArray())
        {
            var isDir = e.GetProperty("type").GetString() == "dir";
            sb.AppendLine(isDir ? e.GetProperty("path").GetString() + "/" : $"{e.GetProperty("path").GetString()}  ({e.GetProperty("size").GetInt64()} B)");
        }
        return sb.Length == 0 ? "(leer)" : sb.ToString();
    }

    public async Task<string> ReadAsync(string path)
    {
        var s = await SendAsync(Req(HttpMethod.Get, $"repos/{Repo}/contents/{P(path)}?ref={Uri.EscapeDataString(Branch)}", accept: "application/vnd.github.raw+json"));
        return s ?? "";
    }

    private async Task<string?> ShaAsync(string path)
    {
        var s = await SendAsync(Req(HttpMethod.Get, $"repos/{Repo}/contents/{P(path)}?ref={Uri.EscapeDataString(Branch)}"), allow404: true);
        if (s == null) return null;
        var el = JsonDocument.Parse(s).RootElement;
        if (el.ValueKind == JsonValueKind.Array) throw new InvalidOperationException($"„{path}“ ist ein Ordner.");
        return el.GetProperty("sha").GetString();
    }

    public async Task<string> WriteAsync(string path, string content, string message)
    {
        var sha = await ShaAsync(path);
        var body = new Dictionary<string, object>
        {
            ["message"] = message,
            ["content"] = Convert.ToBase64String(new UTF8Encoding(false).GetBytes(content)),
            ["branch"] = Branch
        };
        if (sha != null) body["sha"] = sha;
        var s = await SendAsync(Req(HttpMethod.Put, $"repos/{Repo}/contents/{P(path)}", body));
        var c = JsonDocument.Parse(s!).RootElement.GetProperty("commit");
        return $"{(sha == null ? "Neu angelegt" : "Aktualisiert")}: {path}\nCommit {c.GetProperty("sha").GetString()![..7]} auf {Branch}: {message}\n{c.GetProperty("html_url").GetString()}";
    }

    public async Task<string> EditAsync(string path, string oldText, string newText, string message)
    {
        if (string.IsNullOrEmpty(oldText)) throw new ArgumentException("old_text darf nicht leer sein.");
        var text = await ReadAsync(path);
        int count = 0, i = 0;
        while ((i = text.IndexOf(oldText, i, StringComparison.Ordinal)) >= 0) { count++; i += oldText.Length; }
        if (count == 0) throw new InvalidOperationException("old_text kommt in der Datei nicht vor. Lies die Datei mit gh_read und übernimm den Text exakt.");
        if (count > 1) throw new InvalidOperationException($"old_text kommt {count}-mal vor. Nimm mehr umgebenden Text dazu.");
        return await WriteAsync(path, text.Replace(oldText, newText), message);
    }

    public async Task<string> DeleteAsync(string path, string message)
    {
        var sha = await ShaAsync(path) ?? throw new FileNotFoundException($"„{path}“ gibt es im Repo nicht.");
        var s = await SendAsync(Req(HttpMethod.Delete, $"repos/{Repo}/contents/{P(path)}", new { message, sha, branch = Branch }));
        var c = JsonDocument.Parse(s!).RootElement.GetProperty("commit");
        return $"Gelöscht: {path}\nCommit {c.GetProperty("sha").GetString()![..7]}: {message}";
    }

    public async Task<string> CommitsAsync(int limit)
    {
        limit = Math.Clamp(limit, 1, 50);
        var s = await SendAsync(Req(HttpMethod.Get, $"repos/{Repo}/commits?sha={Uri.EscapeDataString(Branch)}&per_page={limit}"));
        var sb = new StringBuilder();
        foreach (var e in JsonDocument.Parse(s!).RootElement.EnumerateArray())
        {
            var c = e.GetProperty("commit");
            var msg = (c.GetProperty("message").GetString() ?? "").Split('\n')[0];
            var date = c.GetProperty("author").GetProperty("date").GetDateTime().ToLocalTime();
            sb.AppendLine($"{e.GetProperty("sha").GetString()![..7]}  {date:dd.MM.yyyy HH:mm}  {c.GetProperty("author").GetProperty("name").GetString()}: {msg}");
        }
        return sb.ToString();
    }

    public async Task<string> IssuesAsync(string state)
    {
        if (state is not ("open" or "closed" or "all")) state = "open";
        var s = await SendAsync(Req(HttpMethod.Get, $"repos/{Repo}/issues?state={state}&per_page=50"));
        var sb = new StringBuilder();
        foreach (var e in JsonDocument.Parse(s!).RootElement.EnumerateArray())
        {
            if (e.TryGetProperty("pull_request", out _)) continue;
            var labels = string.Join(", ", e.GetProperty("labels").EnumerateArray().Select(l => l.GetProperty("name").GetString()));
            sb.AppendLine($"#{e.GetProperty("number").GetInt32()} [{e.GetProperty("state").GetString()}] {e.GetProperty("title").GetString()}{(labels.Length > 0 ? $" ({labels})" : "")}");
        }
        return sb.Length == 0 ? "Keine Issues." : sb.ToString();
    }

    public async Task<string> IssueReadAsync(int number)
    {
        var s = await SendAsync(Req(HttpMethod.Get, $"repos/{Repo}/issues/{number}"));
        var e = JsonDocument.Parse(s!).RootElement;
        var sb = new StringBuilder();
        sb.AppendLine($"#{number} [{e.GetProperty("state").GetString()}] {e.GetProperty("title").GetString()}");
        sb.AppendLine(e.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : "(kein Text)");
        var cs = await SendAsync(Req(HttpMethod.Get, $"repos/{Repo}/issues/{number}/comments?per_page=50"));
        foreach (var c in JsonDocument.Parse(cs!).RootElement.EnumerateArray())
            sb.AppendLine($"\n--- {c.GetProperty("user").GetProperty("login").GetString()}:\n{c.GetProperty("body").GetString()}");
        return sb.ToString();
    }

    public async Task<string> IssueCreateAsync(string title, string body)
    {
        var s = await SendAsync(Req(HttpMethod.Post, $"repos/{Repo}/issues", new { title, body }));
        var e = JsonDocument.Parse(s!).RootElement;
        return $"Issue #{e.GetProperty("number").GetInt32()} angelegt: {e.GetProperty("html_url").GetString()}";
    }

    public async Task<string> IssueCommentAsync(int number, string body)
    {
        await SendAsync(Req(HttpMethod.Post, $"repos/{Repo}/issues/{number}/comments", new { body }));
        return $"Kommentar zu #{number} gepostet.";
    }

    public async Task<string> IssueCloseAsync(int number)
    {
        await SendAsync(Req(HttpMethod.Patch, $"repos/{Repo}/issues/{number}", new { state = "closed" }));
        return $"Issue #{number} geschlossen.";
    }

    public async Task<string> ActionsAsync(int limit)
    {
        limit = Math.Clamp(limit, 1, 20);
        var s = await SendAsync(Req(HttpMethod.Get, $"repos/{Repo}/actions/runs?per_page={limit}"));
        var sb = new StringBuilder();
        foreach (var r in JsonDocument.Parse(s!).RootElement.GetProperty("workflow_runs").EnumerateArray())
        {
            var concl = r.GetProperty("conclusion").ValueKind == JsonValueKind.String ? r.GetProperty("conclusion").GetString() : "läuft";
            var date = r.GetProperty("created_at").GetDateTime().ToLocalTime();
            var title = r.TryGetProperty("display_title", out var dt) ? dt.GetString() : "";
            sb.AppendLine($"{r.GetProperty("name").GetString()} #{r.GetProperty("run_number").GetInt32()}: {r.GetProperty("status").GetString()} / {concl} – {date:dd.MM. HH:mm} – {title}\n  {r.GetProperty("html_url").GetString()}");
        }
        return sb.Length == 0 ? "Keine Workflow-Läufe." : sb.ToString();
    }
}
