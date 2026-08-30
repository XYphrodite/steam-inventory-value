using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SteamInvValue.Core.Providers;

/// <summary>
/// Steam Community Market. Массового прайс-листа у Steam нет — только priceoverview по одному
/// имени за запрос, и он жёстко лимитирован. Поэтому: очередь по одному, пауза между запросами,
/// экспоненциальный backoff на 429 и долгий файловый кэш, который накапливается между запусками.
/// </summary>
public sealed partial class SteamMarketProvider(
    FileCache cache,
    Action<string>? log = null,
    int budget = 400,
    int delayMs = 3500) : IPriceProvider
{
    private readonly Action<string> _log = log ?? (_ => { });
    private int _remaining = budget;

    /// <summary>Объём продаж за сутки по именам, заполняется попутно.</summary>
    public Dictionary<string, int> Volume { get; } = new(StringComparer.Ordinal);

    /// <summary>Сколько имён осталось неопрошенными из-за лимита/блокировки.</summary>
    public int Skipped { get; private set; }

    public string Name => "Steam";
    public string Site => "https://steamcommunity.com/market";
    /// <summary>Комиссия Steam ~15% (5% Steam + 10% игре) — считаем от цены покупателя.</summary>
    public decimal PayoutRate => 1m / 1.15m;
    public bool Supports(int appId) => true;
    /// <summary>Steam-маркету обмен не нужен, нужен признак marketable.</summary>
    public bool CanSell(InventoryItem item) => item.Marketable;

    [GeneratedRegex(@"[\d.,]+")]
    private static partial Regex NumberPart();

    public async Task<IReadOnlyDictionary<string, decimal>> GetPricesUsdAsync(
        int appId, IReadOnlyCollection<string> names, CancellationToken ct)
    {
        var result = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var pending = new List<string>();

        foreach (var name in names.Distinct(StringComparer.Ordinal))
        {
            var hit = cache.Get<SteamPrice>(CacheKey(appId, name), TimeSpan.FromHours(12));
            if (hit is not null)
            {
                if (hit.Usd > 0) result[name] = hit.Usd;
                if (hit.Volume > 0) Volume[name] = hit.Volume;
            }
            else pending.Add(name);
        }

        if (pending.Count == 0) return result;

        var toQuery = pending.Take(Math.Max(0, _remaining)).ToList();
        _remaining -= toQuery.Count;
        Skipped += pending.Count - toQuery.Count;
        _log(S.SteamPlan(result.Count, toQuery.Count, Skipped, toQuery.Count * delayMs / 60000.0));

        var consecutiveFailures = 0;
        var done = 0;

        foreach (var name in toQuery)
        {
            if (ct.IsCancellationRequested) break;

            var price = await FetchAsync(appId, name, ct);
            if (price is null)
            {
                if (++consecutiveFailures >= 5)
                {
                    Skipped += toQuery.Count - done;
                    _log(S.SteamGivingUp);
                    break;
                }
            }
            else
            {
                consecutiveFailures = 0;
                cache.Set(CacheKey(appId, name), price);
                if (price.Usd > 0) result[name] = price.Usd;
                if (price.Volume > 0) Volume[name] = price.Volume;
            }

            done++;
            if (done % 25 == 0) _log(S.SteamProgress(done, toQuery.Count));
            await Task.Delay(delayMs, ct);
        }

        return result;
    }

    private static string CacheKey(int appId, string name) => $"steamprice_{appId}_{name}";

    private async Task<SteamPrice?> FetchAsync(int appId, string name, CancellationToken ct)
    {
        var url = $"https://steamcommunity.com/market/priceoverview/?appid={appId}&currency=1" +
                  $"&market_hash_name={Uri.EscapeDataString(name)}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var resp = await Http.Client.GetAsync(url, ct);
            if ((int)resp.StatusCode == 429)
            {
                var wait = TimeSpan.FromSeconds(15 * Math.Pow(2, attempt));
                _log(S.SteamThrottled(wait.TotalSeconds));
                await Task.Delay(wait, ct);
                continue;
            }
            if (!resp.IsSuccessStatusCode) return null;

            var body = await resp.Content.ReadAsStringAsync(ct);
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (!root.TryGetProperty("success", out var ok) || ok.ValueKind != JsonValueKind.True)
                    return new SteamPrice(0m, 0); // предмет не торгуется — запоминаем, чтобы не спрашивать снова

                var raw = Str(root, "lowest_price") ?? Str(root, "median_price");
                var volume = int.TryParse((Str(root, "volume") ?? "0").Replace(",", ""), out var v) ? v : 0;
                return new SteamPrice(ParseMoney(raw), volume);
            }
            catch { return null; }
        }
        return null;
    }

    private static string? Str(JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>"$1,234.56" -> 1234.56</summary>
    internal static decimal ParseMoney(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0m;
        var m = NumberPart().Match(s.Replace("&#36;", "$"));
        if (!m.Success) return 0m;
        var num = m.Value.Replace(",", "");
        return decimal.TryParse(num, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    public sealed record SteamPrice(decimal Usd, int Volume);
}
