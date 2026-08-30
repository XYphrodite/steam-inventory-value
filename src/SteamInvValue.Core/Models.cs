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

/// <summary>Цена одной штуки на конкретной площадке.</summary>
public sealed record Quote(string Provider, decimal ListUsd, decimal PayoutUsd);

/// <summary>Предмет вместе со всеми найденными ценами.</summary>
public sealed class PricedItem
{
    public required InventoryItem Item { get; init; }
    public List<Quote> Quotes { get; } = new();

    public Quote? Best => Quotes.Count == 0 ? null : Quotes.MaxBy(q => q.PayoutUsd);
    public Quote? Steam => Quotes.FirstOrDefault(q => q.Provider == "Steam");
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

    public Money BestSplit { get; set; } = new(0, 0, 0, 0);
    /// <summary>Сколько позиций получили цену Steam — без этого сравнение со Steam некорректно.</summary>
    public int SteamCovered { get; set; }
    /// <summary>Лучшая площадка, но только по тем позициям, где известна и цена Steam.</summary>
    public Money BestWhereSteamKnown { get; set; } = new(0, 0, 0, 0);
    public Money SteamNet { get; set; } = new(0, 0, 0, 0);
    public Money SteamGross { get; set; } = new(0, 0, 0, 0);

    public List<ProviderTotal> ByProvider { get; } = new();
    public List<AppTotal> ByApp { get; } = new();
    public List<PricedItem> Items { get; } = new();
    public List<string> Notes { get; } = new();
    public decimal UsdRub { get; set; }
}
