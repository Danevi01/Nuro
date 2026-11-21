using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Nuro;

/// <summary>
/// Prüft Pfade und Inhalte, bevor etwas ins GitHub-Repository committet wird.
/// Fehler blockieren den Commit, Warnungen werden nur gemeldet.
/// </summary>
public static class RepoValidator
{
    private static readonly Regex DrivePath = new(@"(?<![A-Za-z])[A-Za-z]:\\[^\s""'<>|*?]+", RegexOptions.Compiled);

    /// <summary>Normalisiert einen Repo-Pfad oder wirft eine verständliche Fehlermeldung.</summary>
    public static string RepoPath(string? path, bool allowEmpty = false)
    {
        var p = (path ?? "").Trim().Trim('"');
        if (Regex.IsMatch(p, @"^[A-Za-z]:") || p.StartsWith(@"\\") || p.Contains(":\\"))
            throw new ArgumentException(
                $"„{p}“ ist ein lokaler Windows-Pfad. GitHub kennt keine Dateien auf dem PC. " +
                "Repo-Pfade sind relativ zur Repository-Wurzel und nutzen „/“, z. B. „src/Nuro/Tools.cs“. " +
                "Für Dateien auf dem PC die lokalen Werkzeuge (read_file, write_file …) verwenden.");
        if (p.Contains('\\'))
            throw new ArgumentException($"„{p}“ enthält „\\“. Repo-Pfade verwenden „/“, z. B. „src/Nuro/Tools.cs“.");
        p = p.Trim('/');
        if (p.Split('/').Any(s => s == ".." || s == "."))
            throw new ArgumentException($"„{p}“: „.“ und „..“ sind in Repo-Pfaden nicht erlaubt.");
        if (!allowEmpty && p.Length == 0) throw new ArgumentException("Repo-Pfad fehlt.");
        return p;
    }

    /// <summary>Darf Nuro diese Datei ändern? Workflows sind geschützt (sie bewachen die Builds selbst).</summary>
    public static void EnsureWritable(string repoPath)
    {
        if (repoPath.StartsWith(".github/", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                "Dateien unter .github/ (Build-Workflows) darf Nuro nicht selbst ändern, weil sie die Builds absichern. " +
                "Bitte den Nutzer, solche Änderungen selbst vorzunehmen.");
    }

    public sealed record Result(List<string> Errors, List<string> Warnings)
    {
        public bool Ok => Errors.Count == 0;
    }

    public static Result Check(string repoPath, string content, IEnumerable<string> localFolders)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var ext = Path.GetExtension(repoPath).ToLowerInvariant();

        // 1) Lokale Pfade, die GitHub nicht kennen kann
        foreach (var folder in localFolders.Where(f => f.Length > 3))
            if (content.Contains(folder, StringComparison.OrdinalIgnoreCase))
                errors.Add($"Der Inhalt verweist auf den lokalen Ordner „{folder}“. Auf GitHub (und im Build) existiert dieser Pfad nicht. " +
                           "Verwende Repo-relative Pfade oder lies den Pfad zur Laufzeit aus der Konfiguration.");
        var drive = DrivePath.Matches(content).Select(m => m.Value).Distinct().Take(5).ToList();
        if (drive.Count > 0 && errors.Count == 0)
            warnings.Add("Enthält absolute Windows-Pfade (" + string.Join(", ", drive) + "). Prüfe, ob sie wirklich auf jedem PC gültig sind.");

        // 2) Syntax je Dateityp
        try
        {
            switch (ext)
            {
                case ".cs":
                {
                    var tree = CSharpSyntaxTree.ParseText(content, new CSharpParseOptions(LanguageVersion.Latest));
                    var diags = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Take(12).ToList();
                    foreach (var d in diags)
                    {
                        var line = d.Location.GetLineSpan().StartLinePosition.Line + 1;
                        errors.Add($"C#-Syntaxfehler Zeile {line}: {d.GetMessage()} ({d.Id})");
                    }
                    var root = tree.GetCompilationUnitRoot();
                    // Doppelte Eigenschaften/Felder in derselben Klasse (häufiger Fehler beim Einfügen von Code)
                    foreach (var type in root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>())
                    {
                        var names = new List<(string name, int line)>();
                        foreach (var m in type.Members)
                        {
                            int ln = m.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                            if (m is Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax pd) names.Add((pd.Identifier.Text, ln));
                            else if (m is Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax fd)
                                names.AddRange(fd.Declaration.Variables.Select(v => (v.Identifier.Text, ln)));
                        }
                        foreach (var g in names.GroupBy(n => n.name).Where(g => g.Count() > 1))
                            errors.Add($"„{g.Key}“ ist in {type.Identifier.Text} mehrfach deklariert (Zeilen {string.Join(", ", g.Select(x => x.line))}) – C# erlaubt das nicht (CS0102).");
                    }
                    var nsCount = root.DescendantNodes().Count(n => n is Microsoft.CodeAnalysis.CSharp.Syntax.BaseNamespaceDeclarationSyntax);
                    if (nsCount > 1) warnings.Add($"Die Datei enthält {nsCount} namespace-Deklarationen – ist Code doppelt eingefügt?");
                    break;
                }
                case ".json":
                    JsonDocument.Parse(content, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                    break;
                case ".csproj": case ".props": case ".targets": case ".xml": case ".resx":
                    XDocument.Parse(content);
                    break;
                case ".html":
                    if (!content.Contains("</html>", StringComparison.OrdinalIgnoreCase))
                        errors.Add("Die HTML-Datei endet nicht mit </html> – wahrscheinlich ist sie abgeschnitten.");
                    break;
            }
        }
        catch (Exception ex)
        {
            errors.Add($"{ext}-Datei ist ungültig: {ex.Message}");
        }

        // 3) Verdacht auf abgeschnittenen Inhalt
        if (Regex.IsMatch(content, @"… \(gekürzt, \d+ Zeichen mehr") || Regex.IsMatch(content, @"weiterlesen mit start_line=\d+\)"))
            errors.Add("Der Inhalt enthält einen Kürzungs-Hinweis von Nuro – die Datei wurde unvollständig gelesen. Erst vollständig lesen, dann schreiben.");

        return new Result(errors, warnings);
    }

    public static string Format(string repoPath, Result r)
    {
        var sb = new StringBuilder();
        foreach (var e in r.Errors) sb.AppendLine($"✗ {repoPath}: {e}");
        foreach (var w in r.Warnings) sb.AppendLine($"⚠ {repoPath}: {w}");
        return sb.ToString();
    }
}
