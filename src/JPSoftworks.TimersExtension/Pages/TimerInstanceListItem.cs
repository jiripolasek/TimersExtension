using System.ComponentModel;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class TimerInstanceListItem : ListItem
{
    private readonly TimerInstance _timer;
    private readonly TimerScheduler _scheduler;
    private readonly AlarmService _alarmService;

    private TimerState _oldState;
    private bool _uiUpdatesEnabled = true;

    public TimerInstanceListItem(TimerInstance timer, TimerScheduler scheduler, AlarmService alarmService)
    {
        _timer = timer;
        _scheduler = scheduler;
        _alarmService = alarmService;
        _timer.PropertyChanged += Timer_PropertyChanged;
        UpdateInfo();
    }

    public void SetUIUpdatesEnabled(bool enabled)
    {
        if (enabled == _uiUpdatesEnabled)
        {
            return;
        }

        _uiUpdatesEnabled = enabled;

        if (enabled)
        {
            _timer.PropertyChanged += Timer_PropertyChanged;
            UpdateInfo();
        }
        else
        {
            _timer.PropertyChanged -= Timer_PropertyChanged;
        }
    }

    private void Timer_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateInfo();
    }

    private void UpdateInfo()
    {
        Title = _timer.Name;
        Subtitle = $"Left {_timer.Remaining:hh\\:mm\\:ss} of {_timer.Duration:hh\\:mm\\:ss} | Ends at {_timer.ExpectedEnd}";

        if (_oldState != _timer.State)
        {
            var pauseCommand = new AnonymousCommand(() =>
            {
                _alarmService.StopAlarm();
                _scheduler.Pause(_timer.Id);
            })
            { Name = "Pause", Icon = new IconInfo("\uE769"), Result = CommandResult.KeepOpen() };

            var resumeCommand = new AnonymousCommand(() =>
            {
                _alarmService.StopAlarm();
                _scheduler.Resume(_timer.Id);
            })
            { Name = "Resume", Icon = new IconInfo("\uE768"), Result = CommandResult.KeepOpen() };
            var stopCommand = new AnonymousCommand(() =>
            {
                _alarmService.StopAlarm();
                _scheduler.Stop(_timer.Id);
            })
            { Name = "Stop", Icon = new IconInfo("\uE71A"), Result = CommandResult.KeepOpen() };
            var restartCommand = new AnonymousCommand(() =>
            {
                _alarmService.StopAlarm();
                _scheduler.Restart(_timer.Id);
            })
            { Name = "Restart", Icon = new IconInfo("\uE777"), Result = CommandResult.KeepOpen() };
            var removeCommand = new AnonymousCommand(() =>
            {
                _alarmService.StopAlarm();
                _scheduler.RemoveTimer(_timer.Id);
            })
            { Name = "Remove", Icon = new IconInfo("\uE74D"), Result = CommandResult.KeepOpen() };

            switch (_timer.State)
            {
                case TimerState.Running:
                    Icon = Icons.RunningTimer;
                    Command = pauseCommand;
                    MoreCommands = [new CommandContextItem(stopCommand), new CommandContextItem(restartCommand)];
                    Tags = [new Tag("Running") { Background = new OptionalColor(true, new Color(56, 177, 131, 80)), Foreground = new OptionalColor(true, new Color(80, 253, 187, 255)) }];
                    break;
                case TimerState.Paused:
                    Icon = Icons.PausedTimer;
                    Command = resumeCommand;
                    MoreCommands = [new CommandContextItem(stopCommand), new CommandContextItem(restartCommand)];
                    Tags = [new Tag("Paused") { Background = new OptionalColor(true, new Color(230, 160, 0, 80)) }];
                    break;
                case TimerState.Finished:
                    Icon = Icons.FinishedTimer;
                    Command = removeCommand;
                    MoreCommands = [new CommandContextItem(restartCommand)];
                    Tags = [new Tag("Finished") { Background = new OptionalColor(true, new Color(0, 80, 160, 80)) }];
                    break;
                case TimerState.Cancelled:
                    Icon = Icons.CancelledTimer;
                    Command = removeCommand;
                    MoreCommands = [new CommandContextItem(restartCommand)];
                    Tags = [new Tag("Cancelled") { Background = new OptionalColor(true, new Color(180, 10, 0, 80)) }];
                    break;
                default:
                    Icon = Icons.GenericTimer;
                    Command = null!;
                    MoreCommands = [new CommandContextItem(removeCommand)];
                    break;
            }

            _oldState = _timer.State;
        }
    }
}