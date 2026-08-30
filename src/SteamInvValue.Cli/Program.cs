using System.Globalization;
using System.Text;
using System.Text.Json;
using SteamInvValue.Core;
using SteamInvValue.Core.Providers;
using SteamInvValue.Cli;

Console.OutputEncoding = Encoding.UTF8;

// ---- разбор аргументов -------------------------------------------------------------------

string? command = null, target = null, jsonOut = null, csvOut = null, configPath = null, nameOpt = null;
int[]? appsOpt = null;
bool? useSteamOpt = null;
var countUnsellable = false;
int? invCacheMinutes = null;
int? budgetOpt = null, delayOpt = null, limitOpt = null;
string? langOpt = null, proxyOpt = null, cookieOpt = null, uiOpt = null;
var top = 20;

var commands = new[] { "add", "rm", "remove", "list", "history", "config", "run", "all", "update", "web", "diff" };
var helpRequested = false;
var checkRequested = false;
bool? updateCheckOpt = null;
long _lastProgressDraw = 0;

for (var i = 0; i < args.Length; i++)
{
    var a = args[i];
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException(T.NoValueFor(a));

    switch (a)
    {
        case "-h" or "--help": helpRequested = true; break;
        case "--check": checkRequested = true; break;
        case "-v" or "--version": Console.WriteLine(Updater.CurrentVersion); return 0;
        case "--update-check": updateCheckOpt = true; break;
        case "--no-update-check": updateCheckOpt = false; break;
        case "--ui": uiOpt = Next(); break;
        case "--config": configPath = Next(); break;
        case "--name": nameOpt = Next(); break;
        case "--apps": appsOpt = Next().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray(); break;
        case "--no-steam": useSteamOpt = false; break;
        case "--count-unsellable": countUnsellable = true; break;
        case "--fresh": invCacheMinutes = 0; break;
        case "--steam": useSteamOpt = true; break;
        case "--steam-budget": budgetOpt = int.Parse(Next()); break;
        case "--steam-delay": delayOpt = int.Parse(Next()); break;
        case "--lang": langOpt = Next(); break;
        case "--proxy": proxyOpt = Next(); break;
        case "--cookie": cookieOpt = Next(); break;
        case "--json": jsonOut = Next(); break;
        case "--csv": csvOut = Next(); break;
        case "--top": top = int.Parse(Next()); break;
        case "--limit": limitOpt = int.Parse(Next()); break;
        default:
            if (a.StartsWith('-')) { Console.Error.WriteLine(T.UnknownOption(a)); return 2; }
            if (command is null && commands.Contains(a)) command = a;
            else if (target is null) target = a;
            else { Console.Error.WriteLine(T.ExtraArgument(a)); return 2; }
            break;
    }
}

// ---- конфиг читается при входе в приложение ----------------------------------------------

var uiFromEnv = Environment.GetEnvironmentVariable("STEAMINV_UI");
Loc.Lang = Loc.Normalize(uiOpt ?? uiFromEnv);

if (helpRequested) { Help(); return 0; }

AppConfig config;
try { config = AppConfig.Load(configPath); }
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }

// Ключ --ui и переменная окружения важнее того, что записано в конфиге.
if (uiOpt is not null || !string.IsNullOrWhiteSpace(uiFromEnv))
    Loc.Lang = Loc.Normalize(uiOpt ?? uiFromEnv);

if (checkRequested) return await SelfCheck();

if (proxyOpt is not null || cookieOpt is not null)
    Http.Configure(proxyOpt ?? config.Proxy, cookieOpt ?? config.Cookie);

var storage = new Storage();
Updater.CleanupOld();   // подчищаем файлы, оставшиеся от прошлого обновления
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    // Сама команда «update» проверку не запускает — она и так идёт за свежей версией.
    if (command == "update") return await UpdateAsync();
    if (command == "web") return Web();

    var code = command switch
    {
        "add" => await Add(),
        "rm" or "remove" => Remove(),
        "list" => List(),
        "history" => ShowHistory(),
        "diff" => await ShowDiffAsync(),
        "config" => ShowConfig(),
        _ => await Run(),
    };

    await ReportUpdateAsync();
    return code;
}
catch (OperationCanceledException) { Console.Error.WriteLine(T.Cancelled); return 130; }
catch (Exception ex) { Console.Error.WriteLine(T.Error(ex.Message)); return 1; }

