// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace JPSoftworks.TimersExtension.Commands;

internal sealed partial class DeletionConfirmationCommand : InvokableCommand
{
    private readonly InvokableCommand _primaryCommand;
    private readonly string _title;
    private readonly string _description;
    private readonly Func<bool> _shouldConfirm;

    public DeletionConfirmationCommand(
        InvokableCommand primaryCommand,
        string title,
        string description,
        Func<bool>? shouldConfirm = null)
    {
        this._primaryCommand = primaryCommand;
        this._title = title;
        this._description = description;
        this._shouldConfirm = shouldConfirm ?? (() => TimerServices.Settings.ConfirmDeletion);
        this.Name = primaryCommand.Name;
        this.Icon = primaryCommand.Icon;
    }

    public override CommandResult Invoke()
    {
        if (!this._shouldConfirm())
        {
            return (CommandResult)this._primaryCommand.Invoke();
        }

        var confirmArgs = new ConfirmationArgs
        {
            PrimaryCommand = this._primaryCommand, Title = this._title, Description = this._description
        };

        return CommandResult.Confirm(confirmArgs);
    }
}