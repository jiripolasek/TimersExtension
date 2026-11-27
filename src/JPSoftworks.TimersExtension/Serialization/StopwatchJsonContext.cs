// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Text.Json.Serialization;
using JPSoftworks.TimersExtension.Models;

namespace JPSoftworks.TimersExtension.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(StopwatchInstance[]))]
[JsonSerializable(typeof(LapRecord[]))]
public partial class StopwatchJsonContext : JsonSerializerContext
{
}