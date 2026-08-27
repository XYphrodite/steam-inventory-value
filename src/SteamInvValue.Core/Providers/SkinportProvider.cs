using System.Text.Json;

namespace SteamInvValue.Core.Providers;

/// <summary>Skinport: публичный прайс-лист всего каталога одним запросом (лимит 8 запросов / 5 минут).</summary>
public sealed class SkinportProvider(FileCache cache) : IPriceProvider
{
    private static readonly int[] Apps = [730, 440, 570, 252490];

    public string Name => "Skinport";
    public string Site => "https://skinport.com";
    public decimal PayoutRate => 0.88m; // комиссия продавца ~12%
    public bool Supports(int appId) => Apps.Contains(appId);

    public async Task<IReadOnlyDictionary<string, decimal>> GetPricesUsdAsync(
        int appId, IReadOnlyCollection<string> names, CancellationToken ct)
    {
        var map = await cache.GetOrAddAsync($"skinport_{appId}", TimeSpan.FromMinutes(30), async () =>
        {
            var url = $"https://api.skinport.com/v1/items?app_id={appId}&currency=USD&tradable=0";
            var json = await Http.Client.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            var d = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var name = e.GetProperty("market_hash_name").GetString();
                if (name is null) continue;
                // min_price — самый дешёвый лот сейчас; если лотов нет, берём оценку площадки
                var price = Dec(e, "min_price") ?? Dec(e, "suggested_price");
                if (price is > 0) d[name] = price.Value;
            }
            return d;
        });
        return map;
    }

    private static decimal? Dec(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;
}
