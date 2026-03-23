using System.Linq;
using System.Security;
using System.Threading.Tasks;
using JPSoftworks.TimersExtension.Models;
using Windows.Data.Xml.Dom;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;
using Windows.UI.Notifications;

namespace JPSoftworks.TimersExtension.Services;

public sealed class AlarmService : IAsyncDisposable, IDisposable
{
    private readonly DispatcherQueueController _dispatcherController;
    private readonly DispatcherQueue _dispatcherQueue;
    private MediaPlayer? _player;
    private AlarmRequest? _currentRequest;
    private bool _disposed;

    public event Action<AlarmResult>? AlarmClosed;

    public AlarmService()
    {
        _dispatcherController = DispatcherQueueController.CreateOnDedicatedThread();
        _dispatcherQueue = _dispatcherController.DispatcherQueue;
    }

    public bool IsPlaying { get; private set; }

    public void ShowAlarm(AlarmRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _currentRequest = request;
        var audioSource = request.SoundUri ?? "ms-appx:///Assets/Sounds/chimes.wav";

        _dispatcherQueue.TryEnqueue(() =>
        {
            StopPlayerCore();

            _player = new MediaPlayer
            {
                Source = MediaSource.CreateFromUri(new Uri(audioSource)),
                IsLoopingEnabled = true,
                Volume = Math.Clamp(request.Volume, 0.0, 1.0)
            };

            _player.MediaFailed += OnMediaFailed;
            _player.Play();
            IsPlaying = true;
        });

        ShowToast(request);
    }

    private void ShowToast(AlarmRequest request)
    {
        var xml = $"""
            <toast duration="long" scenario="alarm">
                <visual>
                    <binding template="ToastGeneric">
                        <text>{SecurityElement.Escape(request.Title)}</text>
                        <text>{SecurityElement.Escape(request.Message)}</text>
                    </binding>
                </visual>
                <actions>
                    <action content="Stop" arguments="action=stop"/>
                    <action content="Restart" arguments="action=restart"/>
                </actions>
                <audio silent="true"/>
            </toast>
            """;

        var doc = new XmlDocument();
        doc.LoadXml(xml);

        var toast = new ToastNotification(doc)
        {
            Tag = request.TimerId.ToString(),
            Group = "timers",
            ExpiresOnReboot = true
        };

        toast.Dismissed += OnToastDismissed;
        toast.Activated += OnToastActivated;
        toast.Failed += OnToastFailed;

        ToastNotificationManager.CreateToastNotifier().Show(toast);
    }

    private void OnToastDismissed(ToastNotification sender, ToastDismissedEventArgs args)
    {
        StopAlarm();

        var timerId = Guid.TryParse(sender.Tag, out var id) ? id : Guid.Empty;
        var kind = args.Reason switch
        {
            ToastDismissalReason.UserCanceled => AlarmClosedKind.Dismissed,
            ToastDismissalReason.TimedOut => AlarmClosedKind.TimedOut,
            _ => AlarmClosedKind.Dismissed
        };

        AlarmClosed?.Invoke(CreateResult(timerId, kind));
    }

    private void OnToastActivated(ToastNotification sender, object args)
    {
        StopAlarm();

        var timerId = Guid.TryParse(sender.Tag, out var id) ? id : Guid.Empty;

        if (args is ToastActivatedEventArgs activated)
        {
            var arguments = ParseArguments(activated.Arguments);

            if (arguments.TryGetValue("action", out var action) && action == "restart")
            {
                AlarmClosed?.Invoke(CreateResult(timerId, AlarmClosedKind.Restart));
                return;
            }
        }

        AlarmClosed?.Invoke(CreateResult(timerId, AlarmClosedKind.Stopped));
    }

    private void OnToastFailed(ToastNotification sender, ToastFailedEventArgs args)
    {
        StopAlarm();
        var timerId = Guid.TryParse(sender.Tag, out var id) ? id : Guid.Empty;
        AlarmClosed?.Invoke(CreateResult(timerId, AlarmClosedKind.Failed));
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        System.Diagnostics.Debug.WriteLine($"Media failed: {args.ErrorMessage}");
    }

    public void StopAlarm()
    {
        _dispatcherQueue.TryEnqueue(StopPlayerCore);
    }

    private void StopPlayerCore()
    {
        if (_player is { } player)
        {
            player.MediaFailed -= OnMediaFailed;
            player.Pause();
            player.Dispose();
            _player = null;
            IsPlaying = false;
        }
    }

    private AlarmResult CreateResult(Guid timerId, AlarmClosedKind kind)
    {
        var duration = _currentRequest?.OriginalDuration ?? TimeSpan.Zero;
        _currentRequest = null;
        return new AlarmResult(timerId, duration, kind);
    }

    private static Dictionary<string, string> ParseArguments(string args)
    {
        return args
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _dispatcherQueue.TryEnqueue(StopPlayerCore);
        await _dispatcherController.ShutdownQueueAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _dispatcherQueue.TryEnqueue(StopPlayerCore);
        _dispatcherController.ShutdownQueueAsync().AsTask().GetAwaiter().GetResult();
    }
}