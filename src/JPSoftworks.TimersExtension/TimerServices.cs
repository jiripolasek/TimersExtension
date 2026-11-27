// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.IO;
using System.Threading;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension;

internal static class TimerServices
{
    private static readonly Lock Gate = new();
    private static SettingsManager? _settings;
    private static TimerStorage? _storage;
    private static TimerScheduler? _scheduler;
    private static StopwatchStorage? _stopwatchStorage;
    private static StopwatchScheduler? _stopwatchScheduler;
    private static AlarmService? _alarmService;

    public static SettingsManager Settings
    {
        get
        {
            EnsureInitialized();
            return _settings!;
        }
    }

    public static TimerStorage Storage
    {
        get
        {
            EnsureInitialized();
            return _storage!;
        }
    }

    public static TimerScheduler Scheduler
    {
        get
        {
            EnsureInitialized();
            return _scheduler!;
        }
    }

    public static StopwatchStorage StopwatchStorage
    {
        get
        {
            EnsureInitialized();
            return _stopwatchStorage!;
        }
    }

    public static StopwatchScheduler StopwatchScheduler
    {
        get
        {
            EnsureInitialized();
            return _stopwatchScheduler!;
        }
    }

    public static AlarmService AlarmService
    {
        get
        {
            EnsureInitialized();
            return _alarmService!;
        }
    }

    private static void EnsureInitialized()
    {
        lock (Gate)
        {
            if (_settings is not null)
            {
                return;
            }

            var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PowerToys", "CommandPalette", "Timers");
            _settings = new SettingsManager();
            _storage = new TimerStorage(baseDir);
            _scheduler = new TimerScheduler(_storage);
            _stopwatchStorage = new StopwatchStorage(baseDir);
            _stopwatchScheduler = new StopwatchScheduler(_stopwatchStorage);
            _alarmService = new AlarmService();
            _settings.AlarmPreviewRequested += Settings_AlarmPreviewRequested;
        }
    }

    private static void Settings_AlarmPreviewRequested(object? sender, EventArgs e)
    {
        lock (Gate)
        {
            if (_settings?.AlarmSoundUri is { } soundUri && _alarmService is not null)
            {
                _alarmService.PreviewSound(soundUri, _settings.AlarmVolume);
            }
        }
    }

    public static void Dispose()
    {
        AlarmService? alarmService;
        StopwatchScheduler? stopwatchScheduler;
        TimerScheduler? scheduler;
        StopwatchStorage? stopwatchStorage;
        TimerStorage? storage;

        lock (Gate)
        {
            if (_settings is null)
            {
                return;
            }

            alarmService = _alarmService;
            stopwatchScheduler = _stopwatchScheduler;
            scheduler = _scheduler;
            stopwatchStorage = _stopwatchStorage;
            storage = _storage;

            _settings.AlarmPreviewRequested -= Settings_AlarmPreviewRequested;
            _alarmService = null;
            _stopwatchScheduler = null;
            _scheduler = null;
            _stopwatchStorage = null;
            _storage = null;
            _settings = null;
        }

        List<Exception>? errors = null;
        foreach (var service in new IDisposable?[]
                 {
                     alarmService, stopwatchScheduler, scheduler, stopwatchStorage, storage
                 })
        {
            try
            {
                service?.Dispose();
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
        }

        if (errors is not null)
        {
            throw new AggregateException("One or more timer services failed to dispose.", errors);
        }
    }
}