// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class DockAccessPage : ListPage
{
    private readonly ListItem[] _items;

    public DockAccessPage(string id, string title, IconInfo icon, ICommand destination)
    {
        this.Id = id;
        this.Title = title;
        this.Name = title;
        this.Icon = icon;
        this._items =
        [
            new ListItem(destination) { Title = title, Icon = icon }
        ];
    }

    public override IListItem[] GetItems() => this._items;

    public void UpdateMoreCommands(ICommandContextItem[] commands) =>
        this._items[0].MoreCommands = [new Separator(), .. commands];
}