using AIUsageDock.Providers;
using Xunit;

namespace AIUsageDock.Tests;

public sealed class CodexResponseTests
{
    private const string Account = "{\"id\":2,\"result\":{\"account\":{\"planType\":\"team\"}}}";
    private const string Limits = "{\"id\":3,\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":8,\"windowDurationMins\":300}}}}";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentResponsesPreserveLimitsAndAccountInEitherOrder(bool limitsFirst)
    {
        var lines = limitsFirst ? new[] { Limits, Account } : new[] { Account, Limits };
        using var output = new StringReader(string.Join('\n', lines));
        var result = await CodexUsageProvider.ReadAccountAndRateLimitResponsesAsync(output);
        var snapshot = CodexUsageProvider.ParseRateLimitsJson(result.RateLimitsJson, DateTimeOffset.UtcNow, result.AccountJson);
        Assert.NotNull(snapshot);
        Assert.Equal(92, snapshot.Session!.RemainingPercent);
        Assert.Equal("Team", snapshot.Plan);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionalAccountErrorDoesNotLoseLimits(bool limitsFirst)
    {
        const string error = "{\"id\":2,\"error\":{\"message\":\"unsupported\"}}";
        var lines = limitsFirst ? new[] { Limits, "{\"method\":\"notification\"}", error }
            : new[] { error, Limits };
        using var output = new StringReader(string.Join('\n', lines));
        var result = await CodexUsageProvider.ReadAccountAndRateLimitResponsesAsync(output);
        Assert.Null(result.AccountJson);
        Assert.Contains("rateLimits", result.RateLimitsJson);
    }

    [Fact]
    public async Task RateLimitErrorsRemainErrors()
    {
        using var output = new StringReader("{\"id\":3,\"error\":{\"message\":\"authentication required\"}}");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CodexUsageProvider.ReadAccountAndRateLimitResponsesAsync(output));
        Assert.Contains("authentication required", error.Message);
    }
}
