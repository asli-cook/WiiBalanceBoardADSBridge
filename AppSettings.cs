using System.Text.Json;

namespace BoardADSBridge;

internal sealed class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BalanceBoardADS",
        "settings.json");

    public int Threshold { get; set; } = 100;
    public bool OutputEnabled { get; set; } = true;
    public bool? MouseClickEnabled { get; set; }
    public bool? TriggerEnabled { get; set; }
    public bool AButtonEnabled { get; set; }
    public bool BButtonEnabled { get; set; }
    public bool XButtonEnabled { get; set; }
    public bool YButtonEnabled { get; set; }
    public bool KeyboardNumpad1Enabled { get; set; }
    public int OutputMode { get; set; } = 2;
    public int? SettingsVersion { get; set; }
    public ulong? BluetoothAddress { get; set; }
    public ulong? BoardBluetoothAddress { get; set; }
    public int[]? CalibrationBaseline { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                if (settings is not null)
                {
                    settings.Threshold = Math.Clamp(settings.Threshold, 100, 12000);
                    if (settings.SettingsVersion is null or < 3)
                    {
                        // Earlier releases selected either mouse OR gamepad. The updated default
                        // sends both so desktop testing and game input are available together.
                        settings.OutputMode = 2;
                        settings.SettingsVersion = 3;
                        settings.Save();
                    }
                    else
                    {
                        settings.OutputMode = Math.Clamp(settings.OutputMode, 0, 2);
                    }
                    settings.MouseClickEnabled ??= settings.OutputMode is 0 or 2;
                    settings.TriggerEnabled ??= settings.OutputMode is 1 or 2;
                    if (settings.SettingsVersion is null or < 5)
                    {
                        settings.AButtonEnabled = false;
                        settings.BButtonEnabled = false;
                        settings.XButtonEnabled = false;
                        settings.YButtonEnabled = false;
                        settings.SettingsVersion = 5;
                        settings.Save();
                    }
                    if (settings.SettingsVersion is null or < 6)
                    {
                        settings.KeyboardNumpad1Enabled = false;
                        settings.SettingsVersion = 6;
                        settings.Save();
                    }
                    if (settings.SettingsVersion is null or < 7)
                    {
                        // The standalone keyboard mode is the new default: one held
                        // Balance Board press becomes Numpad 1, with no mouse or vJoy output.
                        settings.MouseClickEnabled = false;
                        settings.TriggerEnabled = false;
                        settings.AButtonEnabled = false;
                        settings.BButtonEnabled = false;
                        settings.XButtonEnabled = false;
                        settings.YButtonEnabled = false;
                        settings.KeyboardNumpad1Enabled = true;
                        settings.SettingsVersion = 7;
                        settings.Save();
                    }
                    if (settings.SettingsVersion is null or < 8)
                    {
                        // This build is keyboard-only: guarantee no mouse or gamepad
                        // outputs remain enabled from a previous configuration.
                        settings.MouseClickEnabled = false;
                        settings.TriggerEnabled = false;
                        settings.AButtonEnabled = false;
                        settings.BButtonEnabled = false;
                        settings.XButtonEnabled = false;
                        settings.YButtonEnabled = false;
                        settings.KeyboardNumpad1Enabled = true;
                        settings.SettingsVersion = 8;
                        settings.Save();
                    }
                    if (settings.SettingsVersion is null or < 9)
                    {
                        settings.MouseClickEnabled = false;
                        settings.TriggerEnabled = false;
                        settings.AButtonEnabled = false;
                        settings.BButtonEnabled = false;
                        settings.XButtonEnabled = false;
                        settings.YButtonEnabled = false;
                        settings.KeyboardNumpad1Enabled = true;
                        settings.SettingsVersion = 9;
                        settings.Save();
                    }
                    var baseline = settings.CalibrationBaseline;
                    if (baseline is null || baseline.Length != 4 ||
                        baseline.Any(value => value is < 0 or > ushort.MaxValue))
                    {
                        settings.CalibrationBaseline = null;
                    }
                    return settings;
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }

        return new AppSettings
        {
            SettingsVersion = 9,
            MouseClickEnabled = false,
            TriggerEnabled = false,
            KeyboardNumpad1Enabled = true
        };
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
