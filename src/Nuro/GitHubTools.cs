using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nuro;

/// <summary>
/// Zugriff auf das eigene GitHub-Repository über die REST-API.
/// Änderungen landen nie direkt auf dem Haupt-Branch: Nuro arbeitet auf einem Arbeits-Branch (nuro/…),
/// wartet den Build ab und darf erst nach einem erfolgreichen Build mergen.
/// </summary>
public sealed class GitHubTools
{
    private readonly Config _c;
    private static readonly HttpClient Http = CreateClient();

    /// <summary>Aktueller Arbeits-Branch dieser Sitzung (null = noch keiner).</summary>
    public string? WorkBranch { get; private set; }

    public GitHubTools(Config c) => _c = c;

    private static HttpClient CreateClient()
    {
        var h = new HttpClient { BaseAddress = new Uri("https://api.github.com/"), Timeout = TimeSpan.FromSeconds(90) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Nuro/" + LocalServer.Version);
        h.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return h;
    }

    private string Repo => _c.Repo.Trim().Trim('/');
    private string Main => _c.Branch;
    private string ReadBranch => WorkBranch ?? Main;
    private static string Esc(string s) => Uri.EscapeDataString(s);

    private static string P(string path) =>
        string.Join("/", path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

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

    private static async Task<(HttpStatusCode code, string body)> RawAsync(HttpRequestMessage r)
    {
        using (r)
        using (var res = await Http.SendAsync(r))
            return (res.StatusCode, await res.Content.ReadAsStringAsync());
    }

    private static async Task<string?> SendAsync(HttpRequestMessage r, bool allow404 = false)
    {
        var (code, s) = await RawAsync(r);
        if (allow404 && code == HttpStatusCode.NotFound) return null;
        if ((int)code >= 300)
        {
            string msg = s;
            try { msg = JsonDocument.Parse(s).RootElement.GetProperty("message").GetString() ?? s; } catch { }
            var hint = code switch
            {
                HttpStatusCode.Unauthorized => " (Token ungültig oder abgelaufen)",
                HttpStatusCode.Forbidden => " (Token hat nicht die nötigen Rechte)",
                HttpStatusCode.NotFound => " (nicht gefunden – oder der Token hat keinen Zugriff)",
                HttpStatusCode.Conflict => " (Konflikt mit dem aktuellen Stand)",
                _ => ""
            };
            throw new HttpRequestException($"GitHub {(int)code}: {msg}{hint}");
        }
        return s;
    }

    private async Task<JsonElement> GetJsonAsync(string url) =>
        JsonDocument.Parse((await SendAsync(Req(HttpMethod.Get, url)))!).RootElement;

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
            var root = await GetJsonAsync($"repos/{Repo}");
            bool push = root.TryGetProperty("permissions", out var p) && p.TryGetProperty("push", out var pp) && pp.GetBoolean();
            return new { ok = true, name = root.GetProperty("full_name").GetString(), isPrivate = root.GetProperty("private").GetBoolean(), push };
        }
        catch (Exception ex) { return new { ok = false, error = ex.Message }; }
    }

    // ------------------------------------------------------------------ Lesen

    public async Task<string> ListAsync(string path, bool recursive)
    {
        path = RepoValidator.RepoPath(path, allowEmpty: true);
        var sb = new StringBuilder();
        if (recursive)
        {
            var root = await GetJsonAsync($"repos/{Repo}/git/trees/{Esc(ReadBranch)}?recursive=1");
            int n = 0;
            foreach (var e in root.GetProperty("tree").EnumerateArray())
            {
                var p = e.GetProperty("path").GetString()!;
                if (path.Length > 0 && !p.StartsWith(path + "/", StringComparison.Ordinal) && p != path) continue;
                var isDir = e.GetProperty("type").GetString() == "tree";
                sb.AppendLine(isDir ? p + "/" : $"{p}  ({(e.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0)} B)");
                if (++n >= 1500) { sb.AppendLine("… (Liste gekürzt)"); break; }
            }
            return $"GitHub {Repo} @ {ReadBranch}\n" + (sb.Length == 0 ? "(leer)" : sb.ToString());
        }
        var el = await GetJsonAsync($"repos/{Repo}/contents/{P(path)}?ref={Esc(ReadBranch)}");
        if (el.ValueKind != JsonValueKind.Array) return $"„{path}“ ist eine Datei – zum Lesen gh_read verwenden.";
        foreach (var e in el.EnumerateArray())
        {
            var isDir = e.GetProperty("type").GetString() == "dir";
            sb.AppendLine(isDir ? e.GetProperty("path").GetString() + "/" : $"{e.GetProperty("path").GetString()}  ({e.GetProperty("size").GetInt64()} B)");
        }
        return $"GitHub {Repo} @ {ReadBranch}\n" + (sb.Length == 0 ? "(leer)" : sb.ToString());
    }

