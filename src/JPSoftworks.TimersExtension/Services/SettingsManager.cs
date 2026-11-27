// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using System.IO;
using System.Linq;
using JPSoftworks.TimersExtension.Resources;

namespace JPSoftworks.TimersExtension.Services;

internal sealed class SettingsManager : JsonSettingsManager
{
    public event EventHandler? AlarmPreviewRequested;
    public event EventHandler? TimerPresentationChanged;
    private const string DefaultNamespace = "jpsoftworks.timersextension";

    private readonly ChoiceSetSetting _alarmSound = new(
        Namespaced(nameof(AlarmSoundId)),
        Strings.Setting_AlarmSound_Title,
        Strings.Setting_AlarmSound_Description,
        AlarmSoundCatalog.All
            .Select(sound => new ChoiceSetSetting.Choice(sound.DisplayName, sound.Id))
            .ToList());

    private readonly ChoiceSetSetting _alarmVolume = new(
        Namespaced(nameof(AlarmVolume)),
        Strings.Setting_AlarmVolume_Title,
        Strings.Setting_AlarmVolume_Description,
        [
            new ChoiceSetSetting.Choice(Strings.Setting_AlarmVolume_Full, "1"),
            new ChoiceSetSetting.Choice(Strings.Setting_AlarmVolume_High, "0.75"),
            new ChoiceSetSetting.Choice(Strings.Setting_AlarmVolume_Medium, "0.5"),
            new ChoiceSetSetting.Choice(Strings.Setting_AlarmVolume_Low, "0.25")
        ]);

    private readonly ChoiceSetSetting _timerSeconds = new(
        Namespaced(nameof(TimerSecondsDisplay)),
        Strings.Setting_TimerSeconds_Title,
        Strings.Setting_TimerSeconds_Description,
        [
            new ChoiceSetSetting.Choice(Strings.Setting_TimerSeconds_Always, "always"),
            new ChoiceSetSetting.Choice(Strings.Setting_TimerSeconds_UnderOneMinute, "under-1-minute"),
            new ChoiceSetSetting.Choice(Strings.Setting_TimerSeconds_UnderFiveMinutes, "under-5-minutes"),
            new ChoiceSetSetting.Choice(Strings.Setting_TimerSeconds_UnderTenMinutes, "under-10-minutes"),
            new ChoiceSetSetting.Choice(Strings.Setting_TimerSeconds_Never, "never")
        ]);

    private readonly ToggleSetting _animatedTimerIcons = new(
        Namespaced(nameof(AnimatedTimerIcons)),
        Strings.Setting_AnimatedTimerIcons_Title,
        Strings.Setting_AnimatedTimerIcons_Description,
        false);

    private readonly ToggleSetting _confirmDeletion = new(
        Namespaced(nameof(ConfirmDeletion)),
        Strings.Setting_ConfirmDeletion_Title,
        Strings.Setting_ConfirmDeletion_Description,
        false);

    private readonly ToggleSetting _confirmClearAll = new(
        Namespaced(nameof(ConfirmClearAll)),
        Strings.Setting_ConfirmClearAll_Title,
        Strings.Setting_ConfirmClearAll_Description,
        true);

    private string _lastAlarmSoundId = AlarmSoundCatalog.DefaultId;
    private double _lastAlarmVolume = 1.0;
    private string _lastTimerSecondsDisplay = "always";
    private bool _lastAnimatedTimerIcons;

    public string AlarmSoundId => AlarmSoundCatalog.Get(this._alarmSound.Value).Id;

    public string? AlarmSoundUri => AlarmSoundCatalog.Get(this._alarmSound.Value).Uri;

    public bool IsAlarmSilent => AlarmSoundCatalog.Get(this._alarmSound.Value).IsSilent;

    public double AlarmVolume => double.TryParse(this._alarmVolume.Value,
        NumberStyles.Float,
        CultureInfo.InvariantCulture,
        out var volume)
        ? Math.Clamp(volume, 0.0, 1.0)
        : 1.0;

    public string TimerSecondsDisplay =>
        this._timerSeconds.Value switch
        {
            "under-1-minute" or "under-5-minutes" or "under-10-minutes" or "never" => this._timerSeconds.Value,
            _ => "always"
        };

    public bool AnimatedTimerIcons => this._animatedTimerIcons.Value;

    public bool ConfirmDeletion => this._confirmDeletion.Value;

    public bool ConfirmClearAll => this._confirmClearAll.Value;

    public SettingsManager()
    {
        this.FilePath = SettingsJsonPath();

        this.Settings.Add(this._alarmSound);
        this.Settings.Add(this._alarmVolume);
        this.Settings.Add(this._timerSeconds);
        this.Settings.Add(this._animatedTimerIcons);
        this.Settings.Add(this._confirmDeletion);
        this.Settings.Add(this._confirmClearAll);

        this.LoadSettings();
        this._alarmSound.Value = this.AlarmSoundId;
        this._alarmVolume.Value = this.AlarmVolume.ToString("0.##", CultureInfo.InvariantCulture);
        this._timerSeconds.Value = this.TimerSecondsDisplay;
        this._lastAlarmSoundId = this.AlarmSoundId;
        this._lastAlarmVolume = this.AlarmVolume;
        this._lastTimerSecondsDisplay = this.TimerSecondsDisplay;
        this._lastAnimatedTimerIcons = this.AnimatedTimerIcons;
        this.Settings.SettingsChanged += this.Settings_SettingsChanged;
    }

    public bool ShouldShowTimerSeconds(TimeSpan remaining)
    {
        return ShouldShowTimerSeconds(this.TimerSecondsDisplay, remaining);
    }

    internal static bool ShouldShowTimerSeconds(string displayMode, TimeSpan remaining)
    {
        return displayMode switch
        {
            "under-1-minute" => remaining < TimeSpan.FromMinutes(1),
            "under-5-minutes" => remaining < TimeSpan.FromMinutes(5),
            "under-10-minutes" => remaining < TimeSpan.FromMinutes(10),
            "never" => false,
            _ => true
        };
    }

    private void Settings_SettingsChanged(object sender, Settings args)
    {
        var alarmSoundChanged = !string.Equals(this._lastAlarmSoundId, this.AlarmSoundId, StringComparison.Ordinal)
                                || Math.Abs(this._lastAlarmVolume - this.AlarmVolume) > double.Epsilon;
        var timerPresentationChanged = !string.Equals(this._lastTimerSecondsDisplay, this.TimerSecondsDisplay,
                                           StringComparison.Ordinal)
                                       || this._lastAnimatedTimerIcons != this.AnimatedTimerIcons;
        this._lastAlarmSoundId = this.AlarmSoundId;
        this._lastAlarmVolume = this.AlarmVolume;
        this._lastTimerSecondsDisplay = this.TimerSecondsDisplay;
        this._lastAnimatedTimerIcons = this.AnimatedTimerIcons;
        this.SaveSettings();

        if (alarmSoundChanged)
        {
            this.AlarmPreviewRequested?.Invoke(this, EventArgs.Empty);
        }

        if (timerPresentationChanged)
        {
            this.TimerPresentationChanged?.Invoke(this, EventArgs.Empty);
        }
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