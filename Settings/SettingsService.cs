using System.IO;
using System.Text.Json;
using AutoInputTester.Models;

namespace AutoInputTester.Settings;

public sealed class SettingsService
{
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoInputTester");

    private string FilePath => Path.Combine(_directory, "settings.json");
    private string BackupPath => Path.Combine(_directory, "settings.json.bak");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();

            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

            // Non-destructive migration: keep all user values already present in JSON.
            // Missing V6 properties receive AppSettings defaults via the deserializer.
            if (settings.SettingsVersion < 6)
            {
                settings.SettingsVersion = 6;
                Save(settings);
            }

            return settings;
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(FilePath))
                    File.Copy(FilePath, BackupPath, overwrite: true);
            }
            catch { }

            System.Diagnostics.Debug.WriteLine("Settings load failed: " + ex.Message);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        settings.SettingsVersion = 6;
        Directory.CreateDirectory(_directory);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
