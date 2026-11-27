// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class EditTimerForm : FormContent
{
    public event EventHandler<TimerSubmittedEventArgs>? TimerSubmitted;
    private const string DefaultSoundValue = "default";
    private readonly bool _lockTemplateToggle;
    private readonly string? _defaultAlarmSoundId;
    private readonly string _initialDataJson;

    public EditTimerForm(TimerDefinition? defaults = null, string? buttonTitle = null, bool lockTemplateToggle = false)
    {
        this._lockTemplateToggle = lockTemplateToggle;
        this._defaultAlarmSoundId = AlarmSoundCatalog.Find(defaults?.AlarmSoundId)?.Id;
        buttonTitle ??= Strings.Action_StartTimer;
        var name = defaults?.Name ?? string.Empty;
        var duration = defaults?.Duration.ToString("c", CultureInfo.InvariantCulture) ?? string.Empty;
        var timingLabel = lockTemplateToggle ? Strings.Form_DurationLabel : Strings.Form_DurationOrEndTimeLabel;
        var timingHint = lockTemplateToggle ? Strings.Form_DurationHint : Strings.Form_DurationOrEndTimeHint;
        var soundChoices = string.Join(",", AlarmSoundCatalog.TimerChoices.Select(choice =>
            $$"""{"title":"{{JsonEncoded(choice.Name)}}","value":"{{JsonEncoded(choice.Id ?? DefaultSoundValue)}}"}"""));

        this._initialDataJson = CreateFormData(name, duration, string.Empty, this._defaultAlarmSoundId,
            defaults?.IsTemplate ?? false);
        this.DataJson = this._initialDataJson;

        var endTimeInput = lockTemplateToggle
            ? string.Empty
            : $$"""
                ,
                {
                    "type": "Input.Time",
                    "id": "endTime",
                    "label": "{{JsonEncoded(Strings.Form_EndTimeLabel)}}",
                    "value": "${endTime}",
                    "isRequired": false
                }
                """;
        var saveTemplateInput = lockTemplateToggle
            ? string.Empty
            : $$"""
                ,
                {
                    "type": "Input.Toggle",
                    "id": "saveAsTemplate",
                    "title": "{{JsonEncoded(Strings.Form_ToggleSaveAsTemplate)}}",
                    "value": "${saveAsTemplate}"
                }
                """;

        this.TemplateJson = $$"""
                              {
                                  "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
                                  "type": "AdaptiveCard",
                                  "version": "1.5",
                                  "body": [
                                      {
                                          "type": "Input.Text",
                                          "id": "duration",
                                          "label": "{{JsonEncoded(timingLabel)}}",
                                          "value": "${duration}",
                                          "errorMessage": "{{JsonEncoded(Strings.Form_InvalidDurationError)}}",
                                          "isRequired": {{(lockTemplateToggle ? "true" : "false")}}
                                      },
                                      {
                                          "type": "TextBlock",
                                          "id": "durationHint",
                                          "text": "{{JsonEncoded(timingHint)}}",
                                          "size": "Small",
                                          "spacing": "Small",
                                          "isSubtle": true,
                                          "wrap": true
                                      }
                                      {{endTimeInput}},
                                      {
                                          "$when": "${hasError}",
                                          "type": "TextBlock",
                                          "id": "timingError",
                                          "text": "${errorMessage}",
                                          "color": "Attention",
                                          "wrap": true
                                      },
                                      {
                                          "type": "Input.Text",
                                          "id": "name",
                                          "label": "{{JsonEncoded(Strings.Form_NameLabel)}}",
                                          "value": "${name}",
                                          "isRequired": false
                                      },
                                      {
                                          "type": "Input.ChoiceSet",
                                          "id": "alarmSound",
                                          "label": "{{JsonEncoded(Strings.Setting_AlarmSound_Title)}}",
                                          "style": "compact",
                                          "value": "${alarmSound}",
                                          "choices": [{{soundChoices}}]
                                      }
                                      {{saveTemplateInput}}
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

    private static string JsonEncoded(string input) => JsonEncodedText.Encode(input).ToString();

    private static string CreateFormData(
        string name,
        string duration,
        string endTime,
        string? alarmSoundId,
        bool saveAsTemplate,
        string? error = null) =>
        $$"""
          {
              "name": "{{JsonEncoded(name)}}",
              "duration": "{{JsonEncoded(duration)}}",
              "endTime": "{{JsonEncoded(endTime)}}",
              "alarmSound": "{{JsonEncoded(alarmSoundId ?? DefaultSoundValue)}}",
              "saveAsTemplate": "{{(saveAsTemplate ? "true" : "false")}}",
              "hasError": {{(error is null ? "false" : "true")}},
              "errorMessage": "{{JsonEncoded(error ?? string.Empty)}}"
          }
          """;

    public override ICommandResult SubmitForm(string payload) =>
        this.SubmitForm(payload, DateTimeOffset.Now, TimeZoneInfo.Local);

    internal ICommandResult SubmitForm(string payload, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        if (JsonNode.Parse(payload) is not JsonObject formInput)
        {
            return CommandResult.KeepOpen();
        }

        var name = formInput["name"]?.ToString() ?? string.Empty;
        var durationStr = formInput["duration"]?.ToString() ?? string.Empty;
        var endTimeStr = formInput["endTime"]?.ToString() ?? string.Empty;
        var soundValue = formInput["alarmSound"]?.ToString();
        var alarmSoundId = soundValue == DefaultSoundValue
            ? null
            : AlarmSoundCatalog.Find(soundValue)?.Id ?? this._defaultAlarmSoundId;
        var saveAsTemplate = this._lockTemplateToggle || formInput["saveAsTemplate"]?.ToString() == "true";

        if (!string.IsNullOrWhiteSpace(endTimeStr))
        {
            if (saveAsTemplate)
            {
                return ShowTimingError(Strings.Form_EndTimeTemplateMessage);
            }

            if (EndTimeParser.TryGetDuration(
                    endTimeStr,
                    now,
                    timeZone,
                    out var endTimeDuration,
                    out _))
            {
                return SubmitTimer(endTimeDuration);
            }

            return ShowTimingError(Strings.Form_InvalidEndTimeMessage);
        }

        if (DurationParser.TryParse(durationStr, out var duration))
        {
            return SubmitTimer(duration);
        }

        if (!this._lockTemplateToggle && TimerCreationQueryParser.TryParse(durationStr, now, timeZone, out var timing)
                                      && timing is { EndTime: not null, Name: null })
        {
            return saveAsTemplate
                ? ShowTimingError(Strings.Form_EndTimeTemplateMessage)
                : SubmitTimer(timing.Duration);
        }

        if (!saveAsTemplate && string.IsNullOrWhiteSpace(durationStr))
        {
            return ShowTimingError(Strings.Form_DurationOrEndTimeRequired);
        }

        return ShowTimingError(saveAsTemplate
            ? Strings.Form_InvalidDurationMessage
            : Strings.Form_InvalidTimerTimingMessage);

        ICommandResult SubmitTimer(TimeSpan inputDuration)
        {
            var args = new TimerSubmittedEventArgs(new TimerDefinition(name, inputDuration, saveAsTemplate)
            {
                AlarmSoundId = alarmSoundId
            });
            this.TimerSubmitted?.Invoke(this, args);
            this.DataJson = this._initialDataJson;
            return args.Result;
        }

        ICommandResult ShowTimingError(string message)
        {
            this.DataJson = CreateFormData(name, durationStr, endTimeStr, alarmSoundId, saveAsTemplate, message);
            return CommandResult.KeepOpen();
        }
    }
}

internal sealed class TimerSubmittedEventArgs(TimerDefinition definition) : EventArgs
{
    public TimerDefinition Definition { get; } = definition;

    public ICommandResult Result { get; set; } = CommandResult.GoBack();
}