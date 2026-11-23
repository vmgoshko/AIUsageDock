using AIUsageDock.Models;
using AIUsageDock.Pages;
using Xunit;

namespace AIUsageDock.Tests;

public sealed class UsageHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 20, 0, 0, TimeSpan.FromHours(4));
    private static UsageSnapshot Snapshot(DateTimeOffset at, string account = "first@example.test", bool stale = false) =>
        new("Codex", "Plus", [new(UsageWindowKind.Session, "Session", 11, Now.AddHours(3), 300),
            new(UsageWindowKind.Weekly, "Week", 29, Now.AddDays(3), 10080)], at, "test", IsStale: stale,
            Details: [new("Account", account)]);

    [Fact]
    public void HistoryPersistsWithoutDuplicatingCachedOrStaleReadings()
    {
        WithHistory((history, path) =>
        {
            history.Record(Snapshot(Now));
            history.Record(Snapshot(Now));
            history.Record(Snapshot(Now.AddSeconds(20)));
            history.Record(Snapshot(Now.AddMinutes(1), stale: true));
            history.Record(Snapshot(Now.AddMinutes(2)));
            var samples = new UsageHistory(path).GetSamples(Now.AddHours(-1));
            Assert.Equal(2, samples.Length);
            Assert.Equal(11, samples[0].SessionUsed);
            Assert.DoesNotContain("first@example.test", File.ReadAllText(path));
        });
    }

    [Fact]
    public void AccountSwitchClearsPreviousHistoryAndRetentionPrunesOldSamples()
    {
        WithHistory((history, _) =>
        {
            history.Record(Snapshot(Now.AddDays(-15)));
            history.Record(Snapshot(Now.AddMinutes(-2)));
            history.Record(Snapshot(Now));
            Assert.Equal(2, history.GetSamples(Now.AddDays(-30)).Length);
            history.Record(Snapshot(Now.AddSeconds(1), "other@example.test"));
            Assert.Single(history.GetSamples(Now.AddDays(-30)));
        });
    }

    [Fact]
    public void CorruptHistoryIsReportedAndNotOverwritten()
    {
        WithHistory((history, path) =>
        {
            File.WriteAllText(path, "broken metadata");
            history.Record(Snapshot(Now));
            Assert.Empty(history.GetSamples(Now.AddDays(-1)));
            Assert.NotNull(history.Error);
            Assert.Equal("broken metadata", File.ReadAllText(path));
        });
    }

    [Fact]
    public void GraphBreaksAtGapsResetsAndMissingValues()
    {
        var a = new UsageSample(Now, 11, 29, Now.AddHours(3), Now.AddDays(3));
        var b = a with { At = Now.AddMinutes(1), SessionUsed = 12, WeeklyUsed = 30 };
        Assert.True(UsageHistoryChart.CanConnect(a, b, true));
        Assert.True(UsageHistoryChart.CanConnect(a, b with { SessionReset = a.SessionReset?.AddMilliseconds(700) }, true));
        Assert.True(UsageHistoryChart.CanConnect(a, b with { WeeklyReset = a.WeeklyReset?.AddMilliseconds(-700) }, false));
        Assert.False(UsageHistoryChart.CanConnect(a, b with { At = Now.AddMinutes(6) }, true));
        Assert.False(UsageHistoryChart.CanConnect(a, b with { SessionReset = Now.AddHours(8) }, true));
        Assert.False(UsageHistoryChart.CanConnect(a, b with { SessionUsed = 0 }, true));
        Assert.False(UsageHistoryChart.CanConnect(a, b with { SessionUsed = null }, true));
        Assert.True(UsageHistoryChart.CanConnect(a, b with { SessionReset = Now.AddHours(8) }, false));
    }

    [Fact]
    public void ChartRendersPngAndCardContainsGraphAboveAccountData()
    {
        WithHistory((history, _) =>
        {
            history.Record(Snapshot(Now));
            var card = System.Text.Json.Nodes.JsonNode.Parse(UsageCard.Build(
                UsageResult.Success(Snapshot(Now)), "Private", "codex", Now, history))!;
            var body = card["body"]!.AsArray();
            var chart = body.Single(item => item?["type"]?.GetValue<string>() == "Image")!;
            var account = body.Single(item => item?["type"]?.GetValue<string>() == "FactSet")!;
            Assert.True(body.IndexOf(chart) < body.IndexOf(account));
            Assert.StartsWith("data:image/png;base64,", chart["url"]!.GetValue<string>());
            var image = UsageHistoryChart.Render(history.GetSamples(Now.AddHours(-24)), Now);
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, image.Take(8));
        });
        var previewPath = Environment.GetEnvironmentVariable("USAGE_CHART_PREVIEW");
        if (previewPath is not null)
        {
            var samples = Enumerable.Range(0, 1441).Where(i => i < 650 || i > 780).Select(i =>
                new UsageSample(Now.AddHours(-24).AddMinutes(i), (i % 330) / 330d * 83,
                    17 + i / 1440d * 22, Now.AddHours(-24).AddMinutes(i / 330 * 330 + 330), Now.AddDays(3))).ToArray();
            File.WriteAllBytes(previewPath, UsageHistoryChart.Render(samples, Now));
        }
    }

    private static void WithHistory(Action<UsageHistory, string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "UsageHistoryTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        try { var path = Path.Combine(root, "usage-history.json"); action(new(path), path); }
        finally { Directory.Delete(root, true); }
    }
}
