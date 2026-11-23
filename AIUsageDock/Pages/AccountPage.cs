using AIUsageDock.Commands;
using AIUsageDock.Models;
using AIUsageDock.Providers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AIUsageDock.Pages;

internal sealed class AccountPage : ListPage
{
    private readonly CodexAccount _account;
    private readonly IUsageProvider _provider;
    private readonly UsagePage _usage;
    private readonly AccountCatalog _catalog;
    private readonly Func<Task>? _refreshBands;

    public AccountPage(CodexAccount account, IUsageProvider provider, AccountCatalog catalog, Func<Task>? refreshBands = null)
    {
        _account = account;
        _provider = provider;
        _catalog = catalog;
        _refreshBands = refreshBands;
        _usage = new UsagePage(provider);
        Id = $"codexusagedock.account.{account.Slot}";
        Name = Title = account.Name;
        Icon = ProviderIcons.Filled(provider);
    }

    public override IListItem[] GetItems()
    {
        var result = _provider.GetAsync().GetAwaiter().GetResult();
        async Task AfterLogin()
        {
            await _provider.GetAsync(force: true);
            if (_refreshBands is not null) await _refreshBands();
            RaiseItemsChanged();
        }
        var signedIn = result.Snapshot is not null || _provider is ManagedClaudeUsageProvider { IsSignedIn: true };
        IInvokableCommand loginCommand = _account.Service == "claude"
            ? new LoginClaudeCommand(_account.Slot, AfterLogin)
            : new LoginCodexCommand(_account.Slot, AfterLogin);
        var login = new ListItem(loginCommand)
        {
            Title = signedIn ? "Sign in to a different account" : _account.Service == "claude" ? "Sign in to Claude" : "Sign in with ChatGPT",
            Subtitle = $"Separate login for {_provider.DisplayName}",
            Icon = new IconInfo("\uE77B"),
        };
        var details = new ListItem(_usage)
        {
            Title = "Usage, history and reset times",
            Subtitle = result.Snapshot is { } snapshot
                ? $"5h: {snapshot.Session?.RemainingPercent:0}% left · week: {snapshot.Weekly?.RemainingPercent:0}% left"
                : result.Message ?? "Not signed in",
            Icon = Icon,
        };
        var refresh = new ListItem(new RefreshAllCommand(async () =>
        {
            await _provider.GetAsync(force: true);
            RaiseItemsChanged();
        })) { Title = "Refresh this account" };
        var remove = new ListItem(CreateUnbandCommand())
        { Title = "Unband and sign out", Subtitle = "Delete this band's saved login and profile" };
        var rename = new ListItem(new RenameBandPage(_catalog, _account.Slot)) { Title = "Rename band" };
        var items = signedIn ? new List<IListItem> { details, refresh, login, rename, remove }
            : new List<IListItem> { login, details, refresh, rename, remove };
        if (_account.Service == "claude" && signedIn)
            items.Insert(1, new ListItem(new LoginClaudeCommand(_account.Slot, AfterLogin, openUsage: true))
            { Title = "Open Claude Code for this band", Subtitle = "Run /usage, then exit Claude to refresh this band", Icon = Icon });
        return items.ToArray();
    }
    internal AccountActionCommand CreateUnbandCommand() =>
        new($"codexusagedock.remove.{_account.Slot}", $"Unband {_provider.DisplayName}", () =>
        {
            if (_provider is CodexUsageProvider codex) codex.LogoutAndRemoveAsync(_account.Slot).GetAwaiter().GetResult();
            if (_provider is ManagedClaudeUsageProvider claude) claude.LogoutAndRemoveAsync().GetAwaiter().GetResult();
            _catalog.Remove(_account.Slot);
            return "Signed out and band removed";
        });
    internal void UpdateName(string name) { Name = Title = name; RaiseItemsChanged(); }
}
