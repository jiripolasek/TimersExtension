using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class TimerTemplateListItem : ListItem
{
    public TimerTemplateListItem(TimerDefinition template, TimerScheduler scheduler)
        : base(new StartTimerCommand(template with { IsTemplate = false }, scheduler))
    {
        Title = template.Name;
        Subtitle = template.Duration.ToString("hh\\:mm\\:ss");
        Icon = Icons.GenericTimer;

        var removeCommand = new AnonymousCommand(() => scheduler.RemoveTemplate(template.Name))
        {
            Name = "Remove",
            Icon = Icons.Delete,
            Result = CommandResult.KeepOpen()
        };

        MoreCommands = [new CommandContextItem(removeCommand)];
    }
}