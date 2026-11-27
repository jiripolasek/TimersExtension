// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.IO;
using JPSoftworks.TimersExtension.Resources;

namespace JPSoftworks.TimersExtension.Services;

internal sealed record AlarmSoundDefinition(string Id, string DisplayName, string? FileName)
{
    public bool IsSilent => this.FileName is null;

    public string? Uri => this.FileName is null ? null : AlarmSoundCatalog.GetSoundUri(this.FileName);
}

internal static class AlarmSoundCatalog
{
    public const string DefaultId = "aurora";

    public const string FallbackFileName = "chimes.wav";

    private static readonly AlarmSoundDefinition[] Sounds =
    [
        new(DefaultId, Strings.AlarmSound_Aurora, "aurora.wav"),
        new("gentle-chime", Strings.AlarmSound_GentleChime, "gentle-chime.wav"),
        new("bright-bell", Strings.AlarmSound_BrightBell, "bright-bell.wav"),
        new("digital-pulse", Strings.AlarmSound_DigitalPulse, "digital-pulse.wav"),
        new("urgent-alert", Strings.AlarmSound_UrgentAlert, "urgent-alert.wav"),
        new("classic-chimes", Strings.AlarmSound_ClassicChimes, "chimes.wav"),
        new("silent", Strings.AlarmSound_Silent, null)
    ];

    public static IReadOnlyList<AlarmSoundDefinition> All => Sounds;

    public static IEnumerable<(string? Id, string Name)> TimerChoices
    {
        get
        {
            yield return (null, Strings.AlarmSound_UseDefault);
            foreach (var sound in Sounds)
            {
                yield return (sound.Id, sound.DisplayName);
            }
        }
    }

    // Resolve next to the executable: in the WAP package, ms-appx:/// points at the package root, not the app folder.
    public static string GetSoundUri(string fileName)
        => new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", fileName)).AbsoluteUri;

    public static AlarmSoundDefinition Get(string? id) => Find(id) ?? Sounds[0];

    public static AlarmSoundDefinition? Find(string? id)
    {
        foreach (var sound in All)
        {
            if (string.Equals(sound.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return sound;
            }
        }

        return null;
    }
}