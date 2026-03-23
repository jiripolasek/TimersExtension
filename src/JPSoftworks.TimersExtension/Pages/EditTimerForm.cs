using System.Linq;
using System.Text.Json.Nodes;
using JPSoftworks.TimersExtension.Models;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class EditTimerForm : FormContent
{
    public event EventHandler<TimerDefinition>? TimerSubmitted;

    public EditTimerForm(TimerDefinition? defaults = null, string buttonTitle = "Start Timer", bool lockTemplateToggle = false)
    {
        var name = defaults?.Name ?? string.Empty;
        var duration = defaults?.Duration.ToString() ?? "00:05:00";
        if (defaults?.Duration.TotalMinutes % 1 == 0 && defaults?.Duration.Seconds == 0)
        {
            duration = defaults?.Duration.ToString("c") ?? "00:05:00";
        }

        var saveAsTemplate = defaults?.IsTemplate ?? false;
        var toggleValue = saveAsTemplate ? "true" : "false";

        TemplateJson = $$"""
                         {
                             "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
                             "type": "AdaptiveCard",
                             "version": "1.5",
                             "body": [
                                 {
                                     "type": "Input.Text",
                                     "id": "name",
                                     "label": "Name",
                                     "placeholder": "Timer name (optional)",
                                     "value": "{{JsonEncoded(name)}}",
                                     "isRequired": false
                                 },
                                 {
                                     "type": "Input.Text",
                                     "id": "duration",
                                     "label": "Duration (e.g. 5m, 1h 30m, 05:00)",
                                     "value": "{{JsonEncoded(duration)}}",
                                     "errorMessage": "Invalid duration format",
                                     "isRequired": true
                                 },
                                 {
                                     "type": "Input.Toggle",
                                     "id": "saveAsTemplate",
                                     "title": "Save as template",
                                     "value": "{{toggleValue}}"
                                 }
                             ],
                             "actions": [
                                 {
                                     "type": "Action.Submit",
                                     "title": "{{JsonEncoded(buttonTitle)}}"
                                 }
                             ]
                         }
                         """;
    }

    private string JsonEncoded(string input) => System.Text.Json.JsonSerializer.Serialize(input).Trim('"');

    public override ICommandResult SubmitForm(string payload)
    {
        var formInput = JsonNode.Parse(payload);
        if (formInput is null)
        {
            return CommandResult.KeepOpen();
        }

        var name = formInput["name"]?.ToString();
        var durationStr = formInput["duration"]?.ToString() ?? string.Empty;
        var saveAsTemplate = formInput["saveAsTemplate"]?.ToString() == "true";

        if (TryParseDuration(durationStr, out var duration))
        {
            TimerSubmitted?.Invoke(this, new TimerDefinition(name ?? string.Empty, duration, saveAsTemplate));
            return CommandResult.GoBack();
        }

        return CommandResult.ShowToast(new ToastArgs { Message = "Invalid duration format. Try '5m', '1h', or '05:00'", Result = CommandResult.KeepOpen() });
    }

    private static bool TryParseDuration(string input, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        input = input.Trim().ToLowerInvariant();

        if (input.Any(c => c == 'h' || c == 'm' || c == 's'))
        {
            try
            {
                var ts = TimeSpan.Zero;
                var currentVal = "";
                for (int i = 0; i < input.Length; i++)
                {
                    char c = input[i];
                    if (char.IsDigit(c) || c == '.')
                    {
                        currentVal += c;
                    }
                    else if (currentVal.Length > 0 && (c == 'h' || c == 'm' || c == 's'))
                    {
                        if (double.TryParse(currentVal, out var val))
                        {
                            if (c == 'h')
                            {
                                ts += TimeSpan.FromHours(val);
                            }

                            if (c == 'm')
                            {
                                ts += TimeSpan.FromMinutes(val);
                            }

                            if (c == 's')
                            {
                                ts += TimeSpan.FromSeconds(val);
                            }
                        }
                        currentVal = "";
                    }
                }

                if (ts > TimeSpan.Zero)
                {
                    duration = ts;
                    return true;
                }
            }
            catch { }
        }

        if (input.Contains(':'))
        {
            if (TimeSpan.TryParse(input, out var ts))
            {
                duration = ts;
                return true;
            }
        }

        if (double.TryParse(input, out var mins))
        {
            duration = TimeSpan.FromMinutes(mins);
            return true;
        }

        return false;
    }
}