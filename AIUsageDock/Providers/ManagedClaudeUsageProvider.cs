using System.Text.Json;
using AIUsageDock.Models;

namespace AIUsageDock.Providers;

internal sealed class ManagedClaudeUsageProvider : IUsageProvider
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly int _slot;
    private readonly string _profile;
    private UsageResult? _cached;
    private DateTimeOffset _readAt;
    private bool _removed;
    public ManagedClaudeUsageProvider(CodexAccount account)
    {
        _slot = account.Slot;
        _profile = CodexProfiles.Home(_slot, "claude");
        Id = account.Id;
        DisplayName = account.Name;
        History = new UsageHistory(Path.Combine(_profile, "usage-history.json"));
    }

    public string Id { get; }
    public string DisplayName { get; internal set; }
    public string IconName => "claude";
    public UsageHistory History { get; }
    public bool IsSignedIn { get; private set; }

    public async Task<UsageResult> GetAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_removed) return UsageResult.Failure(UsageFailureReason.NotSignedIn, "Band removed");
            if (!force && _cached is not null && DateTimeOffset.UtcNow - _readAt < TimeSpan.FromMinutes(1)) return _cached;
            if (!Directory.Exists(_profile)) return UsageResult.Failure(UsageFailureReason.NotSignedIn, "Click this band to sign in to Claude");
            var executable = ExecutableResolver.ResolveClaude();
            if (executable is null) return UsageResult.Failure(UsageFailureReason.CliUnavailable, "Claude Code CLI not found");
            var status = await ClaudeProfileProcess.RunAsync(executable, _profile, ["auth", "status", "--json"], cancellationToken);
            using var document = JsonDocument.Parse(status.Output);
            var root = document.RootElement;
            IsSignedIn = HasBandLogin(root);
            _readAt = DateTimeOffset.UtcNow;
            if (!IsSignedIn) return _cached = UsageResult.Failure(UsageFailureReason.NotSignedIn, "Click this band to sign in to Claude");
            var current = await TryReadUsageAsync(executable, root, cancellationToken);
            _readAt = DateTimeOffset.UtcNow;
            var cachePath = Path.Combine(_profile, ".claude.json");
            if (current is not null)
            {
                History.Record(current);
                return _cached = UsageResult.Success(WithProfileDetails(current, cachePath));
            }

            if (File.Exists(cachePath))
            {
                var snapshot = ClaudeUsageProvider.ParseCacheJson(File.ReadAllText(cachePath), _readAt, out var fetchedAt);
                if (snapshot is not null)
                    return _cached = UsageResult.Success(snapshot with
                    { IsStale = fetchedAt is null || _readAt - fetchedAt > TimeSpan.FromMinutes(5) });
            }
            var plan = root.TryGetProperty("subscriptionType", out var planValue) && planValue.ValueKind == JsonValueKind.String ? planValue.GetString() : null;
            return _cached = UsageResult.Failure(UsageFailureReason.InvalidData,
                plan == "free" ? "Signed in · Free plan: usage limits are unavailable through Claude Code"
                    : "Signed in · no usage data. Open Claude Code for this band and run /usage.");
        }
        catch (Exception exception) when (IsReadFailure(exception, cancellationToken))
        {
            return UsageResult.Failure(UsageFailureReason.ProcessFailed, "Claude authentication status could not be read");
        }
        finally { _gate.Release(); }
    }

    private static UsageSnapshot WithProfileDetails(UsageSnapshot snapshot, string cachePath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(cachePath));
            var details = new List<UsageDetail>();
            var plan = ClaudeUsageProvider.ReadAccountDetails(document.RootElement, details);
            return snapshot with
            {
                Plan = plan ?? snapshot.Plan,
                Details = details.Concat(snapshot.AccountDetails).DistinctBy(detail => detail.Label).ToArray(),
            };
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
        {
            return snapshot;
        }
    }

    private async Task<UsageSnapshot?> TryReadUsageAsync(string executable, JsonElement accountStatus, CancellationToken cancellationToken)
    {
        try
        {
            // Send only a structured CLI control request. A print-mode /usage
            // prompt could be sent to the model on a CLI that doesn't support it.
            var result = await ClaudeProfileProcess.RunAsync(executable, _profile,
                ["-p", "--safe-mode", "--input-format", "stream-json", "--output-format", "stream-json",
                    "--verbose", "--no-session-persistence"],
                cancellationToken, ClaudeUsageProvider.UsageRequest);
            return result.Code == 0
                ? ClaudeUsageProvider.ParseUsageResponse(result.Output, accountStatus, DateTimeOffset.UtcNow)
                : null;
        }
        catch (Exception exception) when (IsReadFailure(exception, cancellationToken))
        {
            return null;
        }
    }

    private static bool IsReadFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is JsonException or IOException or System.ComponentModel.Win32Exception
            or InvalidOperationException or UnauthorizedAccessException
        || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;

    internal static bool HasBandLogin(JsonElement status) =>
        status.TryGetProperty("loggedIn", out var loggedIn) && loggedIn.ValueKind == JsonValueKind.True
        && status.TryGetProperty("authMethod", out var method) && method.ValueKind == JsonValueKind.String
        && method.GetString() == "claude.ai";

    internal async Task LogoutAndRemoveAsync()
    {
        await _gate.WaitAsync();
        try
        {
            Commands.LoginClaudeCommand.CancelLogin(_slot);
            if (Directory.Exists(_profile))
            {
                var executable = ExecutableResolver.ResolveClaude();
                if (executable is not null)
                {
                    var result = await ClaudeProfileProcess.RunAsync(executable, _profile, ["auth", "logout"]);
                    if (result.Code != 0) throw new InvalidOperationException("Claude logout failed; band retained");
                }
                CodexProfiles.Delete(_slot, "claude");
            }
            _removed = true;
            IsSignedIn = false;
            _cached = null;
        }
        finally { _gate.Release(); }
    }
    public void Dispose() => _gate.Dispose();
}