    private async Task<string?> ReadRawAsync(string path, string branch)
    {
        return await SendAsync(Req(HttpMethod.Get, $"repos/{Repo}/contents/{P(path)}?ref={Esc(branch)}", accept: "application/vnd.github.raw+json"), allow404: true);
    }

    public async Task<string> ReadAsync(string path, int startLine, int maxLines)
    {
        path = RepoValidator.RepoPath(path);
        var text = await ReadRawAsync(path, ReadBranch) ?? throw new FileNotFoundException($"„{path}“ gibt es im Repo (Branch {ReadBranch}) nicht.");
        var lines = text.Replace("\r\n", "\n").Split('\n');
        int s = Math.Max(1, startLine);
        maxLines = Math.Clamp(maxLines, 1, 1500);
        if (s > lines.Length) return $"GitHub {path}: Die Datei hat nur {lines.Length} Zeilen.";
        int e = Math.Min(lines.Length, s + maxLines - 1);
        var head = $"GitHub {path} @ {ReadBranch} – Zeilen {s}–{e} von {lines.Length}" + (e < lines.Length ? $" (weiterlesen mit start_line={e + 1})" : "");
        return head + "\n\n" + string.Join("\n", lines[(s - 1)..e]);
    }

    // ------------------------------------------------------------------ Branch

    private async Task<string?> HeadShaAsync(string branch)
    {
        var s = await SendAsync(Req(HttpMethod.Get, $"repos/{Repo}/git/ref/heads/{Esc(branch)}"), allow404: true);
        return s == null ? null : JsonDocument.Parse(s).RootElement.GetProperty("object").GetProperty("sha").GetString();
    }

