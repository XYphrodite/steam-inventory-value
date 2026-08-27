using System.Globalization;
using System.Text.Json;

namespace SteamInvValue.Core.Providers;

/// <summary>Market.CSGO (бывш. TM): публичный дамп цен, берём сразу в USD.</summary>
public sealed class MarketCsgoProvider(FileCache cache) : IPriceProvider
{
    public string Name => "Market.CSGO";
    public string Site => "https://market.csgo.com";
    public decimal PayoutRate => 0.95m; // комиссия площадки ~5%
    public bool Supports(int appId) => appId == 730;

    public async Task<IReadOnlyDictionary<string, decimal>> GetPricesUsdAsync(
        int appId, IReadOnlyCollection<string> names, CancellationToken ct)
    {
        if (appId != 730) return new Dictionary<string, decimal>();

        return await cache.GetOrAddAsync("marketcsgo_730", TimeSpan.FromMinutes(30), async () =>
        {
            var json = await Http.Client.GetStringAsync("https://market.csgo.com/api/v2/prices/USD.json", ct);
            using var doc = JsonDocument.Parse(json);
            var d = new Dictionary<string, decimal>(StringComparer.Ordinal);
            if (!doc.RootElement.TryGetProperty("items", out var items)) return d;

            foreach (var e in items.EnumerateArray())
            {
                var name = e.TryGetProperty("market_hash_name", out var n) ? n.GetString() : null;
                var raw = e.TryGetProperty("price", out var p) ? p.GetString() : null;
                if (name is null || raw is null) continue;
                if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) && price > 0)
                    d[name] = price;
            }
            return d;
        });
    }
}
