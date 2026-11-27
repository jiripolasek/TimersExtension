// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Text.Json.Serialization;

namespace JPSoftworks.TimersExtension.Models;

public sealed record TimerDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public bool IsTemplate { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AlarmSoundId { get; init; }

    public TimerDefinition()
    {
    }

    public TimerDefinition(string name, TimeSpan duration, bool isTemplate = false)
    {
        this.Name = name;
        this.Duration = duration;
        this.IsTemplate = isTemplate;
    }
}