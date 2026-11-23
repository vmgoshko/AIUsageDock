using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AIUsageDock.Commands;

internal sealed partial class AccountActionCommand : InvokableCommand
{
    private readonly Func<string> _action;
    public AccountActionCommand(string id, string name, Func<string> action)
    {
        Id = id;
        Name = name;
        _action = action;
    }
    public override ICommandResult Invoke()
    {
        try { return CommandResult.ShowToast(_action()); }
        catch (Exception exception) { return CommandResult.ShowToast(exception.Message); }
    }
}
