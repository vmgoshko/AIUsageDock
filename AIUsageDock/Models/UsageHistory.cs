using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIUsageDock.Models;

internal sealed record UsageSample(DateTimeOffset At, double? SessionUsed, double? WeeklyUsed,
    DateTimeOffset? SessionReset, DateTimeOffset? WeeklyReset);

internal sealed class UsageHistory(string path)
{
    private sealed record State(string? Account, List<UsageSample> Samples);
    private readonly object _gate = new();
    public string? Error { get; private set; }

    public void Record(UsageSnapshot snapshot)
    {
        if (snapshot.IsStale || snapshot.Session is null && snapshot.Weekly is null) return;
        lock (_gate)
        {
            try
            {
                var state = Read();
                var email = snapshot.AccountDetails.FirstOrDefault(d => d.Label == "Account")?.Value;
                var identity = email is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email)));
                if (identity is not null && state.Account is not null && identity != state.Account)
                    state = new State(identity, []);
                else if (identity is not null) state = state with { Account = identity };
                // Reopening the page and cached reads must not produce fake samples.
                if (state.Samples.LastOrDefault() is { } last && snapshot.FetchedAt - last.At < TimeSpan.FromMinutes(1)) return;
                state.Samples.RemoveAll(s => s.At < snapshot.FetchedAt.AddDays(-14));
                state.Samples.Add(new(snapshot.FetchedAt, snapshot.Session?.UsedPercent, snapshot.Weekly?.UsedPercent,
                    snapshot.Session?.ResetsAt, snapshot.Weekly?.ResetsAt));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(state));
                File.Move(path + ".tmp", path, overwrite: true);
                Error = null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            { Error = "Usage history could not be saved"; }
        }
    }

    public UsageSample[] GetSamples(DateTimeOffset since)
    {
        lock (_gate)
        {
            try { return Read().Samples.Where(s => s.At >= since).OrderBy(s => s.At).ToArray(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            { Error = "Usage history could not be read"; return []; }
        }
    }

    private State Read()
    {
        if (!File.Exists(path)) return new(null, []);
        var state = JsonSerializer.Deserialize<State>(File.ReadAllText(path));
        if (state?.Samples is null) throw new InvalidDataException("Invalid history");
        return state;
    }
}
