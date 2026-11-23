using System.Diagnostics;
using System.Text;
using AIUsageDock.Commands;
using AIUsageDock.Providers;
using Xunit;

namespace AIUsageDock.Tests;

public sealed class ClaudeProfileTests
{
    [Fact]
    public void CatalogLoadsLegacyAccountsAndPersistsMixedServices()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeCatalogTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "accounts.json");
            File.WriteAllText(path, "{\"NextSlot\":2,\"Accounts\":[{\"Slot\":1,\"Label\":\"Existing\"}]}");
            var catalog = new AccountCatalog(path);
            Assert.Equal("codex", Assert.Single(catalog.Accounts).Service);
            var claude = catalog.Add("claude");
            catalog.Rename(claude.Slot, "Personal Claude");
            var loaded = new AccountCatalog(path);
            Assert.Equal("claude", loaded.Accounts[1].Service);
            Assert.Equal("Personal Claude", loaded.Accounts[1].Name);
            Assert.NotEqual(loaded.Accounts[0].Id, loaded.Accounts[1].Id);
            Assert.NotEqual(CodexProfiles.Home(1), CodexProfiles.Home(1, "claude"));
            Assert.Throws<ArgumentException>(() => loaded.Add("invalid"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void BackgroundStatusUsesOnlyItsProfileAndClearsAlternateCredentials()
    {
        var inherited = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var info = ClaudeProfileProcess.StartInfo("claude.exe", "isolated-profile", "auth", "status", "--json");
        Assert.Equal("isolated-profile", info.Environment["CLAUDE_CONFIG_DIR"]);
        Assert.Equal("isolated-profile", info.WorkingDirectory);
        Assert.Equal(new[] { "auth", "status", "--json" }, info.ArgumentList);
        Assert.All(ClaudeProfileProcess.ClearedVariables, v => Assert.False(info.Environment.ContainsKey(v)));
        Assert.Equal(Path.Combine("isolated-profile", "anthropic"), info.Environment["ANTHROPIC_CONFIG_DIR"]);
        Assert.Equal(inherited, Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"));
    }

    [Theory]
    [InlineData("claude.ai", true)]
    [InlineData("oauth_token", false)]
    [InlineData("api_key", false)]
    public void ExternalCredentialsDoNotAuthorizeBand(string method, bool expected)
    {
        using var status = System.Text.Json.JsonDocument.Parse($"{{\"loggedIn\":true,\"authMethod\":\"{method}\"}}");
        Assert.Equal(expected, ManagedClaudeUsageProvider.HasBandLogin(status.RootElement));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(7, false)]
    public async Task LoginCreatesProfileAndCallsClaudeAiAuthWithoutInheritedApiKey(int exitCode, bool success)
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeLogin-'" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var executable = Path.Combine(root, "fake-claude.cmd");
            await File.WriteAllTextAsync(executable,
                "@echo off\r\nif not exist \"%CLAUDE_CONFIG_DIR%\" exit /b 99\r\n" +
                "if defined ANTHROPIC_API_KEY exit /b 98\r\n" +
                "if not \"%*\"==\"auth login --claudeai\" exit /b 97\r\n" +
                $"exit /b {exitCode}\r\n");
            var script = LoginClaudeCommand.BuildScript(executable, Path.Combine(root, "new-profile"))
                .Replace("Read-Host 'Press Enter to close'", "");
            var info = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            info.Environment["ANTHROPIC_API_KEY"] = "test-inherited-key";
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-EncodedCommand");
            info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            var text = await output;
            Assert.True(Directory.Exists(Path.Combine(root, "new-profile")), await error);
            Assert.Equal(success, text.Contains("Return to Command Palette"));
            if (!success) Assert.Contains("Claude exited with code 7", text);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RemovingClaudeProfilePreservesCodexProfileWithSameSlot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeDeleteTests", Guid.NewGuid().ToString());
        try
        {
            var codex = Path.Combine(root, "profiles", "account-1");
            var claude = Path.Combine(root, "profiles", "claude-account-1");
            Directory.CreateDirectory(codex);
            Directory.CreateDirectory(claude);
            File.WriteAllText(Path.Combine(codex, "auth.json"), "test");
            File.WriteAllText(Path.Combine(claude, ".credentials.json"), "test");
            CodexProfiles.DeleteUnderRoot(root, 1, "claude");
            Assert.False(Directory.Exists(claude));
            Assert.True(File.Exists(Path.Combine(codex, "auth.json")));
        }
        finally { Directory.Delete(root, true); }
    }
}