// ---- команды -----------------------------------------------------------------------------

async Task<int> Add()
{
    if (target is null) { Console.Error.WriteLine(T.NeedLink); return 2; }
    var p = await config.AddAsync(target, nameOpt, appsOpt, cts.Token);
    Console.WriteLine(T.Added(p.Name, p.Id, p.Apps is null ? null : string.Join(',', p.Apps)));
    Console.WriteLine(T.ConfigAt(config.Path));
    return 0;
}

int Remove()
{
    if (target is null) { Console.Error.WriteLine(T.NeedKey); return 2; }
    var p = config.Find(target);
    if (p is null) { Console.Error.WriteLine(T.NotFound(target)); return 1; }
    config.Remove(target);
    storage.Forget(p.Id);
    Console.WriteLine(T.Removed(p.Name, p.Id));
    return 0;
}

int List()
{
    if (config.Profiles.Count == 0)
    {
        Console.WriteLine(T.EmptyList);
        Console.WriteLine(T.ConfigAt(config.Path));
        return 0;
    }

    Console.WriteLine($"{T.ColName,-24}{"SteamID64",-20}{T.ColGames,-12}{T.ColUpdated,-16}{T.ColValue,14}");
    foreach (var p in config.Profiles)
    {
        var last = storage.History(p.Id, 1).LastOrDefault();
        Console.WriteLine(
            $"{Trim(p.Name, 23),-24}{p.Id,-20}" +
            $"{(p.Apps is null ? T.AllGames : string.Join(',', p.Apps)),-12}" +
            $"{(last?.At.ToString("dd.MM HH:mm") ?? "—"),-16}" +
            $"{(last is null ? "—" : last.BestRub.ToString("N0")),14}" +
            $"{(p.Enabled ? "" : T.Disabled)}");
    }
    Console.WriteLine();
    Console.WriteLine(T.ConfigAt(config.Path));
    return 0;
}

int ShowHistory()
{
    var profiles = target is null
        ? config.Profiles
        : config.Find(target) is { } one ? [one] : new List<TrackedProfile>();

    if (profiles.Count == 0) { Console.Error.WriteLine(T.NothingToShow); return 1; }

    foreach (var p in profiles)
    {
        var hist = storage.History(p.Id, limitOpt ?? 30);
        Console.WriteLine();
        Console.WriteLine($"{p.Name} ({p.Id})");
        if (hist.Count == 0) { Console.WriteLine(T.NoHistory); continue; }

        Console.WriteLine($"  {T.ColDate,-18}{Rub(),14}{"$",12}{T.ColChange,10}{T.ColItems,11}");
        decimal? prev = null;
        foreach (var s in hist)
        {
            var delta = prev is > 0 ? $"{(s.BestUsd / prev.Value - 1) * 100:+0.0;-0.0}%" : "—";
            Console.WriteLine($"  {s.At.ToString("dd.MM.yy HH:mm"),-18}{s.BestRub,14:N0}{s.BestUsd,12:N2}{delta,10}{s.Items,11}");
            prev = s.BestUsd;
        }
    }
    Console.WriteLine();
    return 0;
}

int ShowConfig()
{
    Console.WriteLine(T.FileAt(config.Path));
    Console.WriteLine();
    Console.WriteLine(File.ReadAllText(config.Path));
    Console.WriteLine(T.ProxyCookie(Http.HasProxy, Http.HasCookie));
    return 0;
}

