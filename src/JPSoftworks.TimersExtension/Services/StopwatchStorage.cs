// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.IO;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Serialization;

namespace JPSoftworks.TimersExtension.Services;

public sealed partial class StopwatchStorage : IDisposable
{
    private readonly JsonStorageService<StopwatchInstance> _storage;

    internal bool IsReadOnly => this._storage.IsReadOnly;

    public StopwatchStorage(string baseDirectory)
    {
        this._storage = new JsonStorageService<StopwatchInstance>(
            Path.Combine(baseDirectory, "stopwatches.json"),
            StopwatchJsonContext.Default.StopwatchInstanceArray);
    }

    public IReadOnlyList<StopwatchInstance> Load() => this._storage.Load();
    public void Save(IReadOnlyList<StopwatchInstance> stopwatches) => this._storage.Save(stopwatches);

    public void Dispose() => this._storage.Dispose();
}