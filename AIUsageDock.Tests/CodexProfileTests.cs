using AIUsageDock.Providers;
using AIUsageDock.Commands;
using System.Diagnostics;
using System.Text;
using Xunit;

namespace AIUsageDock.Tests;

public sealed class CodexProfileTests
{
    [Fact]
    public void RemovingProfileDeletesCredentialsButPreservesOtherAccounts()
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexDeleteTests", Guid.NewGuid().ToString());
        try
        {
            var first = Path.Combine(root, "profiles", "account-1");
            var second = Path.Combine(root, "profiles", "account-2");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            File.WriteAllText(Path.Combine(first, "auth.json"), "test credentials");
            File.WriteAllText(Path.Combine(second, "auth.json"), "other account");
            CodexProfiles.DeleteUnderRoot(root, 1);
            Assert.False(Directory.Exists(first));
            Assert.True(File.Exists(Path.Combine(second, "auth.json")));
            CodexProfiles.DeleteUnderRoot(root, 1); // Repeating cleanup is harmless.
            Assert.Throws<ArgumentOutOfRangeException>(() => CodexProfiles.DeleteUnderRoot(root, 0));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void ProfileStorageIsOutsideMsixAppDataVirtualization()
    {
        var first = CodexProfiles.Home(1);
        Assert.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex-usage-dock"), first);
        Assert.NotEqual(first, CodexProfiles.Home(2));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(7, false)]
    public async Task LoginCreatesMissingDirectoryAndReportsCliFailure(int exitCode, bool success)
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexLoginTest-'" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var executable = Path.Combine(root, "fake-codex.cmd");
            await File.WriteAllTextAsync(executable, $"@echo off\r\nif not exist \"%CODEX_HOME%\" exit /b 99\r\nexit /b {exitCode}\r\n");
            var profile = Path.Combine(root, "missing-profile");
            var script = LoginCodexCommand.BuildLoginScript(executable, profile, 1)
                .Replace("Read-Host 'Press Enter to close'", "");
            var info = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-EncodedCommand");
            info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
            using var process = Process.Start(info)!;
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            var output = await outputTask;
            Assert.True(Directory.Exists(profile), await errorTask);
            Assert.Equal(success, output.Contains("Login completed."));
            Assert.Equal(!success, output.Contains("Sign-in failed:"));
            if (!success) Assert.Contains("exit code 7", output);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AccountsHaveIndependentProcessEnvironmentsAndCommandIds()
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexProfileTests", Guid.NewGuid().ToString());
        try
        {
            using var first = new CodexUsageProvider("codex", "Codex 1", Path.Combine(root, "first"));
            using var second = new CodexUsageProvider("codex2", "Codex 2", Path.Combine(root, "second"));
            var inheritedHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            var firstProcess = first.CreateProfileStartInfo("codex.exe");
            var secondProcess = second.CreateProfileStartInfo("codex.exe");
            Assert.NotEqual(first.Id, second.Id);
            Assert.Equal(Path.Combine(root, "first"), firstProcess.Environment["CODEX_HOME"]);
            Assert.Equal(Path.Combine(root, "second"), secondProcess.Environment["CODEX_HOME"]);
            Assert.Equal(inheritedHome, Environment.GetEnvironmentVariable("CODEX_HOME"));
            Assert.Contains("cli_auth_credentials_store=\"file\"", firstProcess.ArgumentList);
            Assert.Contains("cli_auth_credentials_store=\"file\"", secondProcess.ArgumentList);
            Assert.Contains("app-server", firstProcess.ArgumentList);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
