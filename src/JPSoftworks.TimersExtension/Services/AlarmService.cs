// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Diagnostics;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Xml.Dom;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;
using Windows.UI.Notifications;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Resources;

namespace JPSoftworks.TimersExtension.Services;

public sealed partial class AlarmService : IAsyncDisposable, IDisposable
{
    public event Action<AlarmResult>? AlarmClosed;
    internal event EventHandler? RingingAlarmChanged;

    private readonly DispatcherQueueController _dispatcherController;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Action<ToastNotification> _showToast;
    private readonly Action<Guid> _removeToast;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, AlarmRequest> _requests = [];
    private MediaPlayer? _player;
    private MediaPlayer? _previewPlayer;
    private Guid? _currentAudioAlarmId;
    private bool _disposed;
    public static TimeSpan SnoozeDuration { get; } = TimeSpan.FromMinutes(5);

    public bool IsPlaying { get; private set; }

    public AlarmService()
        : this(static toast => ToastNotificationManager.CreateToastNotifier().Show(toast),
            static id => ToastNotificationManager.History.Remove(id.ToString(), "timers"))
    {
    }

    internal AlarmService(Action<ToastNotification> showToast, Action<Guid> removeToast)
    {
        this._showToast = showToast;
        this._removeToast = removeToast;
        this._dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
        this._dispatcherQueue = this._dispatcherController.DispatcherQueue;
    }

