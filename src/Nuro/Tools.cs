using System.Text.Json;

namespace Nuro;

/// <summary>
/// Verteilt Werkzeug-Aufrufe des Modells. Die Beschreibungen (JSON-Schemas) stehen in wwwroot/index.html (TOOLS).
/// ask_user, chat_search und chat_read laufen direkt in der Oberfläche; run_command wird dort vorher bestätigt.
/// </summary>
public sealed class Tools
{
    public FileTools Files { get; } 
    public GitHubTools GitHub { get; } 
    public MemoryTools Memory { get; } 
    public CommandTools Commands { get; }
    public GitHubTools GitHub { get; }
    public MemoryTools Memory { get; }
    public CommandTools Commands { get; }
    public WebTools Web { get; }
    public const int MaxChars = 40000;

    public Tools(Config c)
    {
        Files = new FileTools(c);
        GitHub = new GitHubTools(c);
        Memory = new MemoryTools(c);
        Commands = new CommandTools(Files);
        Web = new WebTools();
    }

    public Task<string> RunAsync(string name, JsonElement a) => name switch
    {
        // Lokale Dateien (PC des Nutzers)
        "list_allowed_folders" => Task.FromResult(Files.ListRoots()),
        "list_dir"      => Task.FromResult(Files.ListDir(S(a, "path"), B(a, "recursive"))),
        "read_file"     => Task.FromResult(Files.ReadFile(S(a, "path"), I(a, "start_line", 1), I(a, "max_lines", 400))),
        "write_file"    => Task.FromResult(Files.WriteFile(S(a, "path"), S(a, "content", ""), B(a, "append"))),
        "edit_file"     => Task.FromResult(Files.EditFile(S(a, "path"), S(a, "old_text"), S(a, "new_text", ""))),
        "create_folder" => Task.FromResult(Files.CreateFolder(S(a, "path"))),
        "delete_path"   => Task.FromResult(Files.Delete(S(a, "path"))),
        "move_path"     => Task.FromResult(Files.Move(S(a, "from"), S(a, "to"))),
        "search_files"  => Task.FromResult(Files.Search(S(a, "path"), S(a, "name_pattern", "*"), Opt(a, "contains"))),
        "file_info"     => Task.FromResult(Files.Info(S(a, "path"))),
        "run_command"   => Commands.RunAsync(S(a, "command"), S(a, "working_directory"), I(a, "timeout_seconds", 120)),

        // GitHub (eigenes Repository)
        "gh_list"          => GitHub.ListAsync(S(a, "path", ""), B(a, "recursive")),
        "gh_read"          => GitHub.ReadAsync(S(a, "path"), I(a, "start_line", 1), I(a, "max_lines", 600)),
        "gh_branch_start"  => GitHub.StartBranchAsync(Opt(a, "name")),
        "gh_write"         => GitHub.WriteAsync(S(a, "path"), S(a, "content", ""), S(a, "message", "Änderung durch Nuro")),
        "gh_edit"          => GitHub.EditAsync(S(a, "path"), S(a, "old_text"), S(a, "new_text", ""), S(a, "message", "Änderung durch Nuro")),
        "gh_delete"        => GitHub.DeleteAsync(S(a, "path"), S(a, "message", "Datei gelöscht durch Nuro")),
        "gh_commit"        => GitHub.CommitAsync(Changes(a), S(a, "message", "Änderung durch Nuro")),
        "gh_build_wait"    => GitHub.BuildWaitAsync(I(a, "timeout_seconds", 600)),
        "gh_merge"         => GitHub.MergeAsync(Opt(a, "message")),
        "gh_branch_discard"=> GitHub.DiscardBranchAsync(),
        "gh_commits"       => GitHub.CommitsAsync(I(a, "limit", 10)),
        "gh_actions"       => GitHub.ActionsAsync(I(a, "limit", 5)),
        "gh_issues"        => GitHub.IssuesAsync(S(a, "state", "open")),
        "gh_issue_read"    => GitHub.IssueReadAsync(I(a, "number", 0)),
        "gh_issue_create"  => GitHub.IssueCreateAsync(S(a, "title"), S(a, "body", "")),
        "gh_issue_comment" => GitHub.IssueCommentAsync(I(a, "number", 0), S(a, "body")),
        "gh_issue_close"   => GitHub.IssueCloseAsync(I(a, "number", 0)),

        // Gedächtnis
        "memory_save"   => Task.FromResult(Memory.Remember(S(a, "text"), Opt(a, "category"), Opt(a, "replaces_id"))),
        "memory_search" => Task.FromResult(Memory.Search(S(a, "query", ""))),
        "memory_list"   => Task.FromResult(Memory.List()),
        "memory_delete" => Task.FromResult(Memory.Forget(S(a, "id"))),

        // Web & System
        "web_fetch"  => Web.FetchAsync(S(a, "url")),
        "gpu_status" => GpuTools.StatusTextAsync(),

        _ => throw new InvalidOperationException($"Unbekanntes Werkzeug: {name}")
    };

    private static List<GitHubTools.FileChange> Changes(JsonElement a)
    {
        var list = new List<GitHubTools.FileChange>();
        if (a.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
            foreach (var f in files.EnumerateArray())
                list.Add(new GitHubTools.FileChange(S(f, "path"), S(f, "content")));
        if (a.TryGetProperty("delete", out var del) && del.ValueKind == JsonValueKind.Array)
            foreach (var d in del.EnumerateArray())
                list.Add(new GitHubTools.FileChange(d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : d.ToString(), null));
        return list;
    }

    public static string Clip(string s) =>
        s.Length <= MaxChars ? s : s[..MaxChars] + $"\n\n… (gekürzt, {s.Length - MaxChars} Zeichen mehr – bei Dateien mit start_line weiterlesen)";

    // ---- Argument-Hilfen (Modelle schicken Zahlen/Booleans manchmal als Text) ----
    private static string S(JsonElement a, string key, string? def = null)
    {
        if (a.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null)
            return v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString();
        return def ?? throw new ArgumentException($"Parameter „{key}“ fehlt.");
    }

    private static string? Opt(JsonElement a, string key) =>
        a.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(v.GetString()) ? v.GetString() : null;

    private static int I(JsonElement a, string key, int def)
    {
        if (!a.TryGetProperty(key, out var v)) return def;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        if (v.ValueKind == JsonValueKind.Number) return (int)v.GetDouble();
        return v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var m) ? m : def;
    }

    private static bool B(JsonElement a, string key)
    {
        if (!a.TryGetProperty(key, out var v)) return false;
        return v.ValueKind == JsonValueKind.True ||
               (v.ValueKind == JsonValueKind.String && (v.GetString() ?? "").Trim().ToLowerInvariant() is "true" or "1" or "ja" or "yes");
    }
}
