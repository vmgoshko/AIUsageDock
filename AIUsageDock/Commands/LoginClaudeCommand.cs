using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using AIUsageDock.Providers;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace AIUsageDock.Commands;

internal sealed partial class LoginClaudeCommand : InvokableCommand
{
    private static readonly ConcurrentDictionary<int, Process> Logins = new();
    private readonly int _slot;
    private readonly Func<Task>? _afterExit;
    private readonly bool _openUsage;
    public LoginClaudeCommand(int slot, Func<Task>? afterExit = null, bool openUsage = false)
    {
        _slot = slot;
        _afterExit = afterExit;
        _openUsage = openUsage;
        Id = $"aiusagedock.claude.{(openUsage ? "open" : "login")}.{slot}";
        Name = openUsage ? "Open Claude Code for this band" : "Sign in to Claude";
    }
    internal static void CancelLogin(int slot)
    {
        if (Logins.TryRemove(slot, out var process)) ProcessHelpers.TryKill(process);
    }
    public override ICommandResult Invoke()
    {
        try
        {
            var executable = ExecutableResolver.ResolveClaude();
            if (executable is null) return CommandResult.ShowToast("Install Claude Code CLI first");
            var profile = CodexProfiles.Home(_slot, "claude");
            Directory.CreateDirectory(profile);
            var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-EncodedCommand");
            info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(BuildScript(executable, profile, _openUsage))));
            CancelLogin(_slot);
            var process = Process.Start(info);
            if (process is not null) { Logins[_slot] = process; _ = AfterExitAsync(process); }
            return CommandResult.ShowToast(_openUsage ? "Run /usage in the opened Claude window" : "Complete Claude sign-in in the opened window");
        }
        catch (Exception exception) { return CommandResult.ShowToast($"Could not start Claude: {exception.Message}"); }
    }
    internal static string BuildScript(string executable, string profile, bool openUsage = false)
    {
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        var clear = string.Join(" ", ClaudeProfileProcess.ClearedVariables.Select(v => $"Remove-Item -LiteralPath 'Env:{v}' -ErrorAction SilentlyContinue;"));
        return "$ErrorActionPreference='Stop'; try { " +
            $"New-Item -ItemType Directory -Force -Path {Quote(profile)} | Out-Null; " +
            $"$env:CLAUDE_CONFIG_DIR={Quote(profile)}; $env:ANTHROPIC_CONFIG_DIR={Quote(Path.Combine(profile, "anthropic"))}; $env:DISABLE_AUTOUPDATER='1'; {clear} " +
            $"Set-Location -LiteralPath {Quote(profile)}; " +
            (openUsage ? "Write-Host 'Run /usage to refresh usage data. Exit Claude when finished.'; "
                : "Write-Host 'Sign in with your Claude account. Claude Code may require a paid plan.'; ") +
            $"& {Quote(executable)} {(openUsage ? "" : "auth login --claudeai")}; " +
            "if ($LASTEXITCODE -ne 0) { throw ('Claude exited with code ' + $LASTEXITCODE) }; " +
            "Write-Host 'Return to Command Palette to view this band.' " +
            "} catch { Write-Host $_.Exception.Message -ForegroundColor Red }; Read-Host 'Press Enter to close'";
    }
    private async Task AfterExitAsync(Process process)
    {
        using (process)
        {
            await process.WaitForExitAsync();
            if (Logins.TryGetValue(_slot, out var active) && ReferenceEquals(active, process)) Logins.TryRemove(_slot, out _);
            if (_afterExit is not null) { try { await _afterExit(); } catch { } }
        }
    }
}
