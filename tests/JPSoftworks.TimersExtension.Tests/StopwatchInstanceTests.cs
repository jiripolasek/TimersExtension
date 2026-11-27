// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Text.Json;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Serialization;

namespace JPSoftworks.TimersExtension.Tests;

public sealed class StopwatchInstanceTests
{
    [Fact]
    public void PausedStopwatchKeepsTheElapsedPortionOfTheCurrentLap()
    {
        var stopwatch = new StopwatchInstance();
        stopwatch.TransitionToPaused(DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(175130));

        Assert.Equal(stopwatch.Elapsed, stopwatch.CurrentLapTime);

        stopwatch.Laps = [new LapRecord { TotalTime = TimeSpan.FromMilliseconds(169180) }];
        Assert.Equal(TimeSpan.FromMilliseconds(5950), stopwatch.CurrentLapTime);
    }

    [Theory]
    [InlineData("[{}]")]
    [InlineData("[{\"Laps\":null}]")]
    public void MissingOrNullLapsRestoreAsAnEmptyCollection(string json)
    {
        var stopwatch
            = Assert.Single(JsonSerializer.Deserialize(json, StopwatchJsonContext.Default.StopwatchInstanceArray)!);

        Assert.Empty(stopwatch.Laps);
        Assert.Equal(1, stopwatch.RecordLap(DateTimeOffset.UtcNow).Number);
    }

    [Fact]
    public void NormalizeAfterRestoreStopsRunningStopwatchWithTerminalMarker()
    {
        var stoppedAt = DateTimeOffset.UtcNow;
        var stopwatch = new StopwatchInstance
        {
            StartedAt = stoppedAt - TimeSpan.FromMinutes(2),
            StoppedAt = stoppedAt,
            ElapsedBeforePause = TimeSpan.FromMinutes(2),
            State = StopwatchState.Running
        };

        var changed = stopwatch.NormalizeAfterRestore();

        Assert.True(changed);
        Assert.Equal(StopwatchState.Stopped, stopwatch.State);
        Assert.Equal(TimeSpan.FromMinutes(2), stopwatch.Elapsed);
    }

    [Fact]
    public void NormalizeAfterRestoreLeavesRunningStopwatchWithoutTerminalMarker()
    {
        var stopwatch = new StopwatchInstance { StartedAt = DateTimeOffset.UtcNow, State = StopwatchState.Running };

        var changed = stopwatch.NormalizeAfterRestore();

        Assert.False(changed);
        Assert.Equal(StopwatchState.Running, stopwatch.State);
    }
}