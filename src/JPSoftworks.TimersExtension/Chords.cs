// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Windows.System;

namespace JPSoftworks.TimersExtension;

internal static class Chords
{
    public static KeyChord Start { get; } = From(true, vkey: VirtualKey.Enter);
    public static KeyChord Pause { get; } = From(true, vkey: VirtualKey.P);
    public static KeyChord Resume { get; } = From(true, vkey: VirtualKey.P);
    public static KeyChord Stop { get; } = From(true, vkey: VirtualKey.S);
    public static KeyChord Restart { get; } = From(true, shift: true, vkey: VirtualKey.R);
    public static KeyChord Lap { get; } = From(true, vkey: VirtualKey.L);
    public static KeyChord Reset { get; } = From(true, vkey: VirtualKey.R);
    public static KeyChord Edit { get; } = From(true, vkey: VirtualKey.E);
    public static KeyChord Delete { get; } = From(true, vkey: VirtualKey.Delete);
    public static KeyChord StopAll { get; } = From(true, shift: true, vkey: VirtualKey.S);
    public static KeyChord ClearAll { get; } = From(true, shift: true, vkey: VirtualKey.Delete);
    public static KeyChord RepeatLast { get; } = From(true, shift: true, vkey: VirtualKey.Enter);

    private static KeyChord From(
        bool ctrl = false,
        bool alt = false,
        bool shift = false,
        bool win = false,
        VirtualKey vkey = 0,
        int scanCode = 0)
    {
        return KeyChordHelpers.FromModifiers(ctrl, alt, shift, win, (int)vkey, scanCode);
    }
}