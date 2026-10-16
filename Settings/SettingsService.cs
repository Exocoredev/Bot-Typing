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
            var changed = false;
            if (settings.SettingsVersion < 7)
            {
                settings.SettingsVersion = 7;
                settings.UseDomTextSource = true;
                changed = true;
            }

            // Old default (10) finished live runs while text was still visible.
            if (settings.LiveOcrEmptyScansToFinish < 15)
            {
                settings.LiveOcrEmptyScansToFinish = 25;
                changed = true;
            }

            // Ctrl+Alt+Escape is frequently eaten by hooks; migrate to reliable default.
            if (string.Equals(settings.StopHotkey, "Ctrl+Alt+Escape", StringComparison.OrdinalIgnoreCase))
            {
                settings.StopHotkey = "Ctrl+Shift+F11";
                changed = true;
            }

            if (changed)
                Save(settings);

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
        settings.SettingsVersion = 7;
        Directory.CreateDirectory(_directory);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
