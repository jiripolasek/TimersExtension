using System.IO;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Serialization;

namespace JPSoftworks.TimersExtension.Services;

public sealed class StopwatchStorage
{
    private readonly JsonStorageService<StopwatchInstance> _storage;

    public StopwatchStorage(string baseDirectory)
    {
        _storage = new JsonStorageService<StopwatchInstance>(
            Path.Combine(baseDirectory, "stopwatches.json"),
            StopwatchJsonContext.Default.StopwatchInstanceArray);
    }

    public IReadOnlyList<StopwatchInstance> Load() => _storage.Load();
    public void Save(IReadOnlyList<StopwatchInstance> stopwatches) => _storage.Save(stopwatches);
}