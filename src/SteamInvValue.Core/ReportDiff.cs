namespace SteamInvValue.Core;

/// <summary>Одна строка изменений: как позиция выглядела раньше и как сейчас.</summary>
public sealed record DiffLine(
    int AppId,
    string AppName,
    string Name,
    int CountBefore,
    int CountAfter,
    decimal UnitBeforeUsd,
    decimal UnitAfterUsd,
    decimal ValueBeforeUsd,
    decimal ValueAfterUsd)
{
    public decimal DeltaUsd => ValueAfterUsd - ValueBeforeUsd;
    public decimal PricePercent => UnitBeforeUsd > 0 ? (UnitAfterUsd / UnitBeforeUsd - 1) * 100 : 0;
}

/// <summary>
/// Что изменилось между двумя замерами. История отвечает «сумма выросла на 300 рублей», а
/// это — «почему»: пришли новые предметы, ушли старые или просто сдвинулись цены.
/// </summary>
public sealed class ReportDiff
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }

    public List<DiffLine> Appeared { get; } = [];
    public List<DiffLine> Gone { get; } = [];
    public List<DiffLine> CountChanged { get; } = [];
    public List<DiffLine> PriceChanged { get; } = [];

    public Money TotalBefore { get; set; } = new(0, 0, 0, 0);
    public Money TotalAfter { get; set; } = new(0, 0, 0, 0);
    public Money Delta { get; set; } = new(0, 0, 0, 0);

    /// <summary>Часть изменения, вызванная составом инвентаря — что-то пришло или ушло.</summary>
    public Money DeltaFromItems { get; set; } = new(0, 0, 0, 0);

    /// <summary>Часть изменения, вызванная движением цен на том, что лежало и лежит.</summary>
    public Money DeltaFromPrices { get; set; } = new(0, 0, 0, 0);

    public bool IsEmpty => Appeared.Count == 0 && Gone.Count == 0 &&
                           CountChanged.Count == 0 && PriceChanged.Count == 0;

    /// <summary>
    /// Сравнивает два отчёта. Изменение стоимости раскладывается на состав и цены:
    /// для сохранившейся позиции c1*u1 - c0*u0 = u0*(c1-c0) + c1*(u1-u0), где первое
    /// слагаемое — «стало больше или меньше штук», второе — «цена поехала».
    /// </summary>
    public static ReportDiff Compare(Report before, Report after, CurrencyService fx,
        decimal priceThresholdPercent = 1m)
    {
        var diff = new ReportDiff { From = before.GeneratedAt, To = after.GeneratedAt };

        var old = Index(before);
        var now = Index(after);

        decimal fromItems = 0, fromPrices = 0;

        foreach (var (key, cur) in now)
        {
            if (!old.TryGetValue(key, out var prev))
            {
                diff.Appeared.Add(Line(cur, null));
                fromItems += cur.Value;
                continue;
            }

            if (cur.Count != prev.Count)
            {
                diff.CountChanged.Add(Line(cur, prev));
                fromItems += prev.Unit * (cur.Count - prev.Count);
            }

            if (prev.Unit > 0 && cur.Unit != prev.Unit)
            {
                var percent = Math.Abs((cur.Unit / prev.Unit - 1) * 100);
                if (percent >= priceThresholdPercent) diff.PriceChanged.Add(Line(cur, prev));
                fromPrices += cur.Count * (cur.Unit - prev.Unit);
            }
        }

        foreach (var (key, prev) in old)
            if (!now.ContainsKey(key))
            {
                diff.Gone.Add(Line(null, prev));
                fromItems -= prev.Value;
            }

        diff.Appeared.Sort((a, b) => b.ValueAfterUsd.CompareTo(a.ValueAfterUsd));
        diff.Gone.Sort((a, b) => b.ValueBeforeUsd.CompareTo(a.ValueBeforeUsd));
        diff.CountChanged.Sort((a, b) => Math.Abs(b.DeltaUsd).CompareTo(Math.Abs(a.DeltaUsd)));
        diff.PriceChanged.Sort((a, b) => Math.Abs(b.DeltaUsd).CompareTo(Math.Abs(a.DeltaUsd)));

        // Итоги считаем по тем же позициям, что и раскладку, а не по сохранённым суммам
        // отчёта: иначе «изменение» и «состав + цены» могут разойтись между собой.
        var totalBefore = old.Values.Sum(e => e.Value);
        var totalAfter = now.Values.Sum(e => e.Value);

        diff.TotalBefore = fx.Convert(totalBefore);
        diff.TotalAfter = fx.Convert(totalAfter);
        diff.Delta = fx.Convert(totalAfter - totalBefore);
        diff.DeltaFromItems = fx.Convert(fromItems);
        diff.DeltaFromPrices = fx.Convert(fromPrices);

        return diff;
    }

    private static DiffLine Line(Entry? cur, Entry? prev)
    {
        var any = cur ?? prev!;
        return new DiffLine(any.AppId, any.AppName, any.Name,
            prev?.Count ?? 0, cur?.Count ?? 0,
            prev?.Unit ?? 0, cur?.Unit ?? 0,
            prev?.Value ?? 0, cur?.Value ?? 0);
    }

    /// <summary>Позиции с ценой, сведённые по игре и каноническому имени.</summary>
    private static Dictionary<string, Entry> Index(Report report)
    {
        var map = new Dictionary<string, Entry>(StringComparer.Ordinal);
        var appNames = report.ByApp.ToDictionary(a => a.AppId, a => a.AppName);

        foreach (var p in report.Items)
        {
            if (p.BestTotalUsd <= 0) continue;

            var name = p.Item.MarketHashName ?? p.Item.Name;
            var key = $"{p.Item.AppId}|{name}";
            var unit = p.Best?.PayoutUsd ?? 0;

            if (map.TryGetValue(key, out var existing))
                map[key] = existing with { Count = existing.Count + p.Item.Count, Value = existing.Value + p.BestTotalUsd };
            else
                map[key] = new Entry(p.Item.AppId, appNames.GetValueOrDefault(p.Item.AppId, p.Item.AppId.ToString()),
                    name, p.Item.Count, unit, p.BestTotalUsd);
        }

        return map;
    }

    private sealed record Entry(int AppId, string AppName, string Name, int Count, decimal Unit, decimal Value);
}
