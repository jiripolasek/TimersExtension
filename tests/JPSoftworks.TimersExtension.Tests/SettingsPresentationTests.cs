// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Tests;

public sealed class SettingsPresentationTests
{
    [Theory]
    [InlineData("always", 600, true)]
    [InlineData("under-1-minute", 59, true)]
    [InlineData("under-1-minute", 60, false)]
    [InlineData("under-5-minutes", 299, true)]
    [InlineData("under-5-minutes", 300, false)]
    [InlineData("under-10-minutes", 599, true)]
    [InlineData("under-10-minutes", 600, false)]
    [InlineData("never", 1, false)]
    public void ShouldShowTimerSecondsHonorsThreshold(string mode, double seconds, bool expected)
    {
        Assert.Equal(expected, SettingsManager.ShouldShowTimerSeconds(mode, TimeSpan.FromSeconds(seconds)));
    }
}