// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Linq;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class TimerSoundPage : ListPage
{
    private static readonly IconInfo SelectedIcon = new("\uE73E");
    private readonly TimerInstance _timer;
    private readonly TimerScheduler _scheduler;

    public TimerSoundPage(TimerInstance timer, TimerScheduler scheduler)
    {
        this._timer = timer;
        this._scheduler = scheduler;
        this.Name = Strings.Setting_AlarmSound_Title;
        this.Title = this.Name;
        this.Icon = Icons.AlarmSound;
    }

    public override IListItem[] GetItems()
    {
        var selectedId = AlarmSoundCatalog.Find(this._timer.AlarmSoundId)?.Id;
        return AlarmSoundCatalog.TimerChoices.Select(choice =>
        {
            var command = new AnonymousCommand(() => this._scheduler.SetAlarmSound(this._timer.Id, choice.Id))
            {
                Name = choice.Name, Result = CommandResult.GoBack()
            };
            var item = new ListItem(command) { Title = choice.Name };
            if (choice.Id == selectedId)
            {
                item.Icon = SelectedIcon;
            }

            return item;
        }).ToArray();
    }
}