using System.Text.Json;

namespace Aes67Vcs.Core.Models;

/// <summary>
/// JSON 파일로 Aes67Config 영속화.
/// 저장 위치: %APPDATA%\Aes67Vcs\config.json
/// </summary>
public static class ConfigStore
{
    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aes67Vcs");
    private static readonly string ConfigFile =
        Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    public static Aes67Config Load()
    {
        try
        {
            if (File.Exists(ConfigFile))
            {
                var json = File.ReadAllText(ConfigFile);
                return JsonSerializer.Deserialize<Aes67Config>(json, JsonOpts)
                       ?? new Aes67Config();
            }
        }
        catch { /* 손상된 파일 → 기본값 */ }
        return new Aes67Config();
    }

    public static void Save(Aes67Config cfg)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(cfg, JsonOpts);
        File.WriteAllText(ConfigFile, json);
    }
}
