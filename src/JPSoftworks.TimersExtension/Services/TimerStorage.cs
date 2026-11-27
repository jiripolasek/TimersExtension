// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Serialization;

namespace JPSoftworks.TimersExtension.Services;

public sealed partial class TimerStorage : IDisposable
{
    private const string TemplatesFileName = "templates.json";

    private readonly string _baseDirectory;
    private readonly JsonStorageService<TimerInstance> _timersStorage;
    private readonly JsonStorageService<TimerDefinition> _templatesStorage;

    internal bool TimersAreReadOnly => this._timersStorage.IsReadOnly;
    internal bool IsReadOnly => this.TimersAreReadOnly || this._templatesStorage.IsReadOnly;

    public TimerStorage(string baseDirectory)
    {
        this._baseDirectory = baseDirectory;
        this._timersStorage = new JsonStorageService<TimerInstance>(
            Path.Combine(baseDirectory, "timers.json"),
            TimerJsonContext.Default.TimerInstanceArray);

        this._templatesStorage = new JsonStorageService<TimerDefinition>(
            Path.Combine(baseDirectory, TemplatesFileName),
            TimerJsonContext.Default.TimerDefinitionArray);
    }

    internal void InitializeStarterTemplates(string settingsPath)
    {
        if (this.IsReadOnly)
        {
            return;
        }

        var templatesPath = Path.Combine(this._baseDirectory, TemplatesFileName);
        try
        {
            // An empty templates file is saved when the last template is deleted.
            if (File.Exists(settingsPath) || Directory.EnumerateFileSystemEntries(this._baseDirectory, "*.json*").Any())
            {
                return;
            }

            var temporaryPath = Path.Combine(this._baseDirectory, $".starter-templates-{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew))
                {
                    JsonSerializer.Serialize(stream,
                    [
                        new TimerDefinition(string.Empty, TimeSpan.FromMinutes(10), true),
                        new TimerDefinition(string.Empty, TimeSpan.FromMinutes(30), true),
                        new TimerDefinition(string.Empty, TimeSpan.FromHours(1), true)
                    ], TimerJsonContext.Default.TimerDefinitionArray);
                }

                File.Move(temporaryPath, templatesPath, false);
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!File.Exists(templatesPath))
            {
                Trace.TraceWarning("Could not initialize starter timer templates: {0}", ex);
            }
        }
    }

    public (IReadOnlyList<TimerInstance> timers, IReadOnlyList<TimerDefinition> templates) Load()
    {
        return (this._timersStorage.Load(), this._templatesStorage.Load());
    }

    public IReadOnlyList<TimerInstance> LoadTimers() => this._timersStorage.Load();
    public IReadOnlyList<TimerDefinition> LoadTemplates() => this._templatesStorage.Load();

    public void SaveTimers(IReadOnlyList<TimerInstance> timers) => this._timersStorage.Save(timers);
    public void SaveTemplates(IReadOnlyList<TimerDefinition> templates) => this._templatesStorage.Save(templates);

    public void Dispose()
    {
        List<Exception>? errors = null;
        try
        {
            this._timersStorage.Dispose();
        }
        catch (Exception ex)
        {
            (errors ??= []).Add(ex);
        }

        try
        {
            this._templatesStorage.Dispose();
        }
        catch (Exception ex)
        {
            (errors ??= []).Add(ex);
        }

        if (errors is not null)
        {
            throw new AggregateException("One or more timer stores failed to dispose.", errors);
        }
    }
}