// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Globalization;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Commands;

internal sealed partial class TimerFallbackCommand : InvokableCommand
{
    private readonly TimerScheduler _scheduler;
    private readonly Action<string>? _onTimerStarted;
    private string _query = string.Empty;

    public TimerFallbackCommand(TimerScheduler scheduler, Action<string>? onTimerStarted = null)
    {
        this._scheduler = scheduler;
        this._onTimerStarted = onTimerStarted;
        this.Id = CommandIds.TimerFallback;
        this.Name = Strings.Action_StartTimer;
        this.Icon = Icons.NewTimer;
    }

    public void UpdateQueryText(string query)
    {
        this._query = query;
    }

    public override CommandResult Invoke()
    {
        var submittedQuery = this._query;
        if (TimerCreationQueryParser.TryParse(submittedQuery, out var creation))
        {
            this._scheduler.StartTimer(creation.Duration, creation.Name);
            this._onTimerStarted?.Invoke(submittedQuery);
            return CommandResult.ShowToast(new ToastArgs
            {
                Message = FormatStartedMessage(creation), Result = CommandResult.Dismiss()
            });
        }

        return CommandResult.ShowToast(new ToastArgs
        {
            Message = Strings.Fallback_TypeDurationMessage, Result = CommandResult.KeepOpen()
        });
    }

    private static string FormatStartedMessage(TimerCreationQuery creation)
    {
        if (creation.EndTime is { } endTime)
        {
            var localEndTime = endTime.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
            return creation.Name is null
                ? ResourceFormatter.Format(Strings.Fallback_StartedTimerUntilFormat, localEndTime)
                : ResourceFormatter.Format(Strings.Fallback_StartedNamedTimerUntilFormat, creation.Name, localEndTime);
        }

        return creation.Name is null
            ? ResourceFormatter.Format(Strings.Fallback_StartedTimerFormat,
                DurationParser.FormatCompact(creation.Duration))
            : ResourceFormatter.Format(Strings.Fallback_StartedNamedTimerFormat,
                creation.Name,
                DurationParser.FormatCompact(creation.Duration));
    }
}

internal sealed partial class TimerFallbackCommandItem : CommandItem, IFallbackCommandItem, IFallbackHandler
{
    private readonly TimerFallbackCommand _command;

    public IFallbackHandler FallbackHandler { get; set; }
    public string DisplayTitle => Strings.Action_StartTimer;
    internal bool HasCreationQuery { get; private set; }

    public TimerFallbackCommandItem(TimerScheduler scheduler, Action<string>? onTimerStarted = null)
        : this(new TimerFallbackCommand(scheduler, onTimerStarted))
    {
    }

    private TimerFallbackCommandItem(TimerFallbackCommand command)
        : base(command)
    {
        this._command = command;
        this.Title = Strings.Action_StartTimer;
        this.Icon = Icons.NewTimer;
        this.FallbackHandler = this;
    }

    public void UpdateQuery(string query) => this.UpdateQuery(query, DateTimeOffset.UtcNow, TimeZoneInfo.Local);

    internal void UpdateQuery(string query, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        this._command.UpdateQueryText(query);
        var utcNow = now.ToUniversalTime();
        this.HasCreationQuery = TimerCreationQueryParser.TryParse(query, utcNow, timeZone, out var creation)
                                && TimerInstance.IsValidDuration(creation.Duration, utcNow);
        if (this.HasCreationQuery)
        {
            this.Title = query.Trim();
            var start = TimeZoneInfo.ConvertTime(utcNow, timeZone);
            var showSeconds = creation.Duration < TimeSpan.FromMinutes(1);
            string preview;
            if (creation.EndTime.HasValue)
            {
                preview = showSeconds
                    ? DurationParser.FormatCompact(creation.Duration)
                    : DurationParser.FormatCountdown(creation.Duration, false);
            }
            else
            {
                preview = LabelFormatter.FormatEndTime(utcNow + creation.Duration, utcNow, timeZone, showSeconds);
            }

            var startTime = start.ToString(showSeconds ? "T" : "t", CultureInfo.CurrentCulture);
            var timing = ResourceFormatter.Format(Strings.Preview_IfStartedAtFormat, preview, startTime);
            this.Subtitle = ResourceFormatter.Format(Strings.Preview_TimerActionAndTimingFormat, FormatAction(creation),
                timing);
            return;
        }

        this.Title = Strings.Fallback_TypeDurationTitle;
        this.Subtitle = string.Empty;
    }

