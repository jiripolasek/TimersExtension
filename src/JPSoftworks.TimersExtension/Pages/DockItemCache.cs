// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Linq;
using System.Threading;
using JPSoftworks.TimersExtension.Services;

namespace JPSoftworks.TimersExtension.Pages;

internal sealed class DockItemCache<T, TState>(
    Lock gate,
    NotificationQueue notifications,
    Func<T, Guid> getId,
    Func<T, TState> captureState,
    Func<T, TState, ListItem> createItem,
    Action<ListItem, T, TState, bool> updateItem)
    where T : class
{
    private readonly Dictionary<Guid, Entry> _entries = [];

    public IListItem[] Items { get; private set; } = [];

    public bool Sync(IReadOnlyList<T> models)
    {
        var items = new IListItem[models.Count];
        var visibleIds = new HashSet<Guid>(models.Count);
        var changed = this.Items.Length != items.Length;

        for (var i = 0; i < models.Count; i++)
        {
            var model = models[i];
            var id = getId(model);
            var state = captureState(model);
            visibleIds.Add(id);
            if (!this._entries.TryGetValue(id, out var entry) || !ReferenceEquals(entry.Model, model))
            {
                entry = new Entry(model, createItem(model, state), state);
                this._entries[id] = entry;
            }
            else
            {
                notifications.Enqueue(() => this.Publish(id, entry, state));
            }

            items[i] = entry.Item;
            changed = changed || !ReferenceEquals(items[i], this.Items[i]);
        }

        if (this._entries.Count != visibleIds.Count)
        {
            foreach (var id in this._entries.Keys.Where(id => !visibleIds.Contains(id)).ToArray())
            {
                this._entries.Remove(id);
            }
        }

        if (changed)
        {
            this.Items = items;
        }

        return changed;
    }

    public void Clear()
    {
        this._entries.Clear();
        this.Items = [];
    }

    private void Publish(Guid id, Entry entry, TState state)
    {
        bool updateMoreCommands;
        lock (gate)
        {
            if (this._entries.GetValueOrDefault(id) != entry)
            {
                return;
            }

            updateMoreCommands = !EqualityComparer<TState>.Default.Equals(entry.State, state);
        }

        updateItem(entry.Item, entry.Model, state, updateMoreCommands);
        lock (gate)
        {
            entry.State = state;
        }
    }

    private sealed class Entry(T model, ListItem item, TState state)
    {
        public T Model { get; } = model;
        public ListItem Item { get; } = item;
        public TState State { get; set; } = state;
    }
}