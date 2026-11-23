using AIUsageDock.Bands;
using AIUsageDock.Commands;
using AIUsageDock.Models;
using AIUsageDock.Pages;
using AIUsageDock.Providers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AIUsageDock;

public sealed partial class AIUsageDockCommandsProvider : CommandProvider, IDisposable
{
    private sealed record Entry(CodexAccount Account, IUsageProvider Provider, UsageBand Band, ICommandItem Command);
    private readonly AccountCatalog _catalog;
    private readonly Func<CodexAccount, IUsageProvider> _factory;
    private readonly Dictionary<int, Entry> _entries = new();
    private readonly object _gate = new();
    private readonly ICommandItem[] _utilities;
    private readonly Timer _timer;
    private int _refreshing;
    private bool _disposed;

    public AIUsageDockCommandsProvider() : this(new AccountCatalog(AccountCatalog.DefaultPath,
        preserveLegacyProfiles: Directory.Exists(CodexProfiles.Home(1)) || Directory.Exists(CodexProfiles.Home(2))), true) { }

    internal AIUsageDockCommandsProvider(AccountCatalog catalog, bool startTimer,
        Func<CodexAccount, IUsageProvider>? factory = null)
    {
        Id = "CodexUsageDock";
        DisplayName = "Codex Usage Dock";
        Icon = IconHelpers.FromRelativePath("Assets/Square44x44Logo.png");
        _catalog = catalog;
        _factory = factory ?? (account => account.Service == "claude"
            ? new ManagedClaudeUsageProvider(account)
            : new CodexUsageProvider(account.Id, account.Name, CodexProfiles.Home(account.Slot)));
        _utilities =
        [
            new CommandItem(new AccountActionCommand("codexusagedock.add", "Add Codex band", () =>
            {
                var account = _catalog.Add();
                return $"{account.Name} added. Pin it under Settings → Dock → Bands, then click it to sign in.";
            })) { Title = "Add Codex band", Subtitle = "Create a separate account band without signing in", Icon = new IconInfo("\uE710") },
            new CommandItem(new AccountActionCommand("codexusagedock.add.claude", "Add Claude band", () =>
            {
                var account = _catalog.Add("claude");
                return $"{account.Name} added. Pin it in Settings → Dock → Bands, then click it to sign in.";
            })) { Title = "Add Claude band", Subtitle = "Separate Claude login", Icon = new IconInfo("\uE710") },
            new CommandItem(new RefreshAllCommand(() => RefreshAsync(true))) { Title = "Refresh AI usage" },
        ];
        _catalog.Changed += OnChanged;
        Snapshot();
        _timer = new Timer(async _ => await RefreshAsync(false), null,
            startTimer ? TimeSpan.Zero : Timeout.InfiniteTimeSpan, TimeSpan.FromMinutes(1));
    }

    private Entry[] Snapshot()
    {
        lock (_gate)
        {
            if (_disposed) return [];
            return _catalog.Accounts.Select(account =>
            {
                if (_entries.TryGetValue(account.Slot, out var entry))
                {
                    if (entry.Account.Name != account.Name)
                    {
                        if (entry.Provider is CodexUsageProvider codex) codex.DisplayName = account.Name;
                        if (entry.Provider is ManagedClaudeUsageProvider claude) claude.DisplayName = account.Name;
                        if (entry.Command is CommandItem command) command.Title = $"{account.Name} usage";
                        if (entry.Command.Command is AccountPage existingPage) existingPage.UpdateName(account.Name);
                        entry.Band.UpdateName(account.Name);
                        entry = entry with { Account = account };
                        _entries[account.Slot] = entry;
                    }
                    return entry;
                }
                var provider = _factory(account);
                var page = new AccountPage(account, provider, _catalog, () => RefreshAsync(false));
                entry = new Entry(account, provider, new UsageBand(provider, page),
                    new CommandItem(page) { Title = $"{account.Name} usage", Icon = ProviderIcons.Filled(provider) });
                _entries.Add(account.Slot, entry);
                return entry;
            }).ToArray();
        }
    }

    private void OnChanged(object? sender, EventArgs args)
    {
        Snapshot();
        RaiseItemsChanged();
    }

    public override ICommandItem[] TopLevelCommands()
    {
        var entries = Snapshot();
        return entries.Select(e => e.Command).Concat(_utilities).Concat(entries.Select(e =>
            (ICommandItem)new CommandItem(((AccountPage)e.Command.Command).CreateUnbandCommand())
            { Title = $"Unband {e.Account.Name}", Subtitle = "Sign out and delete this band and its profile" })).ToArray();
    }
    public override ICommandItem[]? GetDockBands() => Snapshot().Select(e => (ICommandItem)e.Band.DockItem).ToArray();
    public override ICommandItem? GetCommandItem(string id) =>
        GetDockBands()!.Concat(TopLevelCommands()).FirstOrDefault(item => item.Command?.Id == id);

    internal async Task RefreshAsync(bool force)
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;
        try
        {
            // Bound simultaneous CLI processes regardless of the number of accounts.
            foreach (var entry in Snapshot())
            {
                if (_disposed) break;
                if (_catalog.Accounts.Any(a => a.Slot == entry.Account.Slot))
                    await entry.Band.RefreshAsync(force);
            }
        }
        finally { Volatile.Write(ref _refreshing, 0); }
    }

    public override void Dispose()
    {
        _timer.Dispose();
        _catalog.Changed -= OnChanged;
        // Let a pending timer read finish before disposing provider semaphores.
        lock (_gate) _disposed = true;
        base.Dispose();
    }
}
