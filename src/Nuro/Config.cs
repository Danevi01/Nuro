using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nuro;

/// <summary>Einstellungen des Hosts. Liegt unter %LOCALAPPDATA%\Nuro\config.json, der GitHub-Token ist per Windows-DPAPI verschlüsselt.</summary>
public sealed class Config
{
    public static string Dir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nuro");
    private static string FilePath => Path.Combine(Dir, "config.json");
    private static readonly object Sync = new();

    public List<string> Folders { get; set; } = new();
    public List<Project> Projects { get; set; } = new();
    public string Repo { get; set; } = "Danevi01/Nuro";
    public string Branch { get; set; } = "main";
    public string MemoryPath { get; set; } = "";
    public string? TokenData { get; set; }

    [JsonIgnore]
    public string? Token
    {
        get
        {
            if (string.IsNullOrEmpty(TokenData)) return null;
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(TokenData), null, DataProtectionScope.CurrentUser)); }
            catch { return null; }
        }
        set
        {
            TokenData = string.IsNullOrWhiteSpace(value) ? null
                : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
        }
    }

    public static Config Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath)) ?? new Config();
        }
        catch { }
        return new Config();
    }

    public void Save()
    {
        lock (Sync)
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
