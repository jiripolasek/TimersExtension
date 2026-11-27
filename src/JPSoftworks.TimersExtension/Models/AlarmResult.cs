// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace JPSoftworks.TimersExtension.Models;

public record AlarmResult(Guid TimerId, TimeSpan OriginalDuration, AlarmClosedKind Kind)
{
    internal string? TimerName { get; init; }
    internal DateTimeOffset? FinishedAt { get; init; }
    internal string? AlarmSoundId { get; init; }
}