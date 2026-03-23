namespace JPSoftworks.TimersExtension.Models;

public sealed record TimerDefinition(string Name, TimeSpan Duration, bool IsTemplate = false);