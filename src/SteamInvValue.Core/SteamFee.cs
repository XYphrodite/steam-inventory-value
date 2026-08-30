namespace SteamInvValue.Core;

/// <summary>
/// Сколько продавец получит на руки с продажи на Steam-маркете.
///
/// Плоские «минус 15%» верны только для дорогих предметов. Steam берёт две комиссии —
/// свою 5% и издателя игры 10%, — и у каждой минимум в один цент. Поэтому с продажи за
/// 0,03 $ продавцу приходит 0,01 $: теряется треть, а не пятнадцать процентов. У инвентаря
/// с сотнями карточек это заметно завышало итог.
///
/// Здесь воспроизведена арифметика самого Steam: он считает в целых центах и подбирает
/// сумму так, чтобы «на руки + обе комиссии» в точности совпало с ценой покупателя.
/// </summary>
public static class SteamFee
{
    private const decimal SteamPercent = 0.05m;
    private const decimal PublisherPercent = 0.10m;
    private const int MinimumCents = 1;

    /// <summary>Цена покупателя (USD) -> сколько получит продавец (USD).</summary>
    public static decimal Net(decimal buyerPrice)
    {
        if (buyerPrice <= 0) return 0m;

        var buyerCents = (int)Math.Round(buyerPrice * 100m, MidpointRounding.AwayFromZero);
        return NetCents(buyerCents) / 100m;
    }

    /// <summary>Та же арифметика в целых центах — как её делает Steam.</summary>
    internal static int NetCents(int buyerCents)
    {
        if (buyerCents <= 0) return 0;

        // Первое приближение: сумма без комиссий, если бы они были ровно процентными.
        var net = (int)Math.Floor(buyerCents / (1m + SteamPercent + PublisherPercent));
        if (net < 1) net = 1;

        // Из-за минимумов и округления приближение может не сойтись — подгоняем в обе стороны.
        while (net > 1 && BuyerCentsFor(net) > buyerCents) net--;
        while (BuyerCentsFor(net + 1) <= buyerCents) net++;

        return BuyerCentsFor(net) <= buyerCents ? net : 0;
    }

    /// <summary>Сколько заплатит покупатель, чтобы продавец получил ровно столько центов.</summary>
    private static int BuyerCentsFor(int netCents)
    {
        var steam = Math.Max((int)Math.Floor(netCents * SteamPercent), MinimumCents);
        var publisher = Math.Max((int)Math.Floor(netCents * PublisherPercent), MinimumCents);
        return netCents + steam + publisher;
    }
}
