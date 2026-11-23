using System.Diagnostics;

namespace AIUsageDock.Providers;

internal static class ClaudeProfileProcess
{
    internal static readonly string[] ClearedVariables =
    [
        "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN",
        "CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX", "CLAUDE_CODE_USE_FOUNDRY",
        "ANTHROPIC_PROFILE", "ANTHROPIC_FEDERATION_RULE_ID", "ANTHROPIC_ORGANIZATION_ID",
        "ANTHROPIC_BASE_URL",
        "ANTHROPIC_CUSTOM_HEADERS", "ANTHROPIC_UNIX_SOCKET", "CLAUDE_CODE_OAUTH_REFRESH_TOKEN",
        "CCR_OAUTH_TOKEN_FILE", "CLAUDE_BG_AUTH_SNAPSHOT_PATH",
        "CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR", "CLAUDE_CODE_API_KEY_FILE_DESCRIPTOR", "CLAUDE_CODE_GATEWAY_TOKEN_FILE_DESCRIPTOR",
        "CLAUDE_SECURESTORAGE_CONFIG_DIR", "CLAUDE_CODE_HOST_CREDS_FILE",
        "ANTHROPIC_IDENTITY_TOKEN", "ANTHROPIC_IDENTITY_TOKEN_FILE", "ANTHROPIC_SERVICE_ACCOUNT_ID",
    ];
    public static ProcessStartInfo StartInfo(string executable, string profile, params string[] arguments)
    {
        var info = ProcessHelpers.CreateStartInfo(executable, arguments,
            new Dictionary<string, string> { ["CLAUDE_CONFIG_DIR"] = profile,
                ["ANTHROPIC_CONFIG_DIR"] = Path.Combine(profile, "anthropic"), ["DISABLE_AUTOUPDATER"] = "1" });
        foreach (var variable in ClearedVariables) info.Environment.Remove(variable);
        // Keep project settings and MCP servers outside the CLI's current directory.
        info.WorkingDirectory = profile;
        return info;
    }
    public static async Task<(int Code, string Output)> RunAsync(
        string executable, string profile, string[] arguments,
        CancellationToken cancellationToken = default, string? input = null)
    {
        Directory.CreateDirectory(profile);
        using var process = new Process { StartInfo = StartInfo(executable, profile, arguments) };
        process.Start();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            if (input is not null)
            {
                await process.StandardInput.WriteLineAsync(input.AsMemory(), timeout.Token);
            }
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            await error; // Drain stderr, but don't show credential-bearing diagnostics.
            return (process.ExitCode, await output);
        }
        finally { ProcessHelpers.TryKill(process); }
    }
}
