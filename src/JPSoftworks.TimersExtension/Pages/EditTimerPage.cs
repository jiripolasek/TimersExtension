// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class EditTimerPage : ContentPage
{
    internal const string PageId = CommandIds.TimerCreatePage;

    private readonly TimerScheduler _scheduler;

    public EditTimerPage(TimerScheduler scheduler)
    {
        this._scheduler = scheduler;
        this.Id = PageId;
        this.Icon = Icons.NewTimer;
        this.Title = Strings.Action_NewTimer;
        this.Name = Strings.Page_Create;
    }

    public override IContent[] GetContent()
    {
        var form = new EditTimerForm(null, Strings.Action_StartTimer);
        form.TimerSubmitted += this.Form_TimerSubmitted;
        return [form];
    }

    private void Form_TimerSubmitted(object? sender, TimerSubmittedEventArgs e)
    {
        var timer = this._scheduler.StartTimer(e.Definition);
        var name = LabelFormatter.FormatTimerName(timer.Name, timer.Duration);
        var message = timer.ExpectedEnd is { } endTime
            ? ResourceFormatter.Format(Strings.Toast_StartedTimerWithEndTimeFormat, name,
                LabelFormatter.FormatEndTime(endTime, timer.StartedAt ?? DateTimeOffset.UtcNow,
                    TimeZoneInfo.Local, timer.Duration < TimeSpan.FromMinutes(1)))
            : ResourceFormatter.Format(Strings.Toast_StartedTimerFormat, name);
        e.Result = CommandResult.ShowToast(new ToastArgs { Message = message, Result = CommandResult.GoBack() });
    }
}