    private static string FormatAction(TimerCreationQuery creation)
    {
        var formattedTime = creation.EndTime is { } endTime
            ? endTime.ToString(endTime.Second == 0 ? "t" : "T", CultureInfo.CurrentCulture)
            : DurationParser.FormatCompact(creation.Duration);

        if (string.Equals(creation.TimeInput, formattedTime, StringComparison.Ordinal))
        {
            return Strings.Action_StartTimer;
        }

        var actionFormat = creation.EndTime.HasValue
            ? Strings.Fallback_StartTimerUntilFormat
            : Strings.Fallback_StartTimerForFormat;
        return ResourceFormatter.Format(actionFormat, formattedTime);
    }
}

internal sealed partial class StopwatchFallbackCommand : InvokableCommand
{
    private readonly StopwatchScheduler _scheduler;
    private string? _name;

    public StopwatchFallbackCommand(StopwatchScheduler scheduler)
    {
        this._scheduler = scheduler;
        this.Id = CommandIds.StopwatchFallback;
        this.Name = Strings.Action_StartStopwatch;
        this.Icon = Icons.NewStopwatch;
    }

    public void UpdateName(string? name) => this._name = name;

    public override CommandResult Invoke()
    {
        var name = this._name;
        if (name is null)
        {
            return CommandResult.KeepOpen();
        }

        this._scheduler.CreateAndStart(name.Length == 0 ? null : name);
        return CommandResult.ShowToast(new ToastArgs
        {
            Message = Strings.Fallback_StartedStopwatch, Result = CommandResult.Dismiss()
        });
    }
}

internal sealed partial class StopwatchFallbackCommandItem : CommandItem, IFallbackCommandItem, IFallbackHandler
{
    private readonly StopwatchFallbackCommand _command;
    private readonly bool _requirePrefix;

    public IFallbackHandler FallbackHandler { get; set; }
    public string DisplayTitle => Strings.Action_StartStopwatch;

    public StopwatchFallbackCommandItem(StopwatchScheduler scheduler, bool requirePrefix = false)
        : this(new StopwatchFallbackCommand(scheduler), requirePrefix)
    {
    }

    private StopwatchFallbackCommandItem(StopwatchFallbackCommand command, bool requirePrefix)
        : base(command)
    {
        this._command = command;
        this._requirePrefix = requirePrefix;
        this.Icon = Icons.NewStopwatch;
        this.FallbackHandler = this;
        this.UpdateQuery(string.Empty);
    }

    public void UpdateQuery(string query)
    {
        var trimmedQuery = query.Trim();
        var name = this._requirePrefix
            ? GetExplicitName(trimmedQuery)
            : DurationParser.TryParse(trimmedQuery, out _)
                ? string.Empty
                : trimmedQuery;

        this._command.UpdateName(name);
        // The host uses the command name when the result title is empty.
        this._command.Name = name is null ? string.Empty : Strings.Action_StartStopwatch;
        this.Title = name switch
        {
            null => string.Empty,
            "" => Strings.Action_StartStopwatch,
            _ => ResourceFormatter.Format(Strings.Fallback_StartStopwatchNamedFormat, name)
        };
    }

    private static string? GetExplicitName(string query)
    {
        foreach (var prefix in Strings.Parsing_StopwatchPrefixes.Split('|',
                     StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (query.Length > prefix.Length
                && char.IsWhiteSpace(query[prefix.Length])
                && query.StartsWith(prefix, true, Strings.Culture ?? CultureInfo.CurrentUICulture))
            {
                return query[prefix.Length..].Trim();
            }
        }

        return null;
    }
}