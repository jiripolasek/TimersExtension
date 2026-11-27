// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class StartTimerCommand : InvokableCommand
{
    private readonly TimerDefinition? _timerDefinition;
    private readonly TimerScheduler _timerScheduler;
    private readonly Guid? _homeTemplateId;

    public StartTimerCommand(TimerDefinition timerDefinition, TimerScheduler timerScheduler, string? commandId = null)
    {
        this._timerDefinition = timerDefinition;
        this._timerScheduler = timerScheduler;
        this.Name = Strings.Action_StartTimer;
        if (!string.IsNullOrWhiteSpace(commandId))
        {
            this.Id = commandId;
        }
    }

    internal StartTimerCommand(Guid homeTemplateId, TimerScheduler timerScheduler)
    {
        this._timerScheduler = timerScheduler;
        this._homeTemplateId = homeTemplateId;
        this.Name = Strings.Action_StartTimer;
        this.Id = CommandIds.HomeTimerTemplate(homeTemplateId);
        this.Icon = Icons.NewTimer;
    }

    public override ICommandResult Invoke()
    {
        var definition = this._homeTemplateId is { } templateId
            ? this._timerScheduler.TryGetTemplate(templateId)
            : this._timerDefinition;
        if (definition is null)
        {
            return CommandResult.ShowToast(new ToastArgs
            {
                Message = Strings.Toast_TimerTemplateUnavailable, Result = CommandResult.KeepOpen()
            });
        }

        if (!TimerInstance.IsValidDuration(definition.Duration, DateTimeOffset.UtcNow))
        {
            return CommandResult.ShowToast(new ToastArgs
            {
                Message = Strings.Form_InvalidDurationMessage, Result = CommandResult.KeepOpen()
            });
        }

        this._timerScheduler.StartTimer(this._homeTemplateId.HasValue
            ? definition with { IsTemplate = false }
            : definition);
        return CommandResult.ShowToast(new ToastArgs
        {
            Message = ResourceFormatter.Format(
                Strings.Toast_StartedTimerFormat,
                LabelFormatter.FormatTemplateTitle(definition.Name, definition.Duration)),
            Result = this._homeTemplateId.HasValue ? CommandResult.Dismiss() : CommandResult.KeepOpen()
        });
    }
}