async Task<int> Run()
{
    // Разовая оценка: аргумент, которого нет в конфиге, считаем ссылкой и никуда не сохраняем.
    if (target is not null && config.Find(target) is null)
        return await RunOne(new TrackedProfile { Id = "", Name = target, Input = target, Apps = appsOpt }, save: false);

    var list = target is not null
        ? new List<TrackedProfile> { config.Find(target)! }
        : config.Profiles.Where(p => p.Enabled).ToList();

    if (list.Count == 0)
    {
        Console.WriteLine(T.NoInventories);
        Console.WriteLine(T.AddHint);
        Console.WriteLine(T.ConfigAt(config.Path));
        return 0;
    }

    var totals = new List<(string Name, decimal Usd, decimal Rub, bool Ok)>();
    foreach (var p in list)
    {
        var code = await RunOne(p, save: true, totals);
        if (code != 0 && list.Count == 1) return code;
    }

    if (totals.Count > 1)
    {
        Console.WriteLine(new string('=', 78));
        Console.WriteLine(T.GrandTotalHeader);
        foreach (var t in totals)
            Console.WriteLine($"  {Trim(t.Name, 30),-32}" +
                              $"{(t.Ok ? t.Rub.ToString("N0") + " " + Rub() : T.NotCounted),18}" +
                              $"{(t.Ok ? t.Usd.ToString("N2") + " $" : ""),16}");
        Console.WriteLine($"  {T.GrandTotal,-32}{totals.Sum(t => t.Rub),15:N0} {Rub()}{totals.Sum(t => t.Usd),14:N2} $");
        Console.WriteLine();
    }
    return 0;
}

async Task<int> RunOne(TrackedProfile p, bool save,
    List<(string Name, decimal Usd, decimal Rub, bool Ok)>? totals = null)
{
    var opt = config.OptionsFor(p);
    if (useSteamOpt is not null) opt.UseSteam = useSteamOpt.Value;
    if (budgetOpt is not null) opt.SteamBudget = budgetOpt.Value;
    if (delayOpt is not null) opt.SteamDelayMs = delayOpt.Value;
    if (langOpt is not null) opt.Language = langOpt;
    if (appsOpt is not null) opt.OnlyApps = appsOpt;
    opt.CountUnsellable = countUnsellable;
    if (invCacheMinutes is not null) opt.InventoryCacheMinutes = invCacheMinutes.Value;

    Console.Error.WriteLine();
    Console.Error.WriteLine($"=== {p.Name} ===");
    try
    {
        var report = await new Valuator(log: m => Console.Error.WriteLine(m))
            .ValuateAsync(string.IsNullOrEmpty(p.Input) ? p.Id : p.Input, opt, cts.Token);

        Print(report, top);

        if (save && !string.IsNullOrEmpty(p.Id))
        {
            storage.SaveReport(p.Id, report);
            p.LastRun = report.GeneratedAt;
            config.Save();
        }

        totals?.Add((p.Name, report.BestSplit.Usd, report.BestSplit.Rub, true));
        await Export(report);
        return 0;
    }
    catch (OperationCanceledException) { throw; }
    catch (Exception ex)
    {
        Console.Error.WriteLine(T.ErrorFor(p.Name, ex.Message));
        totals?.Add((p.Name, 0, 0, false));
        return 1;
    }
}

async Task Export(Report report)
{
    if (jsonOut is not null)
    {
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        await File.WriteAllTextAsync(jsonOut, json, Encoding.UTF8);
        Console.Error.WriteLine(T.JsonWritten(Path.GetFullPath(jsonOut)));
    }

    if (csvOut is not null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("app;name;count;tradable;marketable;best_provider;best_unit_usd;best_total_usd;steam_unit_usd;steam_total_usd");
        foreach (var p in report.Items)
            sb.AppendLine(string.Join(';', [
                p.Item.AppId.ToString(),
                "\"" + (p.Item.MarketHashName ?? p.Item.Name).Replace("\"", "\"\"") + "\"",
                p.Item.Count.ToString(),
                p.Item.Tradable ? "1" : "0",
                p.Item.Marketable ? "1" : "0",
                p.Best?.Provider ?? "",
                F(p.Best?.PayoutUsd), F(p.BestTotalUsd), F(p.Steam?.PayoutUsd), F(p.SteamTotalUsd),
            ]));
        await File.WriteAllTextAsync(csvOut, sb.ToString(), Encoding.UTF8);
        Console.Error.WriteLine(T.CsvWritten(Path.GetFullPath(csvOut)));
    }
}

