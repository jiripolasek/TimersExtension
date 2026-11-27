// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace JPSoftworks.TimersExtension.Models;

public sealed class LapRecord
{
    public int Number { get; init; }
    public TimeSpan LapTime { get; init; }
    public TimeSpan TotalTime { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
}