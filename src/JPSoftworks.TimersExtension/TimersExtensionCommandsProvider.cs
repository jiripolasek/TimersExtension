// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension;

public partial class TimersExtensionCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;
    private readonly SettingsManager _settingsManager = new();

    public TimersExtensionCommandsProvider()
    {
        Id = "JPSoftworks.CmdPal.TimersExtension";
        DisplayName = "Timers";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Settings = _settingsManager.Settings;


        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerToys", "CommandPalette", "Timers");

        _commands = [
            new CommandItem(new Pages.TimersIndexPage(TimerServices.Scheduler, new AlarmService())) { Title = DisplayName, MoreCommands = [new CommandContextItem(this.Settings.SettingsPage!)]  },
            new CommandItem(new Pages.StopwatchIndexPage(new StopwatchScheduler(new StopwatchStorage(baseDir)))) { Title = "Stopwatches", MoreCommands = [new CommandContextItem(this.Settings.SettingsPage!)] },
        ];
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }

}
