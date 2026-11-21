using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Nuro;

public sealed class MemoryTools
{
    private readonly string _memoryPath = @"D:\Local_KI\Memory\memory.json";

    public async Task<string> SaveMemoryAsync(string key, string content)
    {
        try
        {
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
            var json = await File.ReadAllTextAsync(_memoryPath);
            var data = JsonSerializer.Deserialize<MemoryData>(json);
            
            if (data != null && data.Memories.ContainsKey(key))
            {
                return data.Memories[key];
            }
            return "Schlüssel nicht gefunden.";
        }
        catch (Exception ex)
        {
            return $"Fehler beim Abrufen: {ex.Message}";
        }
    }

    public async Task<string> ListMemoriesAsync()
    {
        try
        {
            var json = await File.ReadAllTextAsync(_memoryPath);
            var data = JsonSerializer.Deserialize<MemoryData>(json);
            
            if (data == null) return "Speicher leer.";
            
            var keys = new List<string>();
            foreach (var key in data.Memories.Keys) keys.Add(key);
            
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