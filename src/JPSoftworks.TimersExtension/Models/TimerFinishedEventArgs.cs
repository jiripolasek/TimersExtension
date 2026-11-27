// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace JPSoftworks.TimersExtension.Models;

public sealed class TimerFinishedEventArgs : EventArgs
{
    public TimerInstance Timer { get; }

    internal TimeSpan CountdownDuration { get; }
    internal bool WasSnoozed { get; }
    internal string? AlarmSoundId { get; }
    internal DateTimeOffset? FinishedAt { get; init; }
    internal DateTimeOffset? DetectedAt { get; init; }

    public TimerFinishedEventArgs(TimerInstance timer)
    {
        this.Timer = timer;
        this.FinishedAt = timer.State == TimerState.Finished ? timer.PausedAt : null;
        this.WasSnoozed = timer.SnoozeDuration.HasValue;
        this.CountdownDuration = timer.CountdownDuration;
        this.AlarmSoundId = timer.AlarmSoundId;
    }
}