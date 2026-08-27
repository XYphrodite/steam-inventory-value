using System.Text.Json;

namespace SteamInvValue.Core.Providers;

/// <summary>Waxpeer: /v1/prices, цены в тысячных долях доллара.</summary>
public sealed class WaxpeerProvider(FileCache cache) : IPriceProvider
{
    private static readonly Dictionary<int, string> Games = new()
    {
        [730] = "csgo", [570] = "dota2", [440] = "tf2", [252490] = "rust",
    };

    public string Name => "Waxpeer";
    public string Site => "https://waxpeer.com";
    public decimal PayoutRate => 0.94m; // ~6% комиссии продавца
    public bool Supports(int appId) => Games.ContainsKey(appId);

    public async Task<IReadOnlyDictionary<string, decimal>> GetPricesUsdAsync(
        int appId, IReadOnlyCollection<string> names, CancellationToken ct)
    {
        if (!Games.TryGetValue(appId, out var game)) return new Dictionary<string, decimal>();

        return await cache.GetOrAddAsync($"waxpeer_{game}", TimeSpan.FromMinutes(30), async () =>
        {
            var json = await Http.Client.GetStringAsync(
                $"https://api.waxpeer.com/v1/prices?game={game}&minified=1", ct);
            using var doc = JsonDocument.Parse(json);
            var d = new Dictionary<string, decimal>(StringComparer.Ordinal);
            if (!doc.RootElement.TryGetProperty("items", out var items)) return d;

            foreach (var e in items.EnumerateArray())
            {
                var name = e.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name is null || !e.TryGetProperty("min", out var m) || m.ValueKind != JsonValueKind.Number) continue;
                var price = m.GetDecimal() / 1000m; // Waxpeer отдаёт цену * 1000
                if (price > 0) d[name] = price;
            }
            return d;
        });
    }
}
