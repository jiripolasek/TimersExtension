// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Pages;
using JPSoftworks.TimersExtension.Resources;
using Microsoft.CommandPalette.Extensions;

namespace JPSoftworks.TimersExtension.Tests;

public sealed class EditTimerFormTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CardPutsTimingBeforeTheOptionalName(bool editingTemplate)
    {
        var defaults = editingTemplate ? new TimerDefinition("Tea", TimeSpan.FromMinutes(7), true) : null;
        var form = new EditTimerForm(defaults, lockTemplateToggle: editingTemplate);
        var inputs = JsonNode.Parse(form.TemplateJson)!["body"]!.AsArray();
        var data = JsonNode.Parse(form.DataJson)!;
        var duration = inputs[0]!;
        var hint = inputs[1]!;
        var name = inputs[editingTemplate ? 3 : 4]!;
        var alarmSound = inputs.Single(input => input!["id"]!.GetValue<string>() == "alarmSound")!;

        Assert.Equal("duration", duration["id"]!.GetValue<string>());
        Assert.Equal(editingTemplate ? Strings.Form_DurationLabel : Strings.Form_DurationOrEndTimeLabel,
            duration["label"]!.GetValue<string>());
        Assert.Equal(editingTemplate, duration["isRequired"]!.GetValue<bool>());
        Assert.Equal("TextBlock", hint["type"]!.GetValue<string>());
        Assert.Equal("durationHint", hint["id"]!.GetValue<string>());
        Assert.Equal(editingTemplate ? Strings.Form_DurationHint : Strings.Form_DurationOrEndTimeHint,
            hint["text"]!.GetValue<string>());
        Assert.Null(hint["$when"]);
        if (!editingTemplate)
        {
            Assert.Equal("endTime", inputs[2]!["id"]!.GetValue<string>());
        }

        var feedback = inputs[editingTemplate ? 2 : 3]!;
        Assert.Equal("${hasError}", feedback["$when"]!.GetValue<string>());
        Assert.Equal("${errorMessage}", feedback["text"]!.GetValue<string>());
        Assert.Equal("name", name["id"]!.GetValue<string>());
        Assert.False(name["isRequired"]!.GetValue<bool>());
        Assert.Equal(editingTemplate ? "00:07:00" : string.Empty, data["duration"]!.GetValue<string>());
        Assert.Equal("default", data["alarmSound"]!.GetValue<string>());
        Assert.False(data["hasError"]!.GetValue<bool>());
        Assert.Contains(alarmSound["choices"]!.AsArray(), choice => choice!["value"]!.GetValue<string>() == "silent");
    }

    [Fact]
    public void EmptyDurationAndEndTimeKeepNewTimerOpen()
    {
        var form = new EditTimerForm();
        var submitted = false;
        form.TimerSubmitted += (_, _) => submitted = true;

        var result = form.SubmitForm("""{"duration":"","endTime":""}""");

        Assert.False(submitted);
        Assert.Equal(CommandResultKind.KeepOpen, result.Kind);
        var data = JsonNode.Parse(form.DataJson)!;
        Assert.True(data["hasError"]!.GetValue<bool>());
        Assert.Equal(Strings.Form_DurationOrEndTimeRequired, data["errorMessage"]!.GetValue<string>());
    }

    [Fact]
    public void FormRejectsDurationBeyondTheEndTimestampRange()
    {
        var form = new EditTimerForm();
        var submitted = false;
        form.TimerSubmitted += (_, _) => submitted = true;

        var result = form.SubmitForm(
            """{"name":"Long \"timer\"","duration":"100000000h","alarmSound":"silent","saveAsTemplate":"true"}""");

        Assert.False(submitted);
        Assert.Equal(CommandResultKind.KeepOpen, result.Kind);
        var data = JsonNode.Parse(form.DataJson)!;
        Assert.Equal("Long \"timer\"", data["name"]!.GetValue<string>());
        Assert.Equal("100000000h", data["duration"]!.GetValue<string>());
        Assert.Equal("silent", data["alarmSound"]!.GetValue<string>());
        Assert.Equal("true", data["saveAsTemplate"]!.GetValue<string>());
        Assert.Equal(Strings.Form_InvalidDurationMessage, data["errorMessage"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("until 11:00", "", 10, 1)]
    [InlineData("until 11:00", "", 12, 23)]
    [InlineData("until 1100", "", 10, 1)]
    [InlineData("until 11:00", "13:00", 10, 3)]
    public void NewTimerAcceptsATypedEndTimeAndHonorsThePicker(
        string timing,
        string picker,
        int nowHour,
        int expectedHours)
    {
        var form = new EditTimerForm();
        var now = new DateTimeOffset(2026, 11, 27, nowHour, 0, 0, TimeSpan.Zero);
        TimerDefinition? submitted = null;
        form.TimerSubmitted += (_, args) => submitted = args.Definition;

        var payload = new JsonObject { ["name"] = "Tea", ["duration"] = timing, ["endTime"] = picker };
        var result = form.SubmitForm(payload.ToJsonString(), now, TimeZoneInfo.Utc);

        Assert.Equal(CommandResultKind.GoBack, result.Kind);
        Assert.NotNull(submitted);
        Assert.Equal(TimeSpan.FromHours(expectedHours), submitted.Duration);
        Assert.Equal("Tea", submitted.Name);
    }

    [Fact]
    public void InvalidTypedEndTimeShowsTimingHelp()
    {
        var form = new EditTimerForm();
        var result = form.SubmitForm("""{"duration":"until 25:00"}""");

        Assert.Equal(CommandResultKind.KeepOpen, result.Kind);
        var data = JsonNode.Parse(form.DataJson)!;
        Assert.Equal(Strings.Form_InvalidTimerTimingMessage, data["errorMessage"]!.GetValue<string>());
        Assert.Equal("until 25:00", data["duration"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("", "16:00")]
    [InlineData("until 11:00", "")]
    public void SavingAnEndTimeAsATemplateKeepsTheFormOpen(string timing, string picker)
    {
        var form = new EditTimerForm();
        TimerDefinition? submitted = null;
        form.TimerSubmitted += (_, args) => submitted = args.Definition;

        var payload = new JsonObject
        {
            ["name"] = "Tea", ["duration"] = timing, ["endTime"] = picker, ["saveAsTemplate"] = "true"
        };
        var result = form.SubmitForm(payload.ToJsonString());

        Assert.Null(submitted);
        Assert.Equal(CommandResultKind.KeepOpen, result.Kind);
        var data = JsonNode.Parse(form.DataJson)!;
        Assert.Equal(Strings.Form_EndTimeTemplateMessage, data["errorMessage"]!.GetValue<string>());
        Assert.Equal(timing, data["duration"]!.GetValue<string>());
        Assert.Equal(picker, data["endTime"]!.GetValue<string>());

        form.SubmitForm("""{"name":"Tea","endTime":"16:00","saveAsTemplate":"false","alarmSound":"silent"}""");
        Assert.NotNull(submitted);
        Assert.False(submitted.IsTemplate);
        Assert.Equal("silent", submitted.AlarmSoundId);
        Assert.False(JsonNode.Parse(form.DataJson)!["hasError"]!.GetValue<bool>());
        form.SubmitForm("""{"name":"Tea","duration":"5m","saveAsTemplate":"true","alarmSound":"bright-bell"}""");
        Assert.True(submitted.IsTemplate);
        Assert.Equal(TimeSpan.FromMinutes(5), submitted.Duration);
        Assert.Equal("bright-bell", submitted.AlarmSoundId);
    }

    [Theory]
    [InlineData("en-US", "1.01:00:00")]
    [InlineData("en-US", "00:01:30.5000000")]
    [InlineData("cs-CZ", "1.01:00:00.1234567")]
    [InlineData("fr-FR", "00:01:30.1234567")]
    [InlineData("ar-EG", "1.01:00:00.1234567")]
    [InlineData("ar-EG", "00:00:00.0000001")]
    public void RenamingTemplatePreservesItsPrefilledDuration(string cultureName, string durationText)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            var duration = TimeSpan.ParseExact(durationText, "c", CultureInfo.InvariantCulture);
            var template = new TimerDefinition("Focus", duration, true) { AlarmSoundId = "gentle-chime" };
            var form = new EditTimerForm(template, lockTemplateToggle: true);
            var prefilledDuration = JsonNode.Parse(form.DataJson)!["duration"]!.GetValue<string>();
            TimerDefinition? submitted = null;
            form.TimerSubmitted += (_, args) => submitted = args.Definition;

            form.SubmitForm(new JsonObject { ["name"] = "Renamed", ["duration"] = prefilledDuration }.ToJsonString());

            Assert.NotNull(submitted);
            Assert.Equal(duration, submitted.Duration);
            Assert.Equal("Renamed", submitted.Name);
            Assert.True(submitted.IsTemplate);
            Assert.Equal(template.AlarmSoundId, submitted.AlarmSoundId);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }
}