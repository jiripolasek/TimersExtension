using System.IO;

namespace JPSoftworks.TimersExtension.Services;

internal class SettingsManager : JsonSettingsManager
{
    private const string DefaultNamespace = "jpsoftworks.timersextension";

    private readonly ToggleSetting _confirmDeletion = new(
        Namespaced("ConfirmDeletion"),
        "Ask before deleting a timer",
        "",
        false);

    public bool ConfirmDeletion => this._confirmDeletion.Value;

    public SettingsManager()
    {
        this.FilePath = SettingsJsonPath();

        this.Settings.Add(this._confirmDeletion);

        this.LoadSettings();
        this.Settings.SettingsChanged += (_, _) => this.SaveSettings();
    }

    private static string Namespaced(string propertyName)
    {
        return $"{DefaultNamespace}.{propertyName}";
    }

    private static string SettingsJsonPath()
    {
        var directory = Utilities.BaseSettingsPath("Microsoft.CmdPal");
        Directory.CreateDirectory(directory);

        // now, the state is just next to the exe
        return Path.Combine(directory, "settings.json");
    }
}