// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class EditTemplatePage : ContentPage
{
    private readonly TimerScheduler _scheduler;
    private readonly TimerDefinition _originalTemplate;

    public EditTemplatePage(TimerDefinition template, TimerScheduler scheduler)
    {
        this._originalTemplate = template;
        this._scheduler = scheduler;
        this.Name = Strings.Action_Edit;
        this.Title = Strings.Page_EditTemplate_Title;
    }

    public override IContent[] GetContent()
    {
        var form = new EditTimerForm(this._originalTemplate, Strings.Page_EditTemplate_Save, true);
        form.TimerSubmitted += this.Form_TimerSubmitted;
        return [form];
    }

    private void Form_TimerSubmitted(object? sender, TimerSubmittedEventArgs e)
    {
        if (!this._scheduler.TryUpdateTemplate(e.Definition with { Id = this._originalTemplate.Id }))
        {
            e.Result = CommandResult.ShowToast(new ToastArgs
            {
                Message = Strings.Toast_TemplateDeleted, Result = CommandResult.GoBack()
            });
        }
    }
}