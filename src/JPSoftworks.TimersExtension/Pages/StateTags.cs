// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.TimersExtension.Resources;

namespace JPSoftworks.TimersExtension.Pages;

internal static class StateTags
{
    public static Tag CreateRunning() => new(Strings.State_Running)
    {
        Background = new OptionalColor(true, new Color(56, 177, 131, 80)),
        Foreground = new OptionalColor(true, new Color(80, 253, 187, 255))
    };

    public static Tag CreatePaused() => new(Strings.State_Paused)
    {
        Background = new OptionalColor(true, new Color(230, 160, 0, 80))
    };

    public static Tag CreateFinished() => new(Strings.State_Finished)
    {
        Background = new OptionalColor(true, new Color(0, 80, 160, 80))
    };
}