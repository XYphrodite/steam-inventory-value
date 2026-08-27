using System.Text.Json;

namespace SteamInvValue.Core;

/// <summary>Курсы: рубль — с ЦБ РФ, крипта — с CoinGecko. Всё считается от USD.</summary>
public sealed class CurrencyService(FileCache cache)
{
    public decimal UsdRub { get; private set; } = 0m;
    public decimal BtcUsd { get; private set; } = 0m;
    public decimal UsdtUsd { get; private set; } = 1m;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var rates = await cache.GetOrAddAsync("fx_rates", TimeSpan.FromHours(1), async () =>
        {
            var r = new Dictionary<string, decimal>();
            try
            {
                var cbr = await Http.Client.GetStringAsync("https://www.cbr-xml-daily.ru/daily_json.js", ct);
                using var doc = JsonDocument.Parse(cbr);
                r["usdrub"] = doc.RootElement.GetProperty("Valute").GetProperty("USD").GetProperty("Value").GetDecimal();
            }
            catch { /* курс подставим ниже */ }

            try
            {
                var cg = await Http.Client.GetStringAsync(
                    "https://api.coingecko.com/api/v3/simple/price?ids=bitcoin,tether&vs_currencies=usd,rub", ct);
                using var doc = JsonDocument.Parse(cg);
                r["btcusd"] = doc.RootElement.GetProperty("bitcoin").GetProperty("usd").GetDecimal();
                r["usdtusd"] = doc.RootElement.GetProperty("tether").GetProperty("usd").GetDecimal();
                if (!r.ContainsKey("usdrub") &&
                    doc.RootElement.GetProperty("tether").TryGetProperty("rub", out var tr))
                    r["usdrub"] = tr.GetDecimal(); // фолбэк: курс USDT/RUB как прокси
            }
            catch { /* без крипты обойдёмся */ }

            return r;
        });

        UsdRub = rates.GetValueOrDefault("usdrub", 0m);
        BtcUsd = rates.GetValueOrDefault("btcusd", 0m);
        UsdtUsd = rates.GetValueOrDefault("usdtusd", 1m);
    }

    public Money Convert(decimal usd) => new(
        Math.Round(usd, 2),
        Math.Round(usd * UsdRub, 2),
        BtcUsd > 0 ? Math.Round(usd / BtcUsd, 8) : 0m,
        UsdtUsd > 0 ? Math.Round(usd / UsdtUsd, 2) : 0m);
}
