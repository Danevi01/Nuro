namespace Nuro;

/// <summary>
/// Verwaltet die Liste der Projekte und deren Persistenz.
/// </summary>
public sealed class ProjectManager
{
    private readonly Config _config;

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
        _config.Projects.Add(project);
        _config.Save();
        return project;
    }

    public void RemoveProject(string id)
    {
        var project = _config.Projects.Find(p => p.Id == id);
        if (project != null)
        {
            _config.Projects.Remove(project);
            _config.Save();
        }
    }
}
