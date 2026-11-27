// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace JPSoftworks.TimersExtension.Services;

public sealed partial class JsonStorageService<T> : IStorageService<T>, IDisposable
{
    private const int SharingViolation = 32;
    private const int LockViolation = 33;

    private readonly string _filePath;
    private readonly string _backupFilePath;
    private readonly string _tempFilePath;
    private readonly JsonTypeInfo<T[]> _typeInfo;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly Lock _persistGate = new();
    private Task _persistTask = Task.CompletedTask;
    private volatile bool _isReadOnly;
    private bool _disposed;

    internal bool IsReadOnly => this._isReadOnly;

    public JsonStorageService(string filePath, JsonTypeInfo<T[]> typeInfo)
    {
        this._filePath = filePath;
        this._backupFilePath = $"{filePath}.bak";
        this._tempFilePath = $"{filePath}.tmp";
        this._typeInfo = typeInfo;

        var directory = Path.GetDirectoryName(filePath);
        try
        {
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.MarkReadOnly(filePath, ex);
        }
    }

    public IReadOnlyList<T> Load()
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
        this._mutex.Wait();
        try
        {
            return this.TryLoad(this._filePath) ?? this.TryLoad(this._backupFilePath) ?? [];
        }
        finally
        {
            this._mutex.Release();
        }
    }

    public void Save(IReadOnlyList<T> items)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
        if (this._isReadOnly)
        {
            return;
        }

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

        this.EnqueuePersist(() => this.WriteWithLockAsync(snapshot));
    }

    private void EnqueuePersist(Func<Task> work)
    {
        lock (this._persistGate)
        {
            ObjectDisposedException.ThrowIf(this._disposed, this);
            this._persistTask = this._persistTask
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

            _ = this._persistTask.ContinueWith(
                task => Trace.TraceError("Failed to save '{0}': {1}", this._filePath,
                    task.Exception!.GetBaseException()),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
    }

    private async Task WriteWithLockAsync(T[] value)
    {
        await this._mutex.WaitAsync().ConfigureAwait(false);
        try
        {
            if (this._isReadOnly)
            {
                return;
            }

            try
            {
                await using (var stream = File.Create(this._tempFilePath))
                {
                    await JsonSerializer.SerializeAsync(stream, value, this._typeInfo).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }

                if (File.Exists(this._filePath))
                {
                    File.Replace(this._tempFilePath, this._filePath, this._backupFilePath, true);
                }
                else
                {
                    File.Move(this._tempFilePath, this._filePath);
                }
            }
            finally
            {
                if (File.Exists(this._tempFilePath))
                {
                    File.Delete(this._tempFilePath);
                }
            }
        }
        finally
        {
            this._mutex.Release();
        }
    }

    private T[]? TryLoad(string path)
    {
        try
        {
            using var stream = OpenReadWithRetry(path);
            return JsonSerializer.Deserialize(stream, this._typeInfo);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or JsonException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.MarkReadOnly(path, ex);
            return null;
        }
    }

    private void MarkReadOnly(string path, Exception exception)
    {
        // The caller may still hold backup data after the primary file becomes readable again.
        this._isReadOnly = true;
        Trace.TraceError("Storage is read-only for this session after an error accessing '{0}': {1}", path, exception);
    }

    private static FileStream OpenReadWithRetry(string path)
    {
        try
        {
            return File.OpenRead(path);
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is SharingViolation or LockViolation)
        {
            // A backup or sync tool can briefly hold the primary file during startup.
            Thread.Sleep(100);
            return File.OpenRead(path);
        }
    }

    public void Dispose()
    {
        Task persistTask;
        lock (this._persistGate)
        {
            if (this._disposed)
            {
                return;
            }

            this._disposed = true;
            persistTask = this._persistTask;
        }

        try
        {
            persistTask.GetAwaiter().GetResult();
        }
        finally
        {
            this._mutex.Dispose();
        }
    }
}