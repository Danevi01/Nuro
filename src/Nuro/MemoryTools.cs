using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Nuro;

public sealed class MemoryTools
{
    private readonly string _memoryPath;

    public MemoryTools(Config c)
    {
        _memoryPath = !string.IsNullOrWhiteSpace(c.MemoryPath) 
            ? c.MemoryPath 
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nuro", "memory.json");
    }
    public async Task<string> SaveMemoryAsync(string key, string content)
    {
        try
        {
            if (!File.Exists(_memoryPath))
            {
                var initial = new { LastUpdate = DateTime.UtcNow.ToString("o"), Memories = new Dictionary<string, string>() };
                await File.WriteAllTextAsync(_memoryPath, JsonSerializer.Serialize(initial, new JsonSerializerOptions { WriteIndented = true }));
            }

            var json = await File.ReadAllTextAsync(_memoryPath);
            var data = JsonSerializer.Deserialize<MemoryData>(json);
            
            if (data == null) data = new MemoryData();
            data.Memories[key] = content;
            data.LastUpdate = DateTime.UtcNow.ToString("o");

            var updatedJson = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_memoryPath, updatedJson);
            
            return $"Erfolgreich gespeichert: {key}";
        }
        catch (Exception ex)
        {
            return $"Fehler beim Speichern: {ex.Message}";
        }
    }

    public async Task<string> GetMemoryAsync(string key)
    {
        try
        {
            if (!File.Exists(_memoryPath)) return "Speicherdatei nicht gefunden.";
            var json = await File.ReadAllTextAsync(_memoryPath);
            var data = JsonSerializer.Deserialize<MemoryData>(json);
            
            if (data != null && data.Memories.TryGetValue(key, out var content))
            {
                return content;
            }
            return "Schlüssel nicht gefunden.";
        }
        catch (Exception ex)
        {
            return $"Fehler beim Abrufen: {ex.Message}";
        }
    }

    public async Task<string> SearchMemoryAsync(string query)
    {
        try
        {
            if (!File.Exists(_memoryPath)) return "Speicherdatei nicht gefunden.";
            var json = await File.ReadAllTextAsync(_memoryPath);
            var data = JsonSerializer.Deserialize<MemoryData>(json);
            
            if (data == null || data.Memories == null) return "Speicher leer oder ungültig.";

            var results = data.Memories
                .Where(kvp => kvp.Key.Contains(query, StringComparison.OrdinalIgnoreCase) || 
                               kvp.Value.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(kvp => $"[{kvp.Key}]: {kvp.Value}")
                .ToList();

            if (results.Count == 0) return "Keine passenden Erinnerungen gefunden.";
            
            return "Gefundene Erinnerungen:\n" + string.Join("\n", results);
        }
        catch (Exception ex)
        {
            return $"Fehler bei der Suche: {ex.Message}";
        }
    }

    public async Task<string> ListMemoriesAsync()
    {
        try
        {
            if (!File.Exists(_memoryPath)) return "Speicherdatei nicht gefunden.";
            var json = await File.ReadAllTextAsync(_memoryPath);
            var data = JsonSerializer.Deserialize<MemoryData>(json);
            
            if (data == null) return "Speicher leer.";
            
            var keys = data.Memories.Keys.ToList();
            return "Verfügbare Schlüssel: " + string.Join(", ", keys);
        }
        catch (Exception ex)
        {
            return $"Fehler beim Auflisten: {ex.Message}";
        }
    }

    private class MemoryData
    {
        public string LastUpdate { get; set; }
        public Dictionary<string, string> Memories { get; set; } = new();
    }
}