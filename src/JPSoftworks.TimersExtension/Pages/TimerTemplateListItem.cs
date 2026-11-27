// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.TimersExtension.Commands;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class TimerTemplateListItem : ListItem
{
    internal TimerDefinition Definition { get; }

    public TimerTemplateListItem(TimerDefinition template, TimerScheduler scheduler)
        : base(new StartTimerCommand(template with { IsTemplate = false }, scheduler,
            CommandIds.TimerTemplate(template.Id)))
    {
        this.Definition = template;
        this.Title = LabelFormatter.FormatTemplateName(template);
        this.Subtitle = LabelFormatter.FormatTemplateSubtitle(template);
        this.Icon = Icons.GenericTimer;

        var editCommand = new EditTemplatePage(template, scheduler);
        var deleteCommand = new AnonymousCommand(() => scheduler.RemoveTemplate(template.Id))
        {
            Name = Strings.Action_Delete, Icon = Icons.Delete, Result = CommandResult.KeepOpen()
        };
        var confirmedDeleteCommand = new DeletionConfirmationCommand(
            deleteCommand,
            Strings.Confirm_DeleteTemplate_Title,
            ResourceFormatter.Format(Strings.Confirm_DeleteTemplate_Description,
                LabelFormatter.FormatTemplateName(template)));

        this.MoreCommands =
        [
            new CommandContextItem(editCommand) { Title = Strings.Action_Edit, RequestedShortcut = Chords.Edit },
            new CommandContextItem(confirmedDeleteCommand) { RequestedShortcut = Chords.Delete }
        ];
    }
}