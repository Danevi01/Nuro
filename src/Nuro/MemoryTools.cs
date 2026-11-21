using System;
using System.Text.Json;
using System.IO;
using System.Threading.Tasks;

namespace Nuro;

public sealed class MemoryTools
{
    // Der Pfad wird in der Config definiert, standardmäßig D:\Local_KI\Memory
    private string _memoryPath = @"D:\Local_KI\Memory\memory_snapshot.json";

    public async Task<string> SaveMemoryAsync(string project, string status, string[] completed, string[] active, string notes)
    {
        var snapshot = new
        {
            last_update = DateTime.Now.ToString("o"),
            current_project = project,
            roadmap_status = status,
            completed_tasks = completed,
            active_tasks = active,
            context_notes = notes
        };

        string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        
        // Sicherstellen, dass der Ordner existiert
        string dir = Path.GetDirectoryName(_memoryPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        
        File.WriteAllText(_memoryPath, json);
        return "Gedächtnis erfolgreich aktualisiert.";
    }

    public async Task<string> ReadMemoryAsync()
    {
        if (!File.Exists(_memoryPath)) return "Kein Gedächtnis gefunden.";
        return File.ReadAllText(_memoryPath);
    }
}