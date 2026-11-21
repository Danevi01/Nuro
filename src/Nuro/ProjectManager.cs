using System.Text;

namespace Nuro;

/// <summary>
/// Verwaltet die Liste der Projekte und deren Persistenz (in config.json).
/// </summary>
public sealed class ProjectManager
{
    private readonly Config _config;
    private static readonly object Sync = new();

    public ProjectManager(Config config)
    {
        _config = config;
    }

    public List<Project> GetProjects() => _config.Projects;

    public Project AddProject(string name, string description, List<string> folders)
    {
        var project = new Project
        {
            Name = name,
            Description = description,
            AssociatedFolders = folders
        };
        lock (Sync)
        {
            _config.Projects.Add(project);
            _config.Save();
        }
        return project;
    }

    public bool RemoveProject(string id)
    {
        lock (Sync)
        {
            var project = _config.Projects.Find(p => p.Id == id);
            if (project == null) return false;
            _config.Projects.Remove(project);
            _config.Save();
            return true;
        }
    }

    // ---- Texte für die Werkzeuge ----

    public string Describe()
    {
        if (_config.Projects.Count == 0) return "Es sind noch keine Projekte angelegt.";
        var sb = new StringBuilder();
        foreach (var p in _config.Projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"[{p.Id[..8]}] {p.Name} (seit {p.CreatedAt:dd.MM.yyyy})");
            if (p.Description.Length > 0) sb.AppendLine("  " + p.Description);
            foreach (var f in p.AssociatedFolders) sb.AppendLine("  Ordner: " + f);
        }
        return sb.ToString();
    }

    public string Add(string name, string description, List<string> folders)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("name fehlt.");
        if (_config.Projects.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Ein Projekt „{name}“ gibt es schon.");
        var p = AddProject(name, (description ?? "").Trim(), folders);
        return $"Projekt angelegt [{p.Id[..8]}]: {p.Name}";
    }

    public string Remove(string id)
    {
        id = (id ?? "").Trim().Trim('[', ']');
        var p = _config.Projects.FirstOrDefault(x => x.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase) && id.Length >= 4);
        if (p == null || !RemoveProject(p.Id)) throw new KeyNotFoundException($"Kein Projekt mit der ID {id}.");
        return $"Projekt entfernt: {p.Name}";
    }
}
