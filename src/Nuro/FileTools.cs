using System.IO.Enumeration;
using System.Text;

namespace Nuro;

/// <summary>Dateizugriff – ausschließlich innerhalb der vom Nutzer freigegebenen Ordner.</summary>
public sealed class FileTools
{
    private readonly Config _c;
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly string[] SkipDirs = { ".git", "node_modules", ".vs", "__pycache__" };

    public FileTools(Config c) => _c = c;

    private static string Norm(string p)
    {
        var full = Path.GetFullPath(p);
        return full.Length > 3 ? full.TrimEnd('\\', '/') : full; // "C:\" bleibt "C:\"
    }

    private string[] Roots => _c.Folders.Where(Directory.Exists).Select(Norm).ToArray();

    private string Resolve(string path)
    {
        var roots = Roots;
        if (roots.Length == 0)
            throw new InvalidOperationException("Es sind keine Ordner freigegeben. Bitte den Nutzer, in den Einstellungen einen Ordner freizugeben.");
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Pfad fehlt.");

        var p = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        var full = Norm(Path.IsPathRooted(p) ? p : Path.Combine(roots[0], p));
        foreach (var r in roots)
        {
            var prefix = r.EndsWith('\\') ? r : r + "\\";
            if (full.Equals(r, StringComparison.OrdinalIgnoreCase) || full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return full;
        }
        throw new UnauthorizedAccessException($"Kein Zugriff: „{full}“ liegt außerhalb der freigegebenen Ordner ({string.Join(", ", roots)}).");
    }

    private bool IsRoot(string full) => Roots.Any(r => r.Equals(full, StringComparison.OrdinalIgnoreCase));

    private static string Size(long b) => b < 1024 ? $"{b} B" : b < 1048576 ? $"{b / 1024.0:0.#} KB" : $"{b / 1048576.0:0.#} MB";

    public string ListRoots()
    {
        var roots = Roots;
        return roots.Length == 0 ? "Keine Ordner freigegeben." : "Freigegebene Ordner:\n" + string.Join("\n", roots);
    }

    public string ListDir(string path, bool recursive)
    {
        var full = Resolve(path);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"Ordner nicht gefunden: {full}");
        var sb = new StringBuilder().AppendLine(full);
        int n = 0;
        const int max = 600;

        void Walk(string dir, int depth)
        {
            var ind = new string(' ', depth * 2);
            string[] dirs, files;
            try
            {
                dirs = Directory.GetDirectories(dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
                files = Directory.GetFiles(dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch { sb.AppendLine(ind + "(kein Zugriff)"); return; }

            foreach (var d in dirs)
            {
                if (n++ >= max) return;
                var name = Path.GetFileName(d);
                sb.AppendLine($"{ind}{name}/");
                if (recursive && depth < 8 && !SkipDirs.Contains(name, StringComparer.OrdinalIgnoreCase)) Walk(d, depth + 1);
            }
            foreach (var f in files)
            {
                if (n++ >= max) return;
                long len = 0;
                try { len = new FileInfo(f).Length; } catch { }
                sb.AppendLine($"{ind}{Path.GetFileName(f)}  ({Size(len)})");
            }
        }

        Walk(full, 0);
        if (n >= max) sb.AppendLine($"… (gekürzt nach {max} Einträgen)");
        return sb.ToString();
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        try { return new UTF8Encoding(false, true).GetString(bytes, start, bytes.Length - start); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    private static bool LooksBinary(byte[] b)
    {
        if (b.Length >= 2 && ((b[0] == 0xFF && b[1] == 0xFE) || (b[0] == 0xFE && b[1] == 0xFF))) return false;
        int n = Math.Min(b.Length, 8000);
        for (int i = 0; i < n; i++) if (b[i] == 0) return true;
        return false;
    }

    public string ReadFile(string path, int startLine, int maxLines)
    {
        var full = Resolve(path);
        if (!File.Exists(full)) throw new FileNotFoundException($"Datei nicht gefunden: {full}");
        var fi = new FileInfo(full);
        if (fi.Length > 25 * 1024 * 1024) throw new InvalidOperationException($"Datei zu groß ({Size(fi.Length)}).");
        var bytes = File.ReadAllBytes(full);
        if (LooksBinary(bytes)) throw new InvalidDataException($"„{fi.Name}“ ist eine Binärdatei ({Size(fi.Length)}) und kann nicht als Text gelesen werden.");

        var lines = Decode(bytes).Replace("\r\n", "\n").Split('\n');
        int s = Math.Max(1, startLine);
        maxLines = Math.Clamp(maxLines, 1, 5000);
        if (s > lines.Length) return $"{full}: Die Datei hat nur {lines.Length} Zeilen.";
        int e = Math.Min(lines.Length, s + maxLines - 1);
        var head = $"{full} – Zeilen {s}–{e} von {lines.Length}" + (e < lines.Length ? $" (weiterlesen mit start_line={e + 1})" : "");
        return head + "\n\n" + string.Join("\n", lines[(s - 1)..e]);
    }

    public string WriteFile(string path, string content, bool append)
    {
        var full = Resolve(path);
        if (Directory.Exists(full)) throw new InvalidOperationException($"„{full}“ ist ein Ordner.");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        bool existed = File.Exists(full);
        if (append) File.AppendAllText(full, content, Utf8NoBom);
        else File.WriteAllText(full, content, Utf8NoBom);
        return $"{(append ? "Angehängt an" : existed ? "Überschrieben" : "Neu erstellt")}: {full} ({content.Length} Zeichen)";
    }

    public string EditFile(string path, string oldText, string newText)
    {
        var full = Resolve(path);
        if (!File.Exists(full)) throw new FileNotFoundException($"Datei nicht gefunden: {full}");
        if (string.IsNullOrEmpty(oldText)) throw new ArgumentException("old_text darf nicht leer sein.");
        var bytes = File.ReadAllBytes(full);
        var text = Decode(bytes);

        int count = Count(text, oldText);
        if (count == 0 && text.Contains("\r\n"))
        {
            // Zeilenenden angleichen (Modell schreibt meist \n, Datei hat \r\n)
            oldText = oldText.Replace("\r\n", "\n").Replace("\n", "\r\n");
            newText = newText.Replace("\r\n", "\n").Replace("\n", "\r\n");
            count = Count(text, oldText);
        }
        if (count == 0) throw new InvalidOperationException("old_text kommt in der Datei nicht vor. Lies die Datei erneut und übernimm den Text exakt.");
        if (count > 1) throw new InvalidOperationException($"old_text kommt {count}-mal vor. Nimm mehr umgebenden Text dazu, damit die Stelle eindeutig ist.");

        File.WriteAllText(full, text.Replace(oldText, newText), Utf8NoBom);
        return $"Geändert: {full}";
    }

    private static int Count(string s, string sub)
    {
        int c = 0, i = 0;
        while ((i = s.IndexOf(sub, i, StringComparison.Ordinal)) >= 0) { c++; i += sub.Length; }
        return c;
    }

    public string CreateFolder(string path)
    {
        var full = Resolve(path);
        Directory.CreateDirectory(full);
        return $"Ordner vorhanden: {full}";
    }

    public string Delete(string path)
    {
        var full = Resolve(path);
        if (IsRoot(full)) throw new InvalidOperationException("Ein freigegebener Hauptordner selbst kann nicht gelöscht werden.");
        if (Directory.Exists(full)) { Directory.Delete(full, true); return $"Ordner gelöscht: {full}"; }
        if (File.Exists(full)) { File.Delete(full); return $"Datei gelöscht: {full}"; }
        throw new FileNotFoundException($"Nicht gefunden: {full}");
    }

    public string Move(string from, string to)
    {
        var src = Resolve(from);
        var dst = Resolve(to);
        if (IsRoot(src)) throw new InvalidOperationException("Ein freigegebener Hauptordner kann nicht verschoben werden.");
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        if (Directory.Exists(src)) Directory.Move(src, dst);
        else if (File.Exists(src)) File.Move(src, dst, false);
        else throw new FileNotFoundException($"Nicht gefunden: {src}");
        return $"Verschoben: {src} → {dst}";
    }

    public string Info(string path)
    {
        var full = Resolve(path);
        if (Directory.Exists(full))
        {
            var d = new DirectoryInfo(full);
            return $"Ordner: {full}\nErstellt: {d.CreationTime:g}\nGeändert: {d.LastWriteTime:g}";
        }
        if (!File.Exists(full)) throw new FileNotFoundException($"Nicht gefunden: {full}");
        var f = new FileInfo(full);
        return $"Datei: {full}\nGröße: {Size(f.Length)}\nErstellt: {f.CreationTime:g}\nGeändert: {f.LastWriteTime:g}\nSchreibgeschützt: {(f.IsReadOnly ? "ja" : "nein")}";
    }

    public string Search(string path, string namePattern, string? contains)
    {
        var full = Resolve(path);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"Ordner nicht gefunden: {full}");
        if (string.IsNullOrWhiteSpace(namePattern)) namePattern = "*";
        var sb = new StringBuilder();
        int hits = 0, scanned = 0;
        const int maxHits = 200;
        var stack = new Stack<string>();
        stack.Push(full);

        while (stack.Count > 0 && hits < maxHits && scanned < 200000)
        {
            var dir = stack.Pop();
            string[] subs, files;
            try { subs = Directory.GetDirectories(dir); files = Directory.GetFiles(dir); }
            catch { continue; }
            foreach (var s in subs)
                if (!SkipDirs.Contains(Path.GetFileName(s), StringComparer.OrdinalIgnoreCase)) stack.Push(s);

            foreach (var f in files)
            {
                scanned++;
                if (!FileSystemName.MatchesSimpleExpression(namePattern, Path.GetFileName(f))) continue;
                if (contains == null)
                {
                    sb.AppendLine(f);
                    if (++hits >= maxHits) break;
                    continue;
                }
                try
                {
                    var fi = new FileInfo(f);
                    if (fi.Length > 5 * 1024 * 1024) continue;
                    var bytes = File.ReadAllBytes(f);
                    if (LooksBinary(bytes)) continue;
                    var lines = Decode(bytes).Split('\n');
                    int inFile = 0;
                    for (int i = 0; i < lines.Length && inFile < 5; i++)
                    {
                        if (lines[i].Contains(contains, StringComparison.OrdinalIgnoreCase))
                        {
                            var l = lines[i].Trim();
                            if (l.Length > 200) l = l[..200] + "…";
                            sb.AppendLine($"{f}:{i + 1}: {l}");
                            inFile++;
                        }
                    }
                    if (inFile > 0 && ++hits >= maxHits) break;
                }
                catch { }
            }
        }
        if (hits == 0) return "Keine Treffer.";
        if (hits >= maxHits) sb.AppendLine($"… (nach {maxHits} Treffern abgebrochen)");
        return sb.ToString();
    }
}
