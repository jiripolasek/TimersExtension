// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class GroupHeaderListItem : ListItem
{
    public GroupHeaderListItem(string title)
    {
        this.Title = title;
        this.Section = title;
        this.Command = null!;
    }
}