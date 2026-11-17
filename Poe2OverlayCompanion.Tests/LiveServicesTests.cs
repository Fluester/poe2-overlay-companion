using Poe2OverlayCompanion.Services;

namespace Poe2OverlayCompanion.Tests;

[Trait("Category", "Live")]
public sealed class LiveServicesTests
{
    [Fact]
    public async Task LivePoe2DataService_FetchesOfficialCatalogAndResolvesUniques()
    {
        bool ok = await LivePoe2DataService.RefreshIfStaleAsync(force: true);
        Assert.True(ok);
        Assert.True(LivePoe2DataService.CachedUniquesCount >= 400);
        Assert.True(LivePoe2DataService.CachedBasesCount >= 2000);
        Assert.True(LivePoe2DataService.CachedGemsCount >= 800);

        var andv = Poe2KnowledgeDb.ResolveItemObtain("Andvarius\nGold Ring", "", null, 45);
        Assert.Contains("Gold Ring", andv.ObtainSourceEn, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Gold Ring", andv.Category, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PoeTradePriceService_FetchesLiveLeaguesAndExchangeRates()
    {
        var leagues = await PoeTradePriceService.FetchLeaguesAsync();
        Assert.True(leagues.Count >= 4, $"Expected >= 4 leagues, got {leagues.Count}");

        var (exPerDiv, exPerChaos) = await PoeTradePriceService.FetchKeyExchangeRatesAsync("Forbidden Rites");
        Assert.True(exPerDiv > 0 && exPerChaos > 0);
        Assert.False(exPerDiv == 120.0 && exPerChaos == 3.5, "Rates unexpectedly fell back to defaults");
    }
}