    private static string Slug(string s)
    {
        s = Regex.Replace(s.ToLowerInvariant().Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss"), "[^a-z0-9]+", "-").Trim('-');
        return s.Length > 40 ? s[..40].Trim('-') : s;
    }

    public async Task<string> StartBranchAsync(string? name)
    {
        var mainSha = await HeadShaAsync(Main) ?? throw new InvalidOperationException($"Branch {Main} nicht gefunden.");
        var branch = "nuro/" + (string.IsNullOrWhiteSpace(name) ? DateTime.Now.ToString("yyyyMMdd-HHmmss") : Slug(name) + "-" + DateTime.Now.ToString("HHmmss"));
        await SendAsync(Req(HttpMethod.Post, $"repos/{Repo}/git/refs", new { @ref = "refs/heads/" + branch, sha = mainSha }));
        WorkBranch = branch;
        return $"Arbeits-Branch „{branch}“ angelegt (Stand von {Main}, {mainSha[..7]}). Alle Änderungen gehen jetzt dorthin.";
    }

    private async Task<string> EnsureBranchAsync()
    {
        if (WorkBranch != null && await HeadShaAsync(WorkBranch) != null) return WorkBranch;
        await StartBranchAsync(null);
        return WorkBranch!;
    }

    public async Task<string> DiscardBranchAsync()
    {
        if (WorkBranch == null) return "Es gibt keinen Arbeits-Branch.";
        var b = WorkBranch;
        await SendAsync(Req(HttpMethod.Delete, $"repos/{Repo}/git/refs/heads/{Esc(b)}"), allow404: true);
        WorkBranch = null;
        return $"Arbeits-Branch „{b}“ verworfen. {Main} ist unverändert.";
    }

    // ------------------------------------------------------------------ Schreiben

    public sealed record FileChange(string Path, string? Content); // Content == null → löschen

    /// <summary>Prüft und committet mehrere Dateien atomar (ein Commit) auf den Arbeits-Branch.</summary>
    public async Task<string> CommitAsync(IReadOnlyList<FileChange> changes, string message)
    {
        if (changes.Count == 0) throw new ArgumentException("Keine Dateien angegeben.");
        if (string.IsNullOrWhiteSpace(message)) message = "Änderung durch Nuro";

        // Prüfen, bevor irgendetwas auf GitHub passiert
        var report = new StringBuilder();
        bool blocked = false;
        var normalized = new List<FileChange>();
        foreach (var ch in changes)
        {
            var path = RepoValidator.RepoPath(ch.Path);
            RepoValidator.EnsureWritable(path);
            if (ch.Content != null)
            {
                var r = RepoValidator.Check(path, ch.Content, _c.Folders);
                report.Append(RepoValidator.Format(path, r));
                blocked |= !r.Ok;
            }
            normalized.Add(ch with { Path = path });
        }
        if (blocked)
            return "COMMIT ABGELEHNT – nichts wurde hochgeladen. Behebe diese Fehler und versuche es erneut:\n" + report;

        var branch = await EnsureBranchAsync();
        var headSha = (await HeadShaAsync(branch))!;
        var baseTree = (await GetJsonAsync($"repos/{Repo}/git/commits/{headSha}")).GetProperty("tree").GetProperty("sha").GetString()!;

        var entries = new List<object>();
        foreach (var ch in normalized)
        {
            if (ch.Content == null)
            {
                if (await ReadRawAsync(ch.Path, branch) == null) throw new FileNotFoundException($"„{ch.Path}“ gibt es im Repo nicht.");
                entries.Add(new Dictionary<string, object?> { ["path"] = ch.Path, ["mode"] = "100644", ["type"] = "blob", ["sha"] = null });
            }
            else
            {
                var blob = JsonDocument.Parse((await SendAsync(Req(HttpMethod.Post, $"repos/{Repo}/git/blobs", new { content = ch.Content, encoding = "utf-8" })))!).RootElement;
                entries.Add(new { path = ch.Path, mode = "100644", type = "blob", sha = blob.GetProperty("sha").GetString() });
            }
        }

        var tree = JsonDocument.Parse((await SendAsync(Req(HttpMethod.Post, $"repos/{Repo}/git/trees", new { base_tree = baseTree, tree = entries })))!).RootElement;
        var commit = JsonDocument.Parse((await SendAsync(Req(HttpMethod.Post, $"repos/{Repo}/git/commits",
            new { message, tree = tree.GetProperty("sha").GetString(), parents = new[] { headSha } })))!).RootElement;
        var sha = commit.GetProperty("sha").GetString()!;
        await SendAsync(Req(HttpMethod.Patch, $"repos/{Repo}/git/refs/heads/{Esc(branch)}", new { sha, force = false }));

        var files = string.Join(", ", normalized.Select(c => (c.Content == null ? "−" : "") + c.Path));
        return $"Commit {sha[..7]} auf Arbeits-Branch „{branch}“: {message}\nDateien: {files}\n" +
               (report.Length > 0 ? report.ToString() : "") +
               "Der Build für diesen Commit startet jetzt. NÄCHSTER SCHRITT: gh_build_wait aufrufen und das Ergebnis prüfen.";
    }

    public Task<string> WriteAsync(string path, string content, string message) =>
        CommitAsync(new[] { new FileChange(path, content) }, message);

    public async Task<string> EditAsync(string path, string oldText, string newText, string message)
    {
        path = RepoValidator.RepoPath(path);
        if (string.IsNullOrEmpty(oldText)) throw new ArgumentException("old_text darf nicht leer sein.");
        var branch = WorkBranch != null && await HeadShaAsync(WorkBranch) != null ? WorkBranch : Main;
        var text = await ReadRawAsync(path, branch) ?? throw new FileNotFoundException($"„{path}“ gibt es im Repo nicht.");
        int count = 0, i = 0;
        while ((i = text.IndexOf(oldText, i, StringComparison.Ordinal)) >= 0) { count++; i += oldText.Length; }
        if (count == 0 && text.Contains("\r\n"))
        {
            oldText = oldText.Replace("\r\n", "\n").Replace("\n", "\r\n");
            newText = newText.Replace("\r\n", "\n").Replace("\n", "\r\n");
            i = 0;
            while ((i = text.IndexOf(oldText, i, StringComparison.Ordinal)) >= 0) { count++; i += oldText.Length; }
        }
        if (count == 0) throw new InvalidOperationException("old_text kommt in der Datei nicht vor. Lies die Stelle mit gh_read und übernimm den Text exakt.");
        if (count > 1) throw new InvalidOperationException($"old_text kommt {count}-mal vor. Nimm mehr umgebenden Text dazu.");
        return await CommitAsync(new[] { new FileChange(path, text.Replace(oldText, newText)) }, message);
    }

    public Task<string> DeleteAsync(string path, string message) =>
        CommitAsync(new[] { new FileChange(path, null) }, message);

    /// <summary>Datei-Inhalt (für die Vorab-Prüfung der Oberfläche) nach Anwendung einer Ersetzung.</summary>
    public async Task<string?> PreviewEditAsync(string path, string oldText, string newText)
    {
        path = RepoValidator.RepoPath(path);
        var text = await ReadRawAsync(path, ReadBranch);
        if (text == null || string.IsNullOrEmpty(oldText)) return null;
        if (!text.Contains(oldText, StringComparison.Ordinal))
        {
            oldText = oldText.Replace("\r\n", "\n").Replace("\n", "\r\n");
            newText = newText.Replace("\r\n", "\n").Replace("\n", "\r\n");
        }
        return text.Replace(oldText, newText);
    }

    // ------------------------------------------------------------------ Builds

    private async Task<JsonElement?> RunForShaAsync(string sha)
    {
        var root = await GetJsonAsync($"repos/{Repo}/actions/runs?head_sha={sha}&per_page=5");
        foreach (var r in root.GetProperty("workflow_runs").EnumerateArray()) return r;
        return null;
    }

    /// <summary>Wartet, bis der Build für den neuesten Commit (Arbeits-Branch oder Haupt-Branch) fertig ist.</summary>
    public async Task<string> BuildWaitAsync(int timeoutSeconds, CancellationToken ct = default)
    {
        timeoutSeconds = Math.Clamp(timeoutSeconds, 30, 1200);
        var branch = WorkBranch ?? Main;
        var sha = await HeadShaAsync(branch) ?? throw new InvalidOperationException($"Branch {branch} nicht gefunden.");
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        JsonElement? run = null;

        while (DateTime.UtcNow < deadline)
        {
            run = await RunForShaAsync(sha);
            if (run is { } r && r.GetProperty("status").GetString() == "completed") break;
            await Task.Delay(TimeSpan.FromSeconds(8), ct);
        }

        if (run == null)
            return $"Für Commit {sha[..7]} auf {branch} wurde kein Build gestartet (nach {timeoutSeconds} s). Prüfe mit gh_actions, ob der Workflow läuft.";
        var rr = run.Value;
        var status = rr.GetProperty("status").GetString();
        var url = rr.GetProperty("html_url").GetString();
        if (status != "completed")
            return $"Build für {sha[..7]} läuft noch ({status}) – nach {timeoutSeconds} s abgebrochen. Später erneut gh_build_wait aufrufen.\n{url}";

        var conclusion = rr.GetProperty("conclusion").GetString();
        if (conclusion == "success")
            return WorkBranch != null
                ? $"✓ BUILD ERFOLGREICH für {sha[..7]} auf „{branch}“.\n{url}\nNÄCHSTER SCHRITT: gh_merge, um die Änderung in {Main} zu übernehmen (danach baut {Main} das Release)."
                : $"✓ BUILD ERFOLGREICH für {sha[..7]} auf {Main}. Das Release mit der neuen Nuro.exe wird veröffentlicht.\n{url}";

        return $"✗ BUILD FEHLGESCHLAGEN ({conclusion}) für {sha[..7]} auf „{branch}“.\n{url}\n\n{await FailureDetailsAsync(rr.GetProperty("id").GetInt64())}\n" +
               "NÄCHSTER SCHRITT: Fehler beheben (gh_read → gh_edit/gh_commit auf denselben Branch), dann erneut gh_build_wait. NICHT mergen.";
    }

    private async Task<string> FailureDetailsAsync(long runId)
    {
        var sb = new StringBuilder();
        try
        {
            var jobs = await GetJsonAsync($"repos/{Repo}/actions/runs/{runId}/jobs");
            foreach (var job in jobs.GetProperty("jobs").EnumerateArray())
            {
                if (job.GetProperty("conclusion").GetString() is "success" or "skipped") continue;
                var failedSteps = job.GetProperty("steps").EnumerateArray()
                    .Where(s => s.GetProperty("conclusion").GetString() == "failure")
                    .Select(s => s.GetProperty("name").GetString()).ToList();
                sb.AppendLine($"Job „{job.GetProperty("name").GetString()}“ – fehlgeschlagener Schritt: {string.Join(", ", failedSteps)}");

                var log = await SendAsync(Req(HttpMethod.Get, $"repos/{Repo}/actions/jobs/{job.GetProperty("id").GetInt64()}/logs", accept: "*/*")) ?? "";
                var lines = log.Split('\n').Select(l => Regex.Replace(l, @"^\d{4}-\d\d-\d\dT[\d:.]+Z\s?", "").TrimEnd()).ToArray();
                var errors = lines.Where(l => Regex.IsMatch(l, @"(error|Fehler|##\[error\]|exception)", RegexOptions.IgnoreCase))
                                  .Distinct().Take(40).ToList();
                if (errors.Count > 0) sb.AppendLine("Fehlermeldungen:").AppendLine(string.Join("\n", errors));
                sb.AppendLine("Letzte Log-Zeilen:").AppendLine(string.Join("\n", lines.Where(l => l.Length > 0).TakeLast(25)));
            }
        }
        catch (Exception ex) { sb.AppendLine("(Log konnte nicht geladen werden: " + ex.Message + ")"); }
        var s = sb.ToString();
        return s.Length > 12000 ? s[..12000] + "\n…" : s;
    }

    /// <summary>Übernimmt den Arbeits-Branch in den Haupt-Branch – nur wenn der letzte Build erfolgreich war.</summary>
    public async Task<string> MergeAsync(string? message)
    {
        if (WorkBranch == null) return "Es gibt keinen Arbeits-Branch zum Mergen.";
        var sha = await HeadShaAsync(WorkBranch) ?? throw new InvalidOperationException($"Branch {WorkBranch} gibt es nicht mehr.");
        var run = await RunForShaAsync(sha);
        if (run == null || run.Value.GetProperty("status").GetString() != "completed")
            return $"MERGE ABGELEHNT: Der Build für {sha[..7]} ist noch nicht fertig. Erst gh_build_wait aufrufen.";
        if (run.Value.GetProperty("conclusion").GetString() != "success")
            return $"MERGE ABGELEHNT: Der Build für {sha[..7]} ist fehlgeschlagen. Erst den Fehler beheben, bis gh_build_wait ✓ meldet.";

        var (code, body) = await RawAsync(Req(HttpMethod.Post, $"repos/{Repo}/merges",
            new { @base = Main, head = WorkBranch, commit_message = string.IsNullOrWhiteSpace(message) ? $"Merge {WorkBranch}" : message }));
        if (code == HttpStatusCode.Conflict)
            return $"MERGE-KONFLIKT: {Main} hat sich inzwischen geändert und kollidiert mit „{WorkBranch}“. Den Nutzer informieren.";
        if (code == HttpStatusCode.NoContent)
        {
            await DiscardBranchAsync();
            return $"Nichts zu mergen – {Main} enthält die Änderungen bereits.";
        }
        if ((int)code >= 300) throw new HttpRequestException($"GitHub {(int)code}: {body}");

        var merged = JsonDocument.Parse(body).RootElement.GetProperty("sha").GetString()!;
        var b = WorkBranch;
        await SendAsync(Req(HttpMethod.Delete, $"repos/{Repo}/git/refs/heads/{Esc(b)}"), allow404: true);
        WorkBranch = null;
        return $"✓ „{b}“ in {Main} übernommen (Merge-Commit {merged[..7]}). Arbeits-Branch gelöscht.\n" +
               $"Der Release-Build auf {Main} startet jetzt. NÄCHSTER SCHRITT: gh_build_wait, um zu bestätigen, dass die neue Nuro.exe veröffentlicht wurde.";
    }

    // ------------------------------------------------------------------ Sonstiges

    public async Task<string> CommitsAsync(int limit)
    {
        limit = Math.Clamp(limit, 1, 50);
        var sb = new StringBuilder($"Commits auf {ReadBranch}:\n");
        foreach (var e in (await GetJsonAsync($"repos/{Repo}/commits?sha={Esc(ReadBranch)}&per_page={limit}")).EnumerateArray())
        {
            var c = e.GetProperty("commit");
            var msg = (c.GetProperty("message").GetString() ?? "").Split('\n')[0];
            var date = c.GetProperty("author").GetProperty("date").GetDateTime().ToLocalTime();
            sb.AppendLine($"{e.GetProperty("sha").GetString()![..7]}  {date:dd.MM.yyyy HH:mm}  {msg}");
        }
        return sb.ToString();
    }

    public async Task<string> IssuesAsync(string state)
    {
        if (state is not ("open" or "closed" or "all")) state = "open";
        var sb = new StringBuilder();
        foreach (var e in (await GetJsonAsync($"repos/{Repo}/issues?state={state}&per_page=50")).EnumerateArray())
        {
            if (e.TryGetProperty("pull_request", out _)) continue;
            var labels = string.Join(", ", e.GetProperty("labels").EnumerateArray().Select(l => l.GetProperty("name").GetString()));
            sb.AppendLine($"#{e.GetProperty("number").GetInt32()} [{e.GetProperty("state").GetString()}] {e.GetProperty("title").GetString()}{(labels.Length > 0 ? $" ({labels})" : "")}");
        }
        return sb.Length == 0 ? "Keine Issues." : sb.ToString();
    }

    public async Task<string> IssueReadAsync(int number)
    {
        var e = await GetJsonAsync($"repos/{Repo}/issues/{number}");
        var sb = new StringBuilder();
        sb.AppendLine($"#{number} [{e.GetProperty("state").GetString()}] {e.GetProperty("title").GetString()}");
        sb.AppendLine(e.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : "(kein Text)");
        foreach (var c in (await GetJsonAsync($"repos/{Repo}/issues/{number}/comments?per_page=50")).EnumerateArray())
            sb.AppendLine($"\n--- {c.GetProperty("user").GetProperty("login").GetString()}:\n{c.GetProperty("body").GetString()}");
        return sb.ToString();
    }

    public async Task<string> IssueCreateAsync(string title, string body)
    {
        var e = JsonDocument.Parse((await SendAsync(Req(HttpMethod.Post, $"repos/{Repo}/issues", new { title, body })))!).RootElement;
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
        var sb = new StringBuilder();
        foreach (var r in (await GetJsonAsync($"repos/{Repo}/actions/runs?per_page={limit}")).GetProperty("workflow_runs").EnumerateArray())
        {
            var concl = r.GetProperty("conclusion").ValueKind == JsonValueKind.String ? r.GetProperty("conclusion").GetString() : "läuft";
            var date = r.GetProperty("created_at").GetDateTime().ToLocalTime();
            var title = r.TryGetProperty("display_title", out var dt) ? dt.GetString() : "";
            sb.AppendLine($"#{r.GetProperty("run_number").GetInt32()} [{r.GetProperty("head_branch").GetString()}] {r.GetProperty("status").GetString()} / {concl} – {date:dd.MM. HH:mm} – {title}\n  {r.GetProperty("html_url").GetString()}");
        }
        var state = WorkBranch != null ? $"Aktiver Arbeits-Branch: {WorkBranch}\n" : "Kein Arbeits-Branch aktiv.\n";
        return state + (sb.Length == 0 ? "Keine Workflow-Läufe." : sb.ToString());
    }
}
