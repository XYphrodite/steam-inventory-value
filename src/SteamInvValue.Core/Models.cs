namespace SteamInvValue.Core;

/// <summary>Раздел инвентаря (игра + контекст), найденный в профиле.</summary>
public sealed record InventoryContext(int AppId, string AppName, string ContextId, int AssetCount);

/// <summary>Предмет инвентаря, схлопнутый по classid/instanceid (Count — сколько штук).</summary>
public sealed class InventoryItem
{
    public required int AppId { get; init; }
    public required string ContextId { get; init; }
    public required string ClassId { get; init; }
    public string InstanceId { get; init; } = "0";
    public required string Name { get; init; }
    public string? MarketHashName { get; init; }
    public string? IconUrl { get; init; }
    public string? Type { get; init; }
    public string? Rarity { get; init; }
    public string? Exterior { get; init; }
    public bool Tradable { get; init; }
    public bool Marketable { get; init; }
    public int Count { get; set; }

    public string ImageUrl => string.IsNullOrEmpty(IconUrl)
        ? ""
        : $"https://community.cloudflare.steamstatic.com/economy/image/{IconUrl}/128fx128f";
}

/// <summary>Имена площадок, которые различает расчёт.</summary>
public static class Marketplaces
{
    /// <summary>Steam платит во внутренний кошелёк, а не живыми деньгами — считается отдельно.</summary>
    public const string Steam = "Steam";
}

/// <summary>Цена одной штуки на конкретной площадке.</summary>
public sealed record Quote(string Provider, decimal ListUsd, decimal PayoutUsd);

/// <summary>Предмет вместе со всеми найденными ценами.</summary>
public sealed class PricedItem
{
    public required InventoryItem Item { get; init; }
    public List<Quote> Quotes { get; set; } = [];

    public Quote? Best => Quotes.Count == 0 ? null : Quotes.MaxBy(q => q.PayoutUsd);
    public Quote? Steam => Quotes.FirstOrDefault(q => q.Provider == Marketplaces.Steam);

    /// <summary>Сколько таких предметов продалось на Steam-маркете за сутки. 0 — ни одного.</summary>
    public int SteamVolume { get; set; }

    /// <summary>Цена на Steam есть, а покупателей за сутки не было — продать будет нечем и некому.</summary>
    public bool NoSales => Steam is not null && SteamVolume == 0;

    /// <summary>Позиция входит в минимальный набор, ради которого стоит возиться.</summary>
    public bool InSellPlan { get; set; }
    /// <summary>Лучшее предложение среди тех, кто платит живыми деньгами.</summary>
    public Quote? BestCash => Quotes.Where(q => q.Provider != Marketplaces.Steam).MaxBy(q => q.PayoutUsd);
    public decimal BestTotalUsd => (Best?.PayoutUsd ?? 0m) * Item.Count;
    public decimal SteamTotalUsd => (Steam?.PayoutUsd ?? 0m) * Item.Count;
}

public sealed record Money(decimal Usd, decimal Rub, decimal Btc, decimal Usdt);

public sealed record ProviderTotal(string Provider, decimal ListUsd, decimal PayoutUsd, int Items, int Covered);

public sealed record AppTotal(int AppId, string AppName, int Items, decimal BestUsd, decimal SteamUsd);

public sealed class Report
{
    public required string SteamId64 { get; init; }
    public required string ProfileUrl { get; init; }
    public string? PersonaName { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.Now;

    public int TotalItems { get; set; }
    public int UniqueItems { get; set; }
    public int TradableItems { get; set; }
    public int MarketableItems { get; set; }
    /// <summary>Позиции, которые нельзя продать вообще: без обмена и без маркета.</summary>
    public int UnsellablePositions { get; set; }
    public int UnsellableCount { get; set; }
    public int PricedItems { get; set; }
    public int UnpricedItems { get; set; }

    /// <summary>Максимум по всем площадкам — смесь наличных и кошелька Steam.</summary>
    public Money BestSplit { get; set; } = new(0, 0, 0, 0);
    /// <summary>Часть максимума, которая приходит живыми деньгами.</summary>
    public Money MixedCashPart { get; set; } = new(0, 0, 0, 0);
    /// <summary>Часть максимума, которая уходит в кошелёк Steam.</summary>
    public Money MixedWalletPart { get; set; } = new(0, 0, 0, 0);
    /// <summary>Если продавать только там, где платят живыми деньгами.</summary>
    public Money BestCash { get; set; } = new(0, 0, 0, 0);
    /// <summary>Что нигде, кроме Steam, не продаётся.</summary>
    public Money SteamOnly { get; set; } = new(0, 0, 0, 0);
    /// <summary>Сколько позиций достаточно продать, чтобы получить большую часть денег.</summary>
    public int SellPlanPositions { get; set; }
    public Money SellPlanValue { get; set; } = new(0, 0, 0, 0);
    /// <summary>Их доля во всей сумме, процентов.</summary>
    public decimal SellPlanShare { get; set; }
    /// <summary>Сколько позиций останется в хвосте и сколько они стоят вместе.</summary>
    public int TailPositions { get; set; }
    public Money TailValue { get; set; } = new(0, 0, 0, 0);

    /// <summary>Позиции с ценой Steam, но без единой продажи за сутки.</summary>
    public int NoSalesPositions { get; set; }
    /// <summary>Сколько «стоит» этот неликвид по лучшей цене — цифра, которой не стоит верить.</summary>
    public Money NoSalesValue { get; set; } = new(0, 0, 0, 0);
    /// <summary>Сколько позиций получили цену Steam — без этого сравнение со Steam некорректно.</summary>
    public int SteamCovered { get; set; }
    /// <summary>Сколько имён Steam не успел опросить из-за лимита запросов.</summary>
    public int SteamSkipped { get; set; }
    /// <summary>Лучшая площадка, но только по тем позициям, где известна и цена Steam.</summary>
    public Money BestWhereSteamKnown { get; set; } = new(0, 0, 0, 0);
    public Money SteamNet { get; set; } = new(0, 0, 0, 0);
    public Money SteamGross { get; set; } = new(0, 0, 0, 0);

    public List<ProviderTotal> ByProvider { get; set; } = [];
    public List<AppTotal> ByApp { get; set; } = [];
    public List<PricedItem> Items { get; set; } = [];
    public List<string> Notes { get; set; } = [];
    public decimal UsdRub { get; set; }
}