/// <summary>
/// Что изменилось между последними двумя замерами. История говорит «сумма выросла», а
/// это отвечает, из-за чего именно: пришли вещи, ушли вещи или сдвинулись цены.
/// </summary>
async Task<int> ShowDiffAsync()
{
    var profiles = target is null
        ? config.Profiles
        : config.Find(target) is { } one ? [one] : new List<TrackedProfile>();

    if (profiles.Count == 0) { Console.Error.WriteLine(T.NothingToShow); return 1; }

    var fx = new CurrencyService(new FileCache());
    await fx.LoadAsync(cts.Token);

    foreach (var profile in profiles)
    {
        Console.WriteLine();
        Console.WriteLine($"{profile.Name} ({profile.Id})");

        var current = storage.LoadReport(profile.Id);
        var previous = storage.LoadPreviousReport(profile.Id);
        if (current is null || previous is null) { Console.WriteLine(T.DiffNoPrevious); continue; }

        var diff = ReportDiff.Compare(previous, current, fx);

        Console.WriteLine(T.DiffHeader(diff.From, diff.To));
        Console.WriteLine(T.DiffTotals(diff.TotalBefore.Rub, diff.TotalAfter.Rub, diff.Delta.Rub));
        Console.WriteLine(T.DiffSplit(diff.DeltaFromItems.Rub, diff.DeltaFromPrices.Rub));

        if (diff.IsEmpty) { Console.WriteLine(T.DiffNothing); continue; }

        void Section(string header, IReadOnlyList<DiffLine> lines, Func<DiffLine, string> right)
        {
            if (lines.Count == 0) return;
            Console.WriteLine();
            Console.WriteLine(header);
            foreach (var line in lines.Take(10))
                Console.WriteLine($"    {Trim(line.Name, 42),-42}{right(line)}");
        }

        var rate = current.UsdRub;
        Section(T.DiffAppeared(diff.Appeared.Count), diff.Appeared,
            l => $"{l.CountAfter,4} шт{l.ValueAfterUsd * rate,9:N0} {Rub()}");
        Section(T.DiffGone(diff.Gone.Count), diff.Gone,
            l => $"{l.CountBefore,4} шт{-l.ValueBeforeUsd * rate,9:N0} {Rub()}");
        Section(T.DiffCount(diff.CountChanged.Count), diff.CountChanged,
            l => $"{l.CountBefore} -> {l.CountAfter} шт{l.DeltaUsd * rate,9:N0} {Rub()}");
        Section(T.DiffPrice(diff.PriceChanged.Count), diff.PriceChanged,
            l => $"{l.PricePercent,7:+0.0;-0.0}%{l.DeltaUsd * rate,9:N0} {Rub()}");

        Console.WriteLine();
    }

    return 0;
}

// ---- обновления ---------------------------------------------------------------------------

/// <summary>
/// Проверка обновлений — сетевой запрос, которого пользователь не заказывал, поэтому один
/// раз спрашиваем разрешение и запоминаем ответ в конфиге.
/// </summary>
async Task ReportUpdateAsync()
{
    var allowed = updateCheckOpt ?? config.CheckUpdates;

    if (allowed is null)
    {
        // В неинтерактивном запуске (планировщик, пайп) спрашивать некого — молчим.
        if (Console.IsInputRedirected || Console.IsOutputRedirected) return;

        Console.Error.WriteLine();
        Console.Error.Write(S.AskUpdates + " ");
        var answer = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
        allowed = answer.StartsWith('y') || answer.StartsWith('д');
        config.CheckUpdates = allowed;
        config.Save();
        Console.Error.WriteLine(allowed.Value ? S.UpdatesOn : S.UpdatesOff);
        if (!allowed.Value) return;
    }

    if (allowed != true) return;

    try
    {
        var info = await Updater.CheckAsync(new FileCache(), cts.Token);
        if (info?.IsNewer == true) Console.Error.WriteLine(S.UpdateAvailable(info.Latest, info.Current));
    }
    catch { /* обновление — не повод ломать прогон */ }
}

