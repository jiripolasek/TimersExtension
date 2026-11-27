// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Models;

public sealed record AlarmRequest
{
    private static readonly TimeSpan MissedAlarmAfter = TimeSpan.FromMinutes(10);

    public Guid AlarmId { get; init; } = Guid.NewGuid();
    public required Guid TimerId { get; init; }
    internal string? TimerName { get; init; }
    internal DateTimeOffset? FinishedAt { get; init; }
    internal string? AlarmSoundId { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public required TimeSpan OriginalDuration { get; init; }
    public string? SoundUri { get; init; }
    public bool IsSilent { get; init; }
    internal bool IsMissed { get; init; }
    public double Volume { get; init; } = 1.0;

    public static AlarmRequest FromTimer(
        TimerInstance timer,
        string? soundUri = null,
        double volume = 1.0,
        bool isSilent = false) => FromTimer(new TimerFinishedEventArgs(timer), soundUri, volume, isSilent);

    internal static AlarmRequest FromTimer(
        TimerFinishedEventArgs completion,
        string? soundUri = null,
        double volume = 1.0,
        bool isSilent = false)
    {
        var timer = completion.Timer;
        var sound = AlarmSoundCatalog.Find(completion.AlarmSoundId);
        var name = LabelFormatter.FormatTimerName(timer.Name, timer.Duration);
        var now = completion.DetectedAt?.ToLocalTime() ?? DateTimeOffset.Now;
        var finishedAt = (completion.FinishedAt ?? (timer.State == TimerState.Finished ? timer.PausedAt : null) ?? now)
            .ToLocalTime();
        var isMissed = completion.DetectedAt is not null && now - finishedAt >= MissedAlarmAfter;
        var dateFormat = finishedAt.Date == now.Date ? "t" : "g";
        var titleFormat = (isMissed, completion.WasSnoozed) switch
        {
            (true, true) => Strings.Alarm_MissedSnoozeTitleFormat,
            (true, false) => Strings.Alarm_MissedTitleFormat,
            (false, true) => Strings.Alarm_SnoozeTitleFormat,
            _ => Strings.Alarm_TitleFormat
        };

        return new AlarmRequest
        {
            TimerId = timer.Id,
            TimerName = name,
            FinishedAt = completion.FinishedAt,
            AlarmSoundId = completion.AlarmSoundId,
            Title = ResourceFormatter.Format(titleFormat, name),
            Message = ResourceFormatter.Format(Strings.Alarm_MessageFormat,
                DurationParser.FormatCompact(completion.CountdownDuration),
                finishedAt.ToString(dateFormat, CultureInfo.CurrentCulture)),
            OriginalDuration = timer.Duration,
            SoundUri = sound is null ? soundUri : sound.Uri,
            IsSilent = (sound?.IsSilent ?? isSilent) || isMissed,
            IsMissed = isMissed,
            Volume = volume
        };
    }

    internal AlarmResult ToResult(AlarmClosedKind kind) => new(this.TimerId, this.OriginalDuration, kind)
    {
        TimerName = this.TimerName, FinishedAt = this.FinishedAt, AlarmSoundId = this.AlarmSoundId
    };
}