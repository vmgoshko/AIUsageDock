using System.Diagnostics;
using System.Text;
using System.Collections.Concurrent;
using AIUsageDock.Providers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AIUsageDock.Commands;

internal sealed partial class LoginCodexCommand : InvokableCommand
{
    private static readonly ConcurrentDictionary<int, Process> Logins = new();
    internal static void CancelLogin(int slot)
    {
        if (Logins.TryRemove(slot, out var process))
            ProcessHelpers.TryKill(process);
    }
    private readonly int slot;
    private readonly Func<Task>? _afterExit;
    public LoginCodexCommand(int accountSlot, Func<Task>? afterExit = null)
    {
        slot = accountSlot;
        _afterExit = afterExit;
        Id = $"codexusagedock.login.{slot}";
        Name = $"Sign in to Codex account {slot}";
        Icon = new IconInfo("\uE77B");
    }
    public override ICommandResult Invoke()
    {
        try
        {
            var executable = ExecutableResolver.ResolveCodex();
            if (executable is null) return CommandResult.ShowToast("Install Codex CLI first (npm install -g @openai/codex)");
            var profile = CodexProfiles.Home(slot);
            Directory.CreateDirectory(profile);
            // The interactive console is required to display login instructions and errors.
            // Only this child process receives CODEX_HOME; the desktop/CLI login is untouched.
            var script = BuildLoginScript(executable, profile, slot);
            var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-EncodedCommand");
            info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
            var process = Process.Start(info);
            if (process is not null)
            {
                CancelLogin(slot);
                Logins[slot] = process;
                _ = RefreshAfterExitAsync(process);
            }
            return CommandResult.ShowToast($"Sign in to account {slot} in the opened window");
        }
        catch (Exception exception)
        {
            return CommandResult.ShowToast($"Could not start login: {exception.Message}");
        }
    }

    private async Task RefreshAfterExitAsync(Process process)
    {
        using (process)
        {
            await process.WaitForExitAsync();
            if (Logins.TryGetValue(slot, out var active) && ReferenceEquals(active, process))
                Logins.TryRemove(slot, out _);
            if (_afterExit is not null)
            {
                try { await _afterExit(); } catch { /* A manual refresh remains available. */ }
            }
        }
    }

    internal static string BuildLoginScript(string executable, string profile, int slot)
    {
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        return "$ErrorActionPreference = 'Stop'; try { " +
                $"New-Item -ItemType Directory -Force -Path {Quote(profile)} | Out-Null; " +
                $"$env:CODEX_HOME = {Quote(profile)}; " +
                $"Write-Host 'Sign in to Codex account {slot}. Choose the intended account in your browser.'; " +
                $"& {Quote(executable)} -c 'cli_auth_credentials_store=\"file\"' login; " +
                "if ($LASTEXITCODE -ne 0) { throw ('Codex login failed with exit code ' + $LASTEXITCODE) }; " +
                "Write-Host 'Login completed. Return to Command Palette and run Refresh Codex usage.' " +
                "} catch { Write-Host ('Sign-in failed: ' + $_.Exception.Message) -ForegroundColor Red }; " +
                "Read-Host 'Press Enter to close'";
    }
}
