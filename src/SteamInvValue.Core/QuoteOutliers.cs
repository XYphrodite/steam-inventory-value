namespace SteamInvValue.Core;

/// <summary>
/// Отсев цен, которым нельзя верить.
///
/// «Живые деньги» берут лучшее предложение по каждому предмету, а лучшее из четырёх оценок
/// систематически завышено: побеждает та площадка, которая в этот момент оптимистичнее всех.
/// На копеечных скинах это не случайность, а разметка ботов — в прайсе Waxpeer одна и та же
/// цена стоит у сотни разных предметов сразу, и она может быть в десятки раз выше того, что за
/// ту же вещь просят на других площадках.
///
/// Поэтому цена, которая заметно выше согласного мнения остальных, из выбора лучшей убирается.
/// Сравниваем с медианой, а не со средним: среднее сам выброс и утащит. Нужны минимум два
/// других мнения — по одному расхождению не понять, кто из двоих неправ, и отбрасывать наугад
/// хуже, чем оставить как есть.
/// </summary>
public static class QuoteOutliers
{
    /// <summary>Во сколько раз цена должна превысить медиану остальных, чтобы ей не верить.</summary>
    public const decimal Factor = 3m;

    /// <summary>Сколько других цен нужно, чтобы сравнение вообще имело смысл.</summary>
    public const int MinOthers = 2;

    /// <summary>Помечает недостоверные цены. Возвращает, сколько отбросил.</summary>
    public static int Mark(PricedItem item, decimal factor = Factor)
    {
        if (item.Quotes.Count <= MinOthers) return 0;

        var marked = 0;
        foreach (var quote in item.Quotes)
        {
            var others = item.Quotes.Where(o => !ReferenceEquals(o, quote))
                                    .Select(o => o.ListUsd)
                                    .Where(v => v > 0)
                                    .ToList();
            if (others.Count < MinOthers) continue;

            var median = Median(others);
            if (median <= 0 || quote.ListUsd <= factor * median) continue;

            item.Outliers.Add(quote.Provider);
            marked++;
        }

        return marked;
    }

    private static decimal Median(List<decimal> values)
    {
        values.Sort();
        var middle = values.Count / 2;
        return values.Count % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2m;
    }
}
