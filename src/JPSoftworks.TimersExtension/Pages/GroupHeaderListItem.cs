namespace JPSoftworks.TimersExtension.Pages;

internal sealed partial class GroupHeaderListItem : ListItem
{
    public GroupHeaderListItem(string title)
    {
        Title = title;
        Section = title;
        Command = null!;
    }
}