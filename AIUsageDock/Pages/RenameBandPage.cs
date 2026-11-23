using System.Text.Json;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AIUsageDock.Pages;

internal sealed class RenameBandPage : ContentPage
{
    private readonly AccountCatalog _catalog;
    private readonly int _slot;
    public RenameBandPage(AccountCatalog catalog, int slot)
    {
        _catalog = catalog;
        _slot = slot;
        Id = $"codexusagedock.rename.{slot}";
        Name = Title = "Rename band";
    }
    public override IContent[] GetContent() => [new RenameForm(_catalog, _slot)];

    internal sealed partial class RenameForm : FormContent
    {
        private readonly AccountCatalog _catalog;
        private readonly int _slot;
        public RenameForm(AccountCatalog catalog, int slot)
        {
            _catalog = catalog;
            _slot = slot;
            var name = catalog.Accounts.First(a => a.Slot == slot).Name;
            TemplateJson = JsonSerializer.Serialize(new
            {
                type = "AdaptiveCard", version = "1.5",
                body = new object[] { new { type = "Input.Text", id = "name", label = "Band name", value = name, maxLength = 60 } },
                actions = new[] { new { type = "Action.Submit", title = "Save" } },
            });
            DataJson = "{}";
        }
        public override ICommandResult SubmitForm(string inputs, string data)
        {
            try
            {
                using var values = JsonDocument.Parse(inputs);
                _catalog.Rename(_slot, values.RootElement.GetProperty("name").GetString() ?? "");
                return CommandResult.ShowToast("Band renamed");
            }
            catch (Exception exception) { return CommandResult.ShowToast(exception.Message); }
        }
    }
}