/// <summary>
/// Поднимает веб-морду: запускает соседний exe в этой же консоли, чтобы вывод и Ctrl+C
/// работали как у обычной команды. Отдельная программа нужна из-за ASP.NET — тащить его
/// в консоль значило бы удвоить её размер ради того, кто им не пользуется.
/// </summary>
int Web()
{
    var exe = Path.Combine(Updater.InstallDir, "SteamInvValue.Web.exe");
    if (!File.Exists(exe))
    {
        Console.Error.WriteLine(T.WebMissing(Updater.InstallDir));
        return 1;
    }

    var info = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false };

    // steaminv web 5300  или  steaminv web http://localhost:5300 — адрес уходит переменной,
    // потому что аргументы командной строки перехватил бы сам ASP.NET.
    if (target is not null)
        info.Environment["STEAMINV_URL"] = int.TryParse(target, out var port)
            ? $"http://localhost:{port}"
            : target;

    using var process = System.Diagnostics.Process.Start(info);
    if (process is null) return 1;

    process.WaitForExit();
    return process.ExitCode;
}

/// <summary>
/// Обновляет себя сам, без сторонних процессов и лишних окон. Работающий exe нельзя
/// перезаписать, но можно переименовать — старый уезжает в .old и удаляется при следующем
/// запуске. Поэтому ничего закрывать не нужно: программа доживает своё, а на её месте уже
/// лежит новая версия.
/// </summary>
async Task<int> UpdateAsync()
{
    var dir = Updater.InstallDir;

    try
    {
        Console.WriteLine(S.UpdateLooking);
        var (tag, assets) = await Updater.GetLatestAsync(cts.Token);

        if (!Updater.IsNewer(tag, Updater.CurrentVersion))
        {
            Console.WriteLine(S.UpdateAlready(Updater.CurrentVersion));
            return 0;
        }

        Console.WriteLine(S.UpdateFound(Updater.CurrentVersion, tag));

        // Обновляем ровно то, что установлено рядом.
        var targets = new List<(string Exe, string Asset)>();
        if (File.Exists(Path.Combine(dir, "steaminv.exe")))
            targets.Add(("steaminv.exe", "steaminv-cli-win-x64.zip"));
        if (File.Exists(Path.Combine(dir, "SteamInvValue.Web.exe")))
            targets.Add(("SteamInvValue.Web.exe", "steaminv-web-win-x64.zip"));
        if (targets.Count == 0) targets.Add(("steaminv.exe", "steaminv-cli-win-x64.zip"));

        var temp = Path.Combine(Path.GetTempPath(), "steaminv-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(temp);

        try
        {
            foreach (var (exe, assetName) in targets)
            {
                var asset = assets.FirstOrDefault(a => a.Name == assetName)
                            ?? throw new InvalidOperationException($"{assetName}: нет в релизе {tag}");

                Console.WriteLine(S.UpdateDownloading(asset.Name, asset.Size / 1024.0 / 1024.0));

                var zip = Path.Combine(temp, asset.Name);
                await Updater.DownloadAsync(asset.Url, zip, ShowProgress, cts.Token);
                ClearProgress();

                var unpacked = Path.Combine(temp, Path.GetFileNameWithoutExtension(asset.Name));
                System.IO.Compression.ZipFile.ExtractToDirectory(zip, unpacked, overwriteFiles: true);

                Updater.ReplaceFile(Path.Combine(dir, exe), Path.Combine(unpacked, exe));
                Console.WriteLine(S.UpdateReplaced(exe));
            }
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { /* временное, переживём */ }
        }

        Console.WriteLine(S.UpdateFinished(tag));

        var webRunning = System.Diagnostics.Process.GetProcessesByName("SteamInvValue.Web").Length > 0;
        if (webRunning) Console.WriteLine(S.UpdateRestartWeb);

        return 0;
    }
    catch (OperationCanceledException) { throw; }
    catch (Exception ex)
    {
        Console.Error.WriteLine(S.UpdateFailed(ex.Message));
        return 1;
    }
}

/// <summary>Однострочный прогресс скачивания; в перенаправленный вывод не пишется.</summary>
void ShowProgress(long done, long total)
{
    if (Console.IsOutputRedirected || total <= 0) return;
    if (Environment.TickCount64 - _lastProgressDraw < 150 && done < total) return;
    _lastProgressDraw = Environment.TickCount64;

    var percent = (int)(100 * done / total);
    var bar = new string('#', percent / 5).PadRight(20, '.');
    Console.Write($"\r  [{bar}] {percent,3}%  {done / 1024.0 / 1024.0,5:N1} / {total / 1024.0 / 1024.0:N1} MB");
}

void ClearProgress()
{
    if (Console.IsOutputRedirected) return;
    Console.Write("\r" + new string(' ', 60) + "\r");
}

// ---- вывод -------------------------------------------------------------------------------

static string Rub() => Loc.IsEn ? "RUB" : "₽";

static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

static string F(decimal? d) => (d ?? 0m).ToString("0.00", CultureInfo.InvariantCulture);

static void Print(Report r, int top)
{
    string M(Money m) => $"{m.Rub,12:N0} {Rub()} | {m.Usd,10:N2} $ | {m.Usdt,10:N2} USDT | {m.Btc:0.00000000} BTC";

    Console.WriteLine();
    Console.WriteLine($"{T.Profile} : {r.PersonaName ?? "—"}  {r.ProfileUrl}");
    Console.WriteLine($"{T.CollectedAt} : {r.GeneratedAt:dd.MM.yyyy HH:mm}   {T.RateNote(r.UsdRub)}");
    Console.WriteLine(new string('-', 78));
    Console.WriteLine(T.ItemsLine(r.TotalItems, r.UniqueItems));
    Console.WriteLine(T.TradableLine(r.TradableItems, r.MarketableItems));
    Console.WriteLine(T.PricedLine(r.PricedItems, r.UnpricedItems));
    if (r.UnsellableCount > 0)
        Console.WriteLine(T.UnsellableLine(r.UnsellableCount, r.UnsellablePositions));
    if (r.NoSalesPositions > 0)
        Console.WriteLine(T.NoSalesLine(r.NoSalesPositions, r.NoSalesValue.Rub));
    if (r.LockedPositions > 0 && r.LockedUntilNearest is { } until)
        Console.WriteLine(T.LockedLine(r.LockedCount, r.LockedPositions, until));
    Console.WriteLine();
    Console.WriteLine(T.ValueHeader);
    Console.WriteLine(T.CashRow + M(r.BestCash) + T.CashNote);
    Console.WriteLine(T.WalletRow + M(r.SteamNet) + T.WalletNote);
    Console.WriteLine(T.GrossRow + M(r.SteamGross) + T.GrossNote);
    Console.WriteLine(T.MaxRow + M(r.BestSplit));
    Console.WriteLine(T.MixNote(r.MixedCashPart.Rub, r.MixedWalletPart.Rub));
    if (r.SteamOnly.Rub > 0)
        Console.WriteLine(T.SteamOnlyNote(r.SteamOnly.Rub));
    if (r.SteamNet.Usd > 0 && r.SteamCovered > 0)
    {
        var gain = (r.BestWhereSteamKnown.Usd / r.SteamNet.Usd - 1) * 100;
        Console.WriteLine(T.GainRow(gain, r.SteamCovered, r.PricedItems));
        if (r.SteamSkipped > 0)
            Console.WriteLine(T.SkippedNote(r.SteamSkipped));
    }

    if (r.SellPlanPositions > 0)
    {
        Console.WriteLine();
        Console.WriteLine(T.SellPlanHeader(r.SellPlanPositions, r.SellPlanShare));
        foreach (var p in r.Items.Where(p => p.InSellPlan).Take(15))
            Console.WriteLine($"  {Trim(p.Item.MarketHashName ?? p.Item.Name, 44),-44}" +
                              $"{p.BestTotalUsd * r.UsdRub,9:N0} {Rub()}  {p.Best!.Provider,-12}{T.Sales(p)}");
        if (r.TailPositions > 0) Console.WriteLine(T.SellPlanTail(r.TailPositions, r.TailValue.Rub));
    }

    if (r.ByProvider.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine(T.ProvidersHeader);
        Console.WriteLine($"  {T.ColMarketplace,-14}{T.ColList,14}{T.ColPayout,14}{T.ColPayoutRub,16}{T.ColPositions,10}");
        foreach (var p in r.ByProvider)
            Console.WriteLine($"  {p.Provider,-14}{p.ListUsd,14:N2}{p.PayoutUsd,14:N2}{p.PayoutUsd * r.UsdRub,16:N0}{p.Covered,10}");
    }

    if (r.ByApp.Count > 1)
    {
        Console.WriteLine();
        Console.WriteLine(T.AppsHeader);
        foreach (var a in r.ByApp.Where(a => a.Items > 0))
            Console.WriteLine($"  {Trim(a.AppName, 27),-28}{a.Items,7} {T.Pcs}{a.BestUsd,12:N2} $ {a.BestUsd * r.UsdRub,12:N0} {Rub()}");
    }

    var best = r.Items.Where(p => p.BestTotalUsd > 0).Take(top).ToList();
    if (best.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine(T.TopHeader(best.Count));
        foreach (var p in best)
            Console.WriteLine($"  {Trim(p.Item.MarketHashName ?? p.Item.Name, 40),-40}" +
                              $"{p.Item.Count,4} x {p.Best!.PayoutUsd,9:N2} $ = {p.BestTotalUsd,10:N2} $  " +
                              $"{p.Best.Provider,-12}{T.Sales(p)}");
    }

    if (r.Notes.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine(T.NotesHeader);
        foreach (var n in r.Notes) Console.WriteLine($"  - {n}");
    }
    Console.WriteLine();
}

static async Task<int> SelfCheck()
{
    var cache = new FileCache();
    Console.WriteLine(T.Checking);

    IPriceProvider[] providers =
    [
        new SkinportProvider(cache),
        new WaxpeerProvider(cache),
        new MarketCsgoProvider(cache),
    ];

    foreach (var p in providers)
    {
        try
        {
            var prices = await p.GetPricesUsdAsync(730, [], CancellationToken.None);
            var sample = prices.FirstOrDefault();
            Console.WriteLine($"  {p.Name,-14} {T.CheckOk(prices.Count, sample.Key, sample.Value)}");
        }
        catch (Exception ex) { Console.WriteLine($"  {p.Name,-14} {T.CheckFail(ex.Message)}"); }
    }

    try
    {
        var fx = new CurrencyService(cache);
        await fx.LoadAsync();
        Console.WriteLine($"  {T.Rates,-14} {T.RatesOk(fx.UsdRub, fx.BtcUsd)}");
    }
    catch (Exception ex) { Console.WriteLine($"  {T.Rates,-14} {T.CheckFail(ex.Message)}"); }

    try
    {
        var steam = new SteamMarketProvider(cache, null, 1, 0);
        var r = await steam.GetPricesUsdAsync(730, ["AK-47 | Redline (Field-Tested)"], CancellationToken.None);
        Console.WriteLine(r.Count > 0
            ? $"  {"Steam Market",-14} {T.SteamOk(r.Values.First())}"
            : $"  {"Steam Market",-14} {T.SteamNoAnswer}");
    }
    catch (Exception ex) { Console.WriteLine($"  {"Steam Market",-14} {T.CheckFail(ex.Message)}"); }

    Console.WriteLine();
    return 0;
}

static void Help() => Console.WriteLine(T.Help);