    public void ShowAlarm(AlarmRequest request)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);

        if (!request.IsMissed)
        {
            lock (this._gate)
            {
                this._requests[request.AlarmId] = request;
                if (!request.IsSilent)
                {
                    this._currentAudioAlarmId = request.AlarmId;
                }
            }

            if (!request.IsSilent)
            {
                this.QueueAlarmAudio(request);
                NotificationQueue.Raise(this.RingingAlarmChanged, this);
            }
        }

        this.ShowToast(request);
    }

    private void QueueAlarmAudio(AlarmRequest request)
    {
        var audioSource = request.SoundUri ?? AlarmSoundCatalog.GetSoundUri(AlarmSoundCatalog.FallbackFileName);

        this._dispatcherQueue.TryEnqueue(() =>
        {
            lock (this._gate)
            {
                if (this._currentAudioAlarmId != request.AlarmId || !this._requests.ContainsKey(request.AlarmId))
                {
                    return;
                }
            }

            this.StopPreviewPlayerCore();
            this.StopPlayerCore();

            this._player = new MediaPlayer
            {
                Source = MediaSource.CreateFromUri(new Uri(audioSource)),
                IsLoopingEnabled = true,
                Volume = Math.Clamp(request.Volume, 0.0, 1.0)
            };

            this._player.MediaFailed += this.OnMediaFailed;
            this._player.Play();
            this.IsPlaying = true;
        });
    }

    public void PreviewSound(string soundUri, double volume = 1.0)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);

        lock (this._gate)
        {
            if (this._currentAudioAlarmId is not null)
            {
                return;
            }
        }

        this._dispatcherQueue.TryEnqueue(() =>
        {
            this.StopPreviewPlayerCore();

            this._previewPlayer = new MediaPlayer
            {
                Source = MediaSource.CreateFromUri(new Uri(soundUri)),
                IsLoopingEnabled = false,
                Volume = Math.Clamp(volume, 0.0, 1.0)
            };

            this._previewPlayer.MediaEnded += this.OnPreviewEnded;
            this._previewPlayer.MediaFailed += this.OnPreviewFailed;
            this._previewPlayer.Play();
        });
    }

    private void ShowToast(AlarmRequest request)
    {
        var toast = CreateAlarmToast(request);
        toast.Tag = request.AlarmId.ToString();
        toast.Group = "timers";
        toast.ExpiresOnReboot = true;
        if (!request.IsMissed)
        {
            toast.Dismissed += this.OnToastDismissed;
            toast.Activated += this.OnToastActivated;
            toast.Failed += this.OnToastFailed;
        }

        this._showToast(toast);

        if (!request.IsMissed)
        {
            lock (this._gate)
            {
                if (this._requests.ContainsKey(request.AlarmId))
                {
                    return;
                }
            }

            // The alarm can be dismissed while Windows is displaying its notification.
            NotificationQueue.InvokeSafely(() => this._removeToast(request.AlarmId));
        }
    }

    internal static ToastNotification CreateAlarmToast(AlarmRequest request)
    {
        if (request.IsMissed)
        {
            return CreateToast(request.Title, request.Message);
        }

        var stopLabel = SecurityElement.Escape(Strings.Action_Stop);
        var snoozeLabel = SecurityElement.Escape(Strings.Action_SnoozeFiveMinutes);
        var restartLabel = SecurityElement.Escape(Strings.Action_Restart);
        var actions = $"""
                       <actions>
                           <action content="{snoozeLabel}" arguments="action=snooze"/>
                           <action content="{stopLabel}" arguments="action=stop"/>
                           <action content="{restartLabel}" arguments="action=restart"/>
                       </actions>
                       """;

        return CreateToast(request.Title, request.Message, " duration=\"long\" scenario=\"alarm\"", actions);
    }

    internal static void ShowNotice(string title, string message) =>
        ToastNotificationManager.CreateToastNotifier().Show(CreateToast(title, message));

    private static ToastNotification CreateToast(
        string title,
        string message,
        string attributes = "",
        string actions = "")
    {
        var xml = $"""
                   <toast{attributes}>
                       <visual>
                           <binding template="ToastGeneric">
                               <text>{SecurityElement.Escape(title)}</text>
                               <text>{SecurityElement.Escape(message)}</text>
                           </binding>
                       </visual>
                       {actions}
                       <audio silent="true"/>
                   </toast>
                   """;

        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return new ToastNotification(doc);
    }

    private void OnToastDismissed(ToastNotification sender, ToastDismissedEventArgs args)
    {
        var alarmId = Guid.TryParse(sender.Tag, out var id) ? id : Guid.Empty;
        this.StopAlarmPlayback(alarmId);
        var kind = args.Reason switch
        {
            ToastDismissalReason.UserCanceled => AlarmClosedKind.Dismissed,
            ToastDismissalReason.TimedOut => AlarmClosedKind.TimedOut,
            _ => AlarmClosedKind.Dismissed
        };

        this.AlarmClosed?.Invoke(this.CreateResult(alarmId, kind));
    }

    private void OnToastActivated(ToastNotification sender, object args)
    {
        var alarmId = Guid.TryParse(sender.Tag, out var id) ? id : Guid.Empty;
        this.StopAlarmPlayback(alarmId);

        if (args is ToastActivatedEventArgs activated)
        {
            this.AlarmClosed?.Invoke(this.CreateResult(alarmId, GetActivationKind(activated.Arguments)));
            return;
        }

        this.AlarmClosed?.Invoke(this.CreateResult(alarmId, AlarmClosedKind.Stopped));
    }

    private void OnToastFailed(ToastNotification sender, ToastFailedEventArgs args)
    {
        var alarmId = Guid.TryParse(sender.Tag, out var id) ? id : Guid.Empty;
        this.StopAlarmPlayback(alarmId);
        this.AlarmClosed?.Invoke(this.CreateResult(alarmId, AlarmClosedKind.Failed));
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        Debug.WriteLine($"Media failed: {args.ErrorMessage}");
    }

    private void OnPreviewEnded(MediaPlayer sender, object args)
    {
        this._dispatcherQueue.TryEnqueue(this.StopPreviewPlayerCore);
    }

    private void OnPreviewFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        Debug.WriteLine($"Preview media failed: {args.ErrorMessage}");
        this._dispatcherQueue.TryEnqueue(this.StopPreviewPlayerCore);
    }

    public void StopAlarm()
    {
        bool wasRinging;
        lock (this._gate)
        {
            wasRinging = this._currentAudioAlarmId is not null;
            this._currentAudioAlarmId = null;
            this._dispatcherQueue.TryEnqueue(this.StopPlayerCore);
        }

        if (wasRinging)
        {
            NotificationQueue.Raise(this.RingingAlarmChanged, this);
        }
    }

    internal void DismissStaleAlarms(TimerScheduler scheduler)
    {
        AlarmRequest[] requests;
        lock (this._gate)
        {
            requests = [.. this._requests.Values];
        }

        foreach (var request in requests)
        {
            if (!scheduler.IsCurrentCompletion(request.TimerId, request.FinishedAt))
            {
                this.DismissAlarm(request.AlarmId);
            }
        }
    }

    internal AlarmRequest? GetRingingAlarm()
    {
        lock (this._gate)
        {
            return this._currentAudioAlarmId is { } current ? this._requests.GetValueOrDefault(current) : null;
        }
    }

    internal bool TryDismissRingingAlarm()
    {
        if (this.GetRingingAlarm() is not { } alarm)
        {
            return false;
        }

        this.DismissAlarm(alarm.AlarmId);
        return true;
    }

    internal void DismissAlarm(Guid alarmId)
    {
        lock (this._gate)
        {
            this._requests.Remove(alarmId);
        }

        this.StopAlarmPlayback(alarmId);
        NotificationQueue.InvokeSafely(() => this._removeToast(alarmId));
    }

    private void StopAlarmPlayback(Guid alarmId)
    {
        if (alarmId == Guid.Empty)
        {
            return;
        }

        lock (this._gate)
        {
            if (this._currentAudioAlarmId != alarmId)
            {
                return;
            }

            this._currentAudioAlarmId = null;
            this._dispatcherQueue.TryEnqueue(this.StopPlayerCore);
        }

        NotificationQueue.Raise(this.RingingAlarmChanged, this);
    }

    private void StopPlayerCore()
    {
        if (this._player is { } player)
        {
            player.MediaFailed -= this.OnMediaFailed;
            player.Pause();
            player.Dispose();
            this._player = null;
            this.IsPlaying = false;
        }
    }

    private void StopPreviewPlayerCore()
    {
        if (this._previewPlayer is { } player)
        {
            player.MediaEnded -= this.OnPreviewEnded;
            player.MediaFailed -= this.OnPreviewFailed;
            player.Pause();
            player.Dispose();
            this._previewPlayer = null;
        }
    }

    private AlarmResult CreateResult(Guid alarmId, AlarmClosedKind kind)
    {
        AlarmRequest? request;
        lock (this._gate)
        {
            this._requests.Remove(alarmId, out request);
        }

        return request?.ToResult(kind) ?? new AlarmResult(Guid.Empty, TimeSpan.Zero, kind);
    }

    private static Dictionary<string, string> ParseArguments(string args)
    {
        return args
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
    }

    internal static AlarmClosedKind GetActivationKind(string arguments)
    {
        var parsedArguments = ParseArguments(arguments);
        return parsedArguments.TryGetValue("action", out var action)
            ? action.ToLowerInvariant() switch
            {
                "restart" => AlarmClosedKind.Restart,
                "snooze" => AlarmClosedKind.Snooze,
                _ => AlarmClosedKind.Stopped
            }
            : AlarmClosedKind.Stopped;
    }

    public async ValueTask DisposeAsync()
    {
        if (this._disposed) return;
        this._disposed = true;

        lock (this._gate)
        {
            this._currentAudioAlarmId = null;
            this._requests.Clear();
        }

        this._dispatcherQueue.TryEnqueue(this.StopPlayerCore);
        this._dispatcherQueue.TryEnqueue(this.StopPreviewPlayerCore);
        await this._dispatcherController.ShutdownQueueAsync();
    }

    public void Dispose()
    {
        if (this._disposed) return;
        this._disposed = true;

        lock (this._gate)
        {
            this._currentAudioAlarmId = null;
            this._requests.Clear();
        }

        this._dispatcherQueue.TryEnqueue(this.StopPlayerCore);
        this._dispatcherQueue.TryEnqueue(this.StopPreviewPlayerCore);
        this._dispatcherController.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
    }
}