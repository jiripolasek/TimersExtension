// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace JPSoftworks.TimersExtension;

internal static class CommandIds
{
    public const string Provider = "JPSoftworks.CmdPal.TimersExtension";
    public const string TimersTopLevel = Provider + ".topLevel.timers";
    public const string StopwatchesTopLevel = Provider + ".topLevel.stopwatches";
    public const string TimerCreatePage = Provider + ".page.timer.create";
    public const string StopwatchCreate = Provider + ".command.stopwatch.create";
    public const string TimerDockBand = Provider + ".dock.band.timers";
    public const string StopwatchDockBand = Provider + ".dock.band.stopwatches";
    public const string AllTimersDockBand = Provider + ".dock.band.allTimers";
    public const string AllStopwatchesDockBand = Provider + ".dock.band.allStopwatches";
    public const string ActiveTimersDockBand = Provider + ".dock.band.activeTimers";
    public const string ActiveStopwatchesDockBand = Provider + ".dock.band.activeStopwatches";
    public const string TimerFallback = Provider + ".fallback.timer";
    public const string StopwatchFallback = Provider + ".fallback.stopwatch";
    public const string TimerRepeatLast = Provider + ".command.timer.repeatLast";
    public const string StopNextTimer = Provider + ".command.timer.stopNext";
    public const string DismissRingingTimer = Provider + ".command.timer.dismissRinging";

    private const string TimerTemplatePrefix = Provider + ".command.timer.template.";
    private const string HomeTimerTemplatePrefix = Provider + ".topLevel.timer.template.";
    private const string TimerDetailPrefix = Provider + ".page.timer.detail.";
    private const string StopwatchDetailPrefix = Provider + ".page.stopwatch.detail.";

    public static string TimerTemplate(Guid templateId) => TimerTemplatePrefix + templateId.ToString("N");

    public static string HomeTimerTemplate(Guid templateId) => HomeTimerTemplatePrefix + templateId.ToString("N");

    public static string TimerDetail(Guid timerId) => TimerDetailPrefix + timerId.ToString("N");

    public static string StopwatchDetail(Guid stopwatchId) => StopwatchDetailPrefix + stopwatchId.ToString("N");

    public static bool TryGetTimerTemplateId(string id, out Guid templateId)
    {
        return TryParseGuidSuffix(id, TimerTemplatePrefix, out templateId);
    }

    public static bool TryGetHomeTimerTemplateId(string id, out Guid templateId) =>
        TryParseGuidSuffix(id, HomeTimerTemplatePrefix, out templateId);

    public static bool TryGetTimerDetailId(string id, out Guid timerId)
    {
        return TryParseGuidSuffix(id, TimerDetailPrefix, out timerId);
    }

    public static bool TryGetStopwatchDetailId(string id, out Guid stopwatchId)
    {
        return TryParseGuidSuffix(id, StopwatchDetailPrefix, out stopwatchId);
    }

    private static bool TryParseGuidSuffix(string id, string prefix, out Guid value)
    {
        value = Guid.Empty;
        if (!id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Guid.TryParseExact(id[prefix.Length..], "N", out value);
    }
}