using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamInvValue.Core;

/// <summary>Точка на графике стоимости инвентаря.</summary>
public sealed record Snapshot(
    DateTimeOffset At,
    decimal BestUsd,
    decimal BestRub,
    decimal SteamNetUsd,
    int Items,
    int Priced,
    int SteamCovered);

/// <summary>
/// Хранилище результатов: последний полный отчёт по каждому профилю (чтобы открывать
/// мгновенно, не дёргая Steam) и история стоимости — по строке на прогон.
/// </summary>
public sealed class Storage
{
    private readonly string _root;

    public Storage(string? root = null)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamInvValue");
        Directory.CreateDirectory(Path.Combine(_root, "reports"));
        Directory.CreateDirectory(Path.Combine(_root, "history"));
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private string ReportPath(string id) => Path.Combine(_root, "reports", $"{id}.json");
    private string PreviousReportPath(string id) => Path.Combine(_root, "reports", $"{id}.prev.json");
    private string HistoryPath(string id) => Path.Combine(_root, "history", $"{id}.jsonl");

    public void SaveReport(string id, Report report)
    {
        // Прошлый отчёт не выбрасываем: без него не сказать, из-за чего изменилась сумма.
        if (File.Exists(ReportPath(id)))
            File.Copy(ReportPath(id), PreviousReportPath(id), overwrite: true);

        File.WriteAllText(ReportPath(id), JsonSerializer.Serialize(report, Json));

        var snap = new Snapshot(
            report.GeneratedAt,
            report.BestSplit.Usd, report.BestSplit.Rub, report.SteamNet.Usd,
            report.TotalItems, report.PricedItems, report.SteamCovered);
        File.AppendAllText(HistoryPath(id), JsonSerializer.Serialize(snap, Json) + Environment.NewLine);
    }

    public Report? LoadReport(string id) => Load(ReportPath(id));

    /// <summary>Отчёт предыдущего прогона — для сравнения замеров.</summary>
    public Report? LoadPreviousReport(string id) => Load(PreviousReportPath(id));

    private static Report? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<Report>(File.ReadAllText(path), Json); }
        catch { return null; }
    }

    public DateTimeOffset? ReportTime(string id)
    {
        var p = ReportPath(id);
        return File.Exists(p) ? new DateTimeOffset(File.GetLastWriteTime(p)) : null;
    }

    public IReadOnlyList<Snapshot> History(string id, int limit = 200)
    {
        var p = HistoryPath(id);
        if (!File.Exists(p)) return [];

        var result = new List<Snapshot>();
        foreach (var line in File.ReadLines(p))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var s = JsonSerializer.Deserialize<Snapshot>(line, Json);
                if (s is not null) result.Add(s);
            }
            catch { /* битую строку пропускаем */ }
        }
        return result.Count > limit ? result[^limit..] : result;
    }

    public void Forget(string id)
    {
        foreach (var p in new[] { ReportPath(id), PreviousReportPath(id), HistoryPath(id) })
            if (File.Exists(p)) File.Delete(p);
    }
}
