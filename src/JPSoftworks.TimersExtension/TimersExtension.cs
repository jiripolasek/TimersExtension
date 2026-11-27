// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Threading;
using JPSoftworks.TimersExtension.Resources;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension;

[Guid("668718db-b6d3-4818-86f0-12ed8e5780cb")]
public sealed partial class TimersExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;

    private readonly TimersExtensionCommandsProvider _provider = new();
    private bool _disposed;

    public TimersExtension(ManualResetEvent extensionDisposedEvent)
    {
        this._extensionDisposedEvent = extensionDisposedEvent;
        TimerServices.Storage.InitializeStarterTemplates(TimerServices.Settings.FilePath);
        TimerServices.Scheduler.Initialize();
        TimerServices.StopwatchScheduler.Initialize();
        if (TimerServices.Storage.IsReadOnly || TimerServices.StopwatchStorage.IsReadOnly)
        {
            var message = TimerServices.Storage.TimersAreReadOnly
                ? Strings.Storage_ReadOnlyTimersMessage
                : Strings.Storage_ReadOnlyMessage;
            NotificationQueue.InvokeSafely(() => AlarmService.ShowNotice(Strings.Storage_ReadOnlyTitle, message));
        }
    }

    public object? GetProvider(ProviderType providerType)
    {
        return providerType switch
        {
            ProviderType.Commands => this._provider,
            _ => null
        };
    }

    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;
        List<Exception>? errors = null;
        try
        {
            this._provider.Dispose();
        }
        catch (Exception ex)
        {
            (errors ??= []).Add(ex);
        }

        try
        {
            TimerServices.Dispose();
        }
        catch (Exception ex)
        {
            (errors ??= []).Add(ex);
        }
        finally
        {
            this._extensionDisposedEvent.Set();
        }

        if (errors is not null)
        {
            throw new AggregateException("The timers extension failed to dispose cleanly.", errors);
        }
    }
}