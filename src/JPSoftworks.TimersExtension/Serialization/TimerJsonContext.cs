using System.Text.Json.Serialization;
using JPSoftworks.TimersExtension.Models;

namespace JPSoftworks.TimersExtension.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TimerDefinition[]))]
[JsonSerializable(typeof(TimerInstance[]))]
public partial class TimerJsonContext : JsonSerializerContext
{
}
