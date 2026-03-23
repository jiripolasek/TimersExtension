namespace JPSoftworks.TimersExtension.Models;

public record AlarmResult(Guid TimerId, TimeSpan OriginalDuration, AlarmClosedKind Kind);