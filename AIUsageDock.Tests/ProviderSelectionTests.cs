using AIUsageDock.Models;
using AIUsageDock.Pages;
using Xunit;

namespace AIUsageDock.Tests;

public sealed class ProviderSelectionTests
{
    [Fact]
    public void RenamePersistsWithoutChangingProfileOrBandId()
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexRenameTests", Guid.NewGuid().ToString());
        try
        {
            var path = Path.Combine(root, "accounts.json");
            var catalog = new AccountCatalog(path);
            using var commands = new AIUsageDockCommandsProvider(catalog, false, account => new FakeUsageProvider(account.Id));
            var id = commands.GetDockBands()![0].Command.Id;
            var form = new RenameBandPage.RenameForm(catalog, 1);
            form.SubmitForm("""{"name":"Рабочий аккаунт"}""", "{}");
            Assert.Equal("Рабочий аккаунт", new AccountCatalog(path).Accounts[0].Name);
            Assert.Equal(id, commands.GetDockBands()![0].Command.Id);
            Assert.Equal("Рабочий аккаунт usage", commands.TopLevelCommands()[0].Title);
            Assert.Throws<ArgumentException>(() => catalog.Rename(1, "   "));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void UpgradePreservesLegacySlotsAndEmptyCatalogStaysEmpty()
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexMigrationTests", Guid.NewGuid().ToString());
        var path = Path.Combine(root, "accounts.json");
        try
        {
            var catalog = new AccountCatalog(path, preserveLegacyProfiles: true);
            Assert.Equal(new[] { "codex", "codex2" }, catalog.Accounts.Select(a => a.Id));
            catalog.Remove(1);
            catalog.Remove(2);
            var reloaded = new AccountCatalog(path, preserveLegacyProfiles: true);
            Assert.Empty(reloaded.Accounts);
            Assert.Equal(3, reloaded.Add().Slot);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ManyBandsPersistWithStableIdsAndRemovedSlotsAreNotReused()
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexCatalogTests", Guid.NewGuid().ToString());
        var path = Path.Combine(root, "accounts.json");
        try
        {
            var catalog = new AccountCatalog(path);
            var providers = new Dictionary<int, FakeUsageProvider>();
            using var commands = new AIUsageDockCommandsProvider(catalog, false, account =>
                providers[account.Slot] = new FakeUsageProvider(account.Id));
            for (var i = 0; i < 20; i++) catalog.Add();
            var bands = commands.GetDockBands()!;
            Assert.Equal(21, bands.Length);
            var ids = bands.Select(b => b.Command.Id).ToArray();
            Assert.Equal(21, ids.Distinct().Count());
            Assert.Equal(ids, commands.GetDockBands()!.Select(b => b.Command.Id));
            catalog.Remove(2);
            await commands.RefreshAsync(true);
            Assert.Equal(0, providers[2].Calls);
            Assert.Equal(1, providers[1].Calls);
            Assert.Equal(22, catalog.Add().Slot);
            var reloaded = new AccountCatalog(path);
            Assert.Equal(catalog.Accounts, reloaded.Accounts);
            Assert.DoesNotContain(reloaded.Accounts, a => a.Slot == 2);
            var page = new AccountPage(reloaded.Accounts[0], providers[1], reloaded);
            Assert.Equal("Sign in with ChatGPT", page.GetItems()[0].Title);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class FakeUsageProvider(string id) : IUsageProvider
    {
        public string Id => id;
        public string DisplayName => id;
        public string IconName => "codex";
        public int Calls { get; private set; }
        public Task<UsageResult> GetAsync(bool force = false, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(UsageResult.Failure(UsageFailureReason.NotSignedIn, "Not signed in"));
        }
        public void Dispose() { }
    }
}
