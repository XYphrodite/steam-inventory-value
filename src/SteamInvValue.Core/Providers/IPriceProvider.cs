namespace SteamInvValue.Core.Providers;

/// <summary>Прайс-лист площадки: market_hash_name -> цена листинга в USD.</summary>
public interface IPriceProvider
{
    /// <summary>Отображаемое имя площадки.</summary>
    string Name { get; }

    /// <summary>Доля цены листинга, которая реально доходит до продавца (комиссия площадки).</summary>
    decimal PayoutRate { get; }

    /// <summary>
    /// Сколько получит продавец с этой цены. По умолчанию — простой процент; Steam считает
    /// иначе, у него минимальные комиссии в один цент бьют по дешёвым предметам.
    /// </summary>
    decimal Payout(decimal listUsd) => listUsd * PayoutRate;

    /// <summary>Ссылка на площадку — для отчёта.</summary>
    string Site { get; }

    bool Supports(int appId);

    /// <summary>
    /// Можно ли реально продать этот предмет на площадке. Сторонним нужен обмен,
    /// Steam-маркету — признак marketable. Непродаваемое в суммы не идёт.
    /// </summary>
    bool CanSell(InventoryItem item) => item.Tradable;

    /// <summary>
    /// Возвращает цены для запрошенных имён. Массовые провайдеры игнорируют <paramref name="names"/>
    /// и отдают весь каталог; точечные (Steam) обходят имена по одному.
    /// </summary>
    Task<IReadOnlyDictionary<string, decimal>> GetPricesUsdAsync(
        int appId, IReadOnlyCollection<string> names, CancellationToken ct);
}
