namespace JPSoftworks.TimersExtension.Models;

public sealed record AlarmRequest
{
    public required Guid TimerId { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public required TimeSpan OriginalDuration { get; init; }
    public string? SoundUri { get; init; }
    public double Volume { get; init; } = 1.0;

    public static AlarmRequest FromTimer(TimerInstance timer, string? soundUri = null, double volume = 1.0)
    {
        var name = string.IsNullOrWhiteSpace(timer.Name) ? "Timer" : timer.Name;
        var finishedAt = DateTimeOffset.Now;

        return new AlarmRequest
        {
            TimerId = timer.Id,
            Title = $"{name} done",
            Message = $"""
                       {FormatDuration(timer.Duration)}
                       Finished at {finishedAt:t}
                       """,
            OriginalDuration = timer.Duration,
            SoundUri = soundUri,
            Volume = volume
        };
    }

    private static string FormatDuration(TimeSpan duration) => duration switch
    {
        { TotalHours: >= 1 } => $"{(int)duration.TotalHours}h {duration.Minutes}m",
        { TotalMinutes: >= 1 } => $"{(int)duration.TotalMinutes}m",
        _ => $"{(int)duration.TotalSeconds}s"
    };
}