namespace Nuro;

/// <summary>
/// Repräsentiert ein Projekt innerhalb von Nuro.
/// </summary>
public sealed class Project
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> AssociatedFolders { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
