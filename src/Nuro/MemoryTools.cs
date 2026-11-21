using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nuro;

public sealed class MemoryEntry
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public string Category { get; set; } = "sonstiges";
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime Updated { get; set; } = DateTime.Now;
}

/// <summary>
/// Langzeitgedächtnis: kurze, dauerhafte Fakten über den Nutzer und seine Projekte.
/// Liegt in %LOCALAPPDATA%\Nuro\memory.json (oder Config.MemoryPath). Alte Schlüssel/Wert-Dateien werden übernommen.
/// </summary>
public sealed class MemoryTools
{
    public static readonly string[] Categories = { "person", "projekt", "vorliebe", "wissen", "sonstiges" };
    private readonly string _path;
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public MemoryTools(Config c)
    {
        _path = !string.IsNullOrWhiteSpace(c.MemoryPath) ? c.MemoryPath : Path.Combine(Config.Dir, "memory.json");
    }

    public List<MemoryEntry> All()
    {
        lock (Sync) return Load();
    }

    private List<MemoryEntry> Load()
    {
        if (!File.Exists(_path)) return new();
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(_path));
            if (node is JsonArray)
                return JsonSerializer.Deserialize<List<MemoryEntry>>(node.ToJsonString(), Json) ?? new();

            // Altes Format: { "Memories": { "schlüssel": "inhalt" } }
            var list = new List<MemoryEntry>();
            if (node?["Memories"] is JsonObject old)
                foreach (var kv in old)
                    list.Add(new MemoryEntry { Id = NewId(), Text = $"{kv.Key}: {kv.Value}", Category = "sonstiges" });
            Save(list);
            return list;
        }
        catch { return new(); }
    }

    private void Save(List<MemoryEntry> list)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(list, Json));
        File.Move(tmp, _path, true);
    }

    private static string NewId() => Convert.ToHexString(Guid.NewGuid().ToByteArray())[..6].ToLowerInvariant();

    private static string Cat(string? c)
    {
        c = (c ?? "").Trim().ToLowerInvariant();
        return Categories.Contains(c) ? c : "sonstiges";
    }

    public string Remember(string text, string? category, string? replacesId)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) throw new ArgumentException("text fehlt.");
        if (text.Length > 600) text = text[..600];
        lock (Sync)
        {
            var list = Load();
            if (!string.IsNullOrWhiteSpace(replacesId))
            {
                var e = list.FirstOrDefault(x => x.Id == replacesId.Trim());
                if (e == null) throw new KeyNotFoundException($"Keine Erinnerung mit der ID {replacesId}.");
                e.Text = text;
                if (!string.IsNullOrWhiteSpace(category)) e.Category = Cat(category);
                e.Updated = DateTime.Now;
                Save(list);
                return $"Aktualisiert [{e.Id}]: {e.Text}";
            }
            var dup = list.FirstOrDefault(x => string.Equals(x.Text, text, StringComparison.OrdinalIgnoreCase));
            if (dup != null) return $"Schon bekannt [{dup.Id}]: {dup.Text}";
            var n = new MemoryEntry { Id = NewId(), Text = text, Category = Cat(category) };
            list.Add(n);
            Save(list);
            return $"Gemerkt [{n.Id}] ({n.Category}): {n.Text}";
        }
    }

    public string Forget(string id)
    {
        lock (Sync)
        {
            var list = Load();
            var n = list.RemoveAll(x => x.Id == id.Trim());
            if (n == 0) throw new KeyNotFoundException($"Keine Erinnerung mit der ID {id}.");
            Save(list);
            return $"Vergessen: [{id}]";
        }
    }

    private static string[] Tokens(string s) =>
        s.ToLowerInvariant().Split(new[] { ' ', ',', '.', ';', ':', '!', '?', '(', ')', '"', '\'', '/', '\\', '-', '_', '\n', '\t' },
            StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length > 2).ToArray();

    public string Search(string query)
    {
        var list = All();
        if (list.Count == 0) return "Das Gedächtnis ist leer.";
        var q = Tokens(query ?? "");
        var hits = list
            .Select(e =>
            {
                var t = e.Text.ToLowerInvariant();
                int score = q.Sum(w => t.Contains(w) ? 2 : w.Length > 5 && t.Contains(w[..5]) ? 1 : 0);
                if (e.Category.Equals(query?.Trim(), StringComparison.OrdinalIgnoreCase)) score += 3;
                return (e, score);
            })
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score).ThenByDescending(x => x.e.Updated)
            .Take(25).ToList();
        if (hits.Count == 0) return "Keine passenden Erinnerungen gefunden.";
        var sb = new StringBuilder();
        foreach (var (e, _) in hits) sb.AppendLine($"[{e.Id}] ({e.Category}, {e.Updated:dd.MM.yyyy}) {e.Text}");
        return sb.ToString();
    }

    public string List()
    {
        var list = All();
        if (list.Count == 0) return "Das Gedächtnis ist leer.";
        var sb = new StringBuilder();
        foreach (var g in list.GroupBy(e => e.Category).OrderBy(g => Array.IndexOf(Categories, g.Key)))
        {
            sb.AppendLine($"## {g.Key}");
            foreach (var e in g.OrderByDescending(x => x.Updated)) sb.AppendLine($"[{e.Id}] {e.Text}");
        }
        return sb.ToString();
    }
}
