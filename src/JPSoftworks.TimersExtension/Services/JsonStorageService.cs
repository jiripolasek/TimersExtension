using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace JPSoftworks.TimersExtension.Services;

public sealed class JsonStorageService<T> : IStorageService<T>
{
    private readonly string _filePath;
    private readonly JsonTypeInfo<T[]> _typeInfo;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly Lock _persistGate = new();
    private Task _persistTask = Task.CompletedTask;

    public JsonStorageService(string filePath, JsonTypeInfo<T[]> typeInfo)
    {
        _filePath = filePath;
        _typeInfo = typeInfo;

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public IReadOnlyList<T> Load()
    {
        _mutex.Wait();
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            using var stream = File.OpenRead(_filePath);
            return JsonSerializer.Deserialize(stream, _typeInfo) ?? [];
        }
        catch
        {
            return [];
        }
        finally
        {
            _mutex.Release();
        }
    }

    public void Save(IReadOnlyList<T> items)
    {
        T[] snapshot;
        if (items is T[] array)
        {
            snapshot = new T[array.Length];
            array.CopyTo(snapshot, 0);
        }
        else
        {
            snapshot = [.. items];
        }

        EnqueuePersist(() => WriteWithLockAsync(snapshot));
    }

    private void EnqueuePersist(Func<Task> work)
    {
        lock (_persistGate)
        {
            _persistTask = _persistTask
                .ContinueWith(
                    async t =>
                    {
                        _ = t.Exception;
                        await work().ConfigureAwait(false);
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default)
                .Unwrap();

            _ = _persistTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    private async Task WriteWithLockAsync(T[] value)
    {
        await _mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var stream = File.Create(_filePath);
            await JsonSerializer.SerializeAsync(stream, value, _typeInfo).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }
}