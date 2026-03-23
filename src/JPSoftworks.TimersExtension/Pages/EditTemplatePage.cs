using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class EditTemplatePage : ContentPage
{
    private readonly EditTimerForm _form;
    private readonly IContent[] _contents;
    private readonly TimerScheduler _scheduler;
    private readonly TimerDefinition _originalTemplate;

    public override IContent[] GetContent() => _contents;

    public EditTemplatePage(TimerDefinition template, TimerScheduler scheduler)
    {
        _originalTemplate = template;
        _scheduler = scheduler;
        Title = "Edit Template";
        _form = new EditTimerForm(template, "Save Template");
        _form.TimerSubmitted += Form_TimerSubmitted;
        _contents = [_form];
    }

    private void Form_TimerSubmitted(object? sender, TimerDefinition e)
    {
        _scheduler.RemoveTemplate(_originalTemplate.Name);
        if (e.IsTemplate)
        {
            _scheduler.AddTemplate(e);
        }
    }
}