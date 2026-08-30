using SteamInvValue.Core.Providers;

namespace SteamInvValue.Core;

public sealed class ValuationOptions
{
    /// <summary>Ограничить оценку конкретными appid (пусто — все инвентари профиля).</summary>
    public int[]? OnlyApps { get; set; }
    /// <summary>Спрашивать ли Steam Market (медленно из-за лимитов, но это единственный источник для карточек).</summary>
    public bool UseSteam { get; set; } = true;
    /// <summary>Сколько имён максимум опросить в Steam за один запуск.</summary>
    public int SteamBudget { get; set; } = 400;
    /// <summary>Пауза между запросами к Steam Market, мс.</summary>
    public int SteamDelayMs { get; set; } = 3500;
    public string Language { get; set; } = "english";
    /// <summary>Считать ли стоимость того, что продать нельзя (нет обмена и маркета). По умолчанию нет.</summary>
    public bool CountUnsellable { get; set; }
    /// <summary>Сколько минут переиспользовать уже прочитанный инвентарь, не дёргая Steam. 0 — всегда заново.</summary>
    public int InventoryCacheMinutes { get; set; } = 30;
}

public sealed class Valuator(FileCache? cache = null, Action<string>? log = null)
{
    private readonly FileCache _cache = cache ?? new FileCache();
    private readonly Action<string> _log = log ?? (_ => { });

