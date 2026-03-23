using System.IO;
using JPSoftworks.TimersExtension.Models;
using JPSoftworks.TimersExtension.Serialization;

namespace JPSoftworks.TimersExtension.Services;

public sealed class TimerStorage
{
    private readonly JsonStorageService<TimerInstance> _timersStorage;
    private readonly JsonStorageService<TimerDefinition> _templatesStorage;

    public TimerStorage(string baseDirectory)
    {
        _timersStorage = new JsonStorageService<TimerInstance>(
            Path.Combine(baseDirectory, "timers.json"),
            TimerJsonContext.Default.TimerInstanceArray);

        _templatesStorage = new JsonStorageService<TimerDefinition>(
            Path.Combine(baseDirectory, "templates.json"),
            TimerJsonContext.Default.TimerDefinitionArray);
    }

    public (IReadOnlyList<TimerInstance> timers, IReadOnlyList<TimerDefinition> templates) Load()
    {
        return (_timersStorage.Load(), _templatesStorage.Load());
    }

    public IReadOnlyList<TimerInstance> LoadTimers() => _timersStorage.Load();
    public IReadOnlyList<TimerDefinition> LoadTemplates() => _templatesStorage.Load();

    public void SaveTimers(IReadOnlyList<TimerInstance> timers) => _timersStorage.Save(timers);
    public void SaveTemplates(IReadOnlyList<TimerDefinition> templates) => _templatesStorage.Save(templates);
}