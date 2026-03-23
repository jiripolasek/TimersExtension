using System.Linq;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class EditTimerPage : ContentPage
{
    private readonly EditTimerForm _form;
    private readonly IContent[] _contents;
    private readonly TimerScheduler _scheduler;

    public override IContent[] GetContent() => _contents;

    public EditTimerPage(TimerScheduler scheduler, TimersIndexPage timersIndexPage)
    {
        _scheduler = scheduler;
        Title = "New Timer";
        _form = new EditTimerForm(null, "Start Timer");
        _form.TimerSubmitted += Form_TimerSubmitted;
        _contents = [_form];
    }

    private void Form_TimerSubmitted(object? sender, TimerDefinition e)
    {
        var definition = e;
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            var usedNames = _scheduler.GetTimersSnapshot().Select(t => t.Name)
                .Concat(_scheduler.GetTemplatesSnapshot().Select(t => t.Name))
                .ToHashSet();

            var random = new Random();
            string newName;
            do
            {
                newName = $"Timer {random.Next(1, 10000)}";
            } while (usedNames.Contains(newName));

            definition = definition with { Name = newName };
        }
        _scheduler.StartTimer(definition);
    }
}