    public async Task<Report> ValuateAsync(string profileInput, ValuationOptions? options = null,
        CancellationToken ct = default)
    {
        var opt = options ?? new ValuationOptions();

        _log("Определяю профиль…");
        var profile = await SteamIdResolver.ResolveAsync(profileInput, ct);
        _log($"SteamID64 {profile.SteamId64} ({profile.PersonaName ?? "без ника"})");

        var report = new Report
        {
            SteamId64 = profile.SteamId64,
            ProfileUrl = profile.ProfileUrl,
            PersonaName = profile.PersonaName,
        };

        var inv = new SteamInventoryClient(_log);
        IReadOnlyList<InventoryContext> discovered;
        var probed = false;
        string? why = null;

        try
        {
            discovered = await inv.GetContextsAsync(profile.SteamId64, ct);
            if (discovered.Count == 0) why = "страница профиля вернула пустой список";
        }
        catch (Exception ex)
        {
            discovered = [];
            why = ex.Message;
        }

        if (discovered.Count == 0)
        {
            _log($"Список игр со страницы профиля не получен ({why}) — перебираю известные игры.");
            discovered = await inv.ProbeContextsAsync(profile.SteamId64, opt.OnlyApps, ct);
            probed = true;
        }

        // Перебор знает только зашитый список игр, поэтому список инвентарей может быть неполным —
        // это обязано быть видно в отчёте, а не только в логе.
        if (probed)
            report.Notes.Add(
                $"Список игр получен перебором {KnownApps.Count} известных инвентарей, а не со страницы " +
                $"профиля ({why}). Инвентари других игр в отчёт не попали — перезапусти, когда Steam " +
                "перестанет ограничивать запросы.");

        if (discovered.Count == 0)
            throw new InvalidOperationException(
                "Инвентарь пуст, скрыт настройками приватности или Steam временно ограничил запросы (429). " +
                "Попробуй через 10-15 минут.");

        var contexts = discovered
            .Where(c => opt.OnlyApps is null || opt.OnlyApps.Contains(c.AppId))
            .OrderByDescending(c => c.AssetCount)
            .ToList();

        _log($"Найдено инвентарей: {contexts.Count} ({string.Join(", ", contexts.Select(c => $"{c.AppName}:{c.AssetCount}"))})");

        var appNames = new Dictionary<int, string>();
        var all = new List<InventoryItem>();
        foreach (var ctx in contexts)
        {
            appNames[ctx.AppId] = ctx.AppName;
            var cacheKey = $"inv_{profile.SteamId64}_{ctx.AppId}_{ctx.ContextId}_{opt.Language}";
            var cached = opt.InventoryCacheMinutes > 0
                ? _cache.Get<List<InventoryItem>>(cacheKey, TimeSpan.FromMinutes(opt.InventoryCacheMinutes))
                : null;

            if (cached is { Count: > 0 })
            {
                _log($"  {ctx.AppName}: взят из кэша, {cached.Sum(i => i.Count)} шт");
                all.AddRange(cached);
                continue; // Steam не трогаем — именно из-за лимита 429 на /inventory/
            }

            try
            {
                var items = await inv.GetItemsAsync(profile.SteamId64, ctx, opt.Language, ct);
                if (items.Count > 0) _cache.Set(cacheKey, items);
                all.AddRange(items);
            }
            catch (Exception ex)
            {
                report.Notes.Add($"{ctx.AppName}: не прочитан ({ex.Message})");
            }
            await Task.Delay(1200, ct);
        }

        report.TotalItems = all.Sum(i => i.Count);
        report.UniqueItems = all.Count;
        report.TradableItems = all.Where(i => i.Tradable).Sum(i => i.Count);
        report.MarketableItems = all.Where(i => i.Marketable).Sum(i => i.Count);

        // --- цены ---
        var steam = new SteamMarketProvider(_cache, _log, opt.SteamBudget, opt.SteamDelayMs);
        var providers = new List<IPriceProvider>
        {
            new SkinportProvider(_cache),
            new WaxpeerProvider(_cache),
            new MarketCsgoProvider(_cache),
        };
        if (opt.UseSteam) providers.Add(steam);

        var priced = all.Select(i => new PricedItem { Item = i }).ToList();

        foreach (var group in priced.GroupBy(p => p.Item.AppId))
        {
            var appId = group.Key;

            foreach (var provider in providers.Where(p => p.Supports(appId)))
            {
                // Спрашиваем цену только на то, что эта площадка реально примет: у Steam лимит
                // запросов, и тратить его на непередаваемое нет смысла.
                var names = group.Where(p => opt.CountUnsellable || provider.CanSell(p.Item))
                                 .Select(p => p.Item.MarketHashName)
                                 .Where(n => !string.IsNullOrEmpty(n))
                                 .Select(n => n!)
                                 .Distinct(StringComparer.Ordinal)
                                 .ToList();
                if (names.Count == 0) continue;

                try
                {
                    _log($"{appNames.GetValueOrDefault(appId, appId.ToString())} → {provider.Name}: {names.Count} имён");
                    var prices = await provider.GetPricesUsdAsync(appId, names, ct);
                    foreach (var p in group)
                    {
                        if (p.Item.MarketHashName is null) continue;
                        if (!opt.CountUnsellable && !provider.CanSell(p.Item)) continue;
                        if (!prices.TryGetValue(p.Item.MarketHashName, out var list) || list <= 0) continue;
                        p.Quotes.Add(new Quote(provider.Name,
                            Math.Round(list, 2),
                            Math.Round(list * provider.PayoutRate, 2)));
                    }
                }
                catch (Exception ex)
                {
                    report.Notes.Add($"{provider.Name}: {ex.Message}");
                }
            }
        }

        report.SteamSkipped = steam.Skipped;
        if (steam.Skipped > 0)
            report.Notes.Add($"Steam: {steam.Skipped} имён не опрошено (лимит запросов). " +
                             "Запусти ещё раз — кэш накопится и покрытие вырастет.");

        // --- итоги ---
        var fx = new CurrencyService(_cache);
        await fx.LoadAsync(ct);
        report.UsdRub = fx.UsdRub;

        report.PricedItems = priced.Count(p => p.Quotes.Count > 0);
        report.UnpricedItems = priced.Count - report.PricedItems;

        var unsellable = priced.Where(p => !p.Item.Tradable && !p.Item.Marketable).ToList();
        report.UnsellablePositions = unsellable.Count;
        report.UnsellableCount = unsellable.Sum(p => p.Item.Count);
        if (report.UnsellablePositions > 0 && !opt.CountUnsellable)
            report.Notes.Add($"{report.UnsellableCount} шт ({report.UnsellablePositions} позиций) продать нельзя — " +
                             "ни обмена, ни маркета; в суммы не входят.");

        report.BestSplit = fx.Convert(priced.Sum(p => p.BestTotalUsd));

        // Steam платит в кошелёк, остальные — живыми деньгами, поэтому максимум разбивается надвое.
        decimal cash = 0m, mixCash = 0m, mixWallet = 0m, steamOnly = 0m;
        foreach (var p in priced)
        {
            var n = p.Item.Count;
            var bestCash = p.BestCash;
            if (bestCash is not null) cash += bestCash.PayoutUsd * n;

            if (p.Best is { } best)
            {
                if (best.Provider == Marketplaces.Steam) mixWallet += best.PayoutUsd * n;
                else mixCash += best.PayoutUsd * n;
            }

            if (bestCash is null && p.Steam is { } steamOnlyQuote) steamOnly += steamOnlyQuote.PayoutUsd * n;
        }
        report.BestCash = fx.Convert(cash);
        report.MixedCashPart = fx.Convert(mixCash);
        report.MixedWalletPart = fx.Convert(mixWallet);
        report.SteamOnly = fx.Convert(steamOnly);
        report.SteamNet = fx.Convert(priced.Sum(p => p.SteamTotalUsd));
        report.SteamGross = fx.Convert(priced.Sum(p => (p.Steam?.ListUsd ?? 0m) * p.Item.Count));

        var withSteam = priced.Where(p => p.Steam is not null).ToList();
        report.SteamCovered = withSteam.Count;
        report.BestWhereSteamKnown = fx.Convert(withSteam.Sum(p => p.BestTotalUsd));

        foreach (var provider in providers)
        {
            var rows = priced.Select(p => (p, q: p.Quotes.FirstOrDefault(q => q.Provider == provider.Name)))
                             .Where(x => x.q is not null)
                             .ToList();
            if (rows.Count == 0) continue;
            report.ByProvider.Add(new ProviderTotal(
                provider.Name,
                Math.Round(rows.Sum(x => x.q!.ListUsd * x.p.Item.Count), 2),
                Math.Round(rows.Sum(x => x.q!.PayoutUsd * x.p.Item.Count), 2),
                rows.Sum(x => x.p.Item.Count),
                rows.Count));
        }
        report.ByProvider.Sort((a, b) => b.PayoutUsd.CompareTo(a.PayoutUsd));

        foreach (var g in priced.GroupBy(p => p.Item.AppId))
            report.ByApp.Add(new AppTotal(
                g.Key, appNames.GetValueOrDefault(g.Key, g.Key.ToString()),
                g.Sum(p => p.Item.Count),
                Math.Round(g.Sum(p => p.BestTotalUsd), 2),
                Math.Round(g.Sum(p => p.SteamTotalUsd), 2)));
        report.ByApp.Sort((a, b) => b.BestUsd.CompareTo(a.BestUsd));

        report.Items.AddRange(priced.OrderByDescending(p => p.BestTotalUsd));
        return report;
    }
}
