using System.Globalization;
using System.Text;
using System.Text.Json;
using SteamInvValue.Core;
using SteamInvValue.Core.Providers;

Console.OutputEncoding = Encoding.UTF8;

// ---- разбор аргументов -------------------------------------------------------------------

string? command = null, target = null, jsonOut = null, csvOut = null, configPath = null, nameOpt = null;
int[]? appsOpt = null;
bool? useSteamOpt = null;
int? budgetOpt = null, delayOpt = null, limitOpt = null;
string? langOpt = null, proxyOpt = null, cookieOpt = null;
var top = 20;

var commands = new[] { "add", "rm", "remove", "list", "history", "config", "run", "all" };

for (var i = 0; i < args.Length; i++)
{
    var a = args[i];
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"У ключа {a} нет значения");

    switch (a)
    {
        case "-h" or "--help": Help(); return 0;
        case "--check": return await SelfCheck();
        case "--config": configPath = Next(); break;
        case "--name": nameOpt = Next(); break;
        case "--apps": appsOpt = Next().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray(); break;
        case "--no-steam": useSteamOpt = false; break;
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
            if (a.StartsWith('-')) { Console.Error.WriteLine($"Неизвестный ключ {a}"); return 2; }
            if (command is null && commands.Contains(a)) command = a;
            else if (target is null) target = a;
            else { Console.Error.WriteLine($"Лишний аргумент {a}"); return 2; }
            break;
    }
}

// ---- конфиг читается при входе в приложение ----------------------------------------------

AppConfig config;
try { config = AppConfig.Load(configPath); }
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }

if (proxyOpt is not null || cookieOpt is not null)
    Http.Configure(proxyOpt ?? config.Proxy, cookieOpt ?? config.Cookie);

var storage = new Storage();
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    switch (command)
    {
        case "add": return await Add();
        case "rm" or "remove": return Remove();
        case "list": return List();
        case "history": return ShowHistory();
        case "config": return ShowConfig();
        default: return await Run();
    }
}
catch (OperationCanceledException) { Console.Error.WriteLine("Прервано."); return 130; }
catch (Exception ex) { Console.Error.WriteLine($"Ошибка: {ex.Message}"); return 1; }

// ---- команды -----------------------------------------------------------------------------

async Task<int> Add()
{
    if (target is null) { Console.Error.WriteLine("Укажи ссылку на профиль: steaminv add <ссылка>"); return 2; }
    var p = await config.AddAsync(target, nameOpt, appsOpt, cts.Token);
    Console.WriteLine($"Добавлен: {p.Name} ({p.Id}){(p.Apps is null ? "" : $", игры: {string.Join(',', p.Apps)}")}");
    Console.WriteLine($"Конфиг: {config.Path}");
    return 0;
}

int Remove()
{
    if (target is null) { Console.Error.WriteLine("Укажи имя или SteamID: steaminv rm <ключ>"); return 2; }
    var p = config.Find(target);
    if (p is null) { Console.Error.WriteLine($"Профиль '{target}' не найден."); return 1; }
    config.Remove(target);
    storage.Forget(p.Id);
    Console.WriteLine($"Удалён: {p.Name} ({p.Id})");
    return 0;
}

int List()
{
    if (config.Profiles.Count == 0)
    {
        Console.WriteLine("Список пуст. Добавь: steaminv add https://steamcommunity.com/id/nickname");
        Console.WriteLine($"Конфиг: {config.Path}");
        return 0;
    }

    Console.WriteLine($"{"Имя",-24}{"SteamID64",-20}{"Игры",-12}{"Обновлён",-16}{"Стоимость, ₽",14}");
    foreach (var p in config.Profiles)
    {
        var last = storage.History(p.Id, 1).LastOrDefault();
        Console.WriteLine(
            $"{Trim(p.Name, 23),-24}{p.Id,-20}" +
            $"{(p.Apps is null ? "все" : string.Join(',', p.Apps)),-12}" +
            $"{(last?.At.ToString("dd.MM HH:mm") ?? "—"),-16}" +
            $"{(last is null ? "—" : last.BestRub.ToString("N0")),14}" +
            $"{(p.Enabled ? "" : "  (выключен)")}");
    }
    Console.WriteLine();
    Console.WriteLine($"Конфиг: {config.Path}");
    return 0;
}

int ShowHistory()
{
    var profiles = target is null
        ? config.Profiles
        : config.Find(target) is { } one ? [one] : new List<TrackedProfile>();

    if (profiles.Count == 0) { Console.Error.WriteLine("Нечего показывать."); return 1; }

    foreach (var p in profiles)
    {
        var hist = storage.History(p.Id, limitOpt ?? 30);
        Console.WriteLine();
        Console.WriteLine($"{p.Name} ({p.Id})");
        if (hist.Count == 0) { Console.WriteLine("  истории нет — запусти оценку"); continue; }

        Console.WriteLine($"  {"Дата",-18}{"₽",14}{"$",12}{"изм.",10}{"предметов",11}");
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
    Console.WriteLine($"Файл: {config.Path}");
    Console.WriteLine();
    Console.WriteLine(File.ReadAllText(config.Path));
    Console.WriteLine($"Прокси: {(Http.HasProxy ? "задан" : "нет")}   Cookie Steam: {(Http.HasCookie ? "задан" : "нет")}");
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
        Console.WriteLine("В конфиге нет ни одного инвентаря.");
        Console.WriteLine("Добавь: steaminv add https://steamcommunity.com/id/nickname");
        Console.WriteLine($"Конфиг: {config.Path}");
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
        Console.WriteLine("ИТОГО ПО ВСЕМ ИНВЕНТАРЯМ");
        foreach (var t in totals)
            Console.WriteLine($"  {Trim(t.Name, 30),-32}" +
                              $"{(t.Ok ? t.Rub.ToString("N0") + " ₽" : "не посчитан"),18}" +
                              $"{(t.Ok ? t.Usd.ToString("N2") + " $" : ""),16}");
        Console.WriteLine($"  {"ВСЕГО",-32}{totals.Sum(t => t.Rub),16:N0} ₽{totals.Sum(t => t.Usd),14:N2} $");
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
        Console.Error.WriteLine($"Ошибка ({p.Name}): {ex.Message}");
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
        Console.Error.WriteLine($"JSON: {Path.GetFullPath(jsonOut)}");
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
        Console.Error.WriteLine($"CSV: {Path.GetFullPath(csvOut)}");
    }
}

// ---- вывод -------------------------------------------------------------------------------

static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

static string F(decimal? d) => (d ?? 0m).ToString("0.00", CultureInfo.InvariantCulture);

static void Print(Report r, int top)
{
    string M(Money m) => $"{m.Rub,12:N0} ₽ | {m.Usd,10:N2} $ | {m.Usdt,10:N2} USDT | {m.Btc:0.00000000} BTC";

    Console.WriteLine();
    Console.WriteLine($"Профиль : {r.PersonaName ?? "—"}  {r.ProfileUrl}");
    Console.WriteLine($"Собрано : {r.GeneratedAt:dd.MM.yyyy HH:mm}   курс ЦБ: {r.UsdRub:N2} ₽/$");
    Console.WriteLine(new string('-', 78));
    Console.WriteLine($"Предметов        : {r.TotalItems} шт ({r.UniqueItems} уникальных)");
    Console.WriteLine($"Торгуемых        : {r.TradableItems} шт, продаваемых на маркете: {r.MarketableItems} шт");
    Console.WriteLine($"С ценой          : {r.PricedItems} позиций, без цены: {r.UnpricedItems}");
    Console.WriteLine();
    Console.WriteLine("СТОИМОСТЬ");
    Console.WriteLine($"  Steam, ценник   : {M(r.SteamGross)}");
    Console.WriteLine($"  Steam, на руки  : {M(r.SteamNet)}   (минус 15% комиссии, деньги остаются в кошельке Steam)");
    Console.WriteLine($"  Лучшая площадка : {M(r.BestSplit)}   (каждый предмет продан там, где платят больше — живыми деньгами)");
    if (r.SteamNet.Usd > 0 && r.SteamCovered > 0)
    {
        var gain = (r.BestWhereSteamKnown.Usd / r.SteamNet.Usd - 1) * 100;
        Console.WriteLine($"  Выгода vs Steam : {gain:+0.0;-0.0}%  (по {r.SteamCovered} из {r.PricedItems} позиций, где есть обе цены)");
        if (r.SteamCovered < r.PricedItems)
            Console.WriteLine("                    Steam опрошен не полностью — итог «Steam, на руки» занижен, сравнивай по проценту выше.");
    }

    if (r.ByProvider.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("ПО ПЛОЩАДКАМ (если продать там всё, что площадка принимает)");
        Console.WriteLine($"  {"Площадка",-14}{"ценник, $",14}{"на руки, $",14}{"на руки, ₽",16}{"позиций",10}");
        foreach (var p in r.ByProvider)
            Console.WriteLine($"  {p.Provider,-14}{p.ListUsd,14:N2}{p.PayoutUsd,14:N2}{p.PayoutUsd * r.UsdRub,16:N0}{p.Covered,10}");
    }

    if (r.ByApp.Count > 1)
    {
        Console.WriteLine();
        Console.WriteLine("ПО ИГРАМ");
        foreach (var a in r.ByApp.Where(a => a.Items > 0))
            Console.WriteLine($"  {Trim(a.AppName, 27),-28}{a.Items,7} шт{a.BestUsd,12:N2} $ {a.BestUsd * r.UsdRub,12:N0} ₽");
    }

    var best = r.Items.Where(p => p.BestTotalUsd > 0).Take(top).ToList();
    if (best.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"ТОП-{best.Count} ПО СТОИМОСТИ");
        foreach (var p in best)
            Console.WriteLine($"  {Trim(p.Item.MarketHashName ?? p.Item.Name, 44),-44}" +
                              $"{p.Item.Count,4} x {p.Best!.PayoutUsd,9:N2} $ = {p.BestTotalUsd,10:N2} $  {p.Best.Provider}");
    }

    if (r.Notes.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("ЗАМЕЧАНИЯ");
        foreach (var n in r.Notes) Console.WriteLine($"  - {n}");
    }
    Console.WriteLine();
}

static async Task<int> SelfCheck()
{
    var cache = new FileCache();
    Console.WriteLine("Проверяю источники…");

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
            Console.WriteLine($"  {p.Name,-14} OK  {prices.Count} позиций  пример: {sample.Key} = {sample.Value:N2} $");
        }
        catch (Exception ex) { Console.WriteLine($"  {p.Name,-14} ОШИБКА  {ex.Message}"); }
    }

    try
    {
        var fx = new CurrencyService(cache);
        await fx.LoadAsync();
        Console.WriteLine($"  {"Курсы",-14} OK  1 $ = {fx.UsdRub:N2} ₽, BTC = {fx.BtcUsd:N0} $");
    }
    catch (Exception ex) { Console.WriteLine($"  {"Курсы",-14} ОШИБКА  {ex.Message}"); }

    try
    {
        var steam = new SteamMarketProvider(cache, null, 1, 0);
        var r = await steam.GetPricesUsdAsync(730, ["AK-47 | Redline (Field-Tested)"], CancellationToken.None);
        Console.WriteLine(r.Count > 0
            ? $"  {"Steam Market",-14} OK  AK-47 Redline FT = {r.Values.First():N2} $"
            : $"  {"Steam Market",-14} НЕТ ОТВЕТА (скорее всего лимит 429, попробуй позже)");
    }
    catch (Exception ex) { Console.WriteLine($"  {"Steam Market",-14} ОШИБКА  {ex.Message}"); }

    Console.WriteLine();
    return 0;
}

static void Help() => Console.WriteLine("""
steaminv — оценка инвентарей Steam. Ссылки хранятся в конфиге и читаются при запуске.

  steaminv                       оценить все инвентари из конфига
  steaminv run <ключ>            оценить один (ключ — имя или SteamID64)
  steaminv <ссылка>              разовая оценка, не сохраняя в конфиг

  steaminv add <ссылка> [--name Имя] [--apps 730,753]
  steaminv rm <ключ>
  steaminv list                  что под наблюдением и на сколько
  steaminv history [ключ]        как менялась стоимость от запуска к запуску
  steaminv config                где лежит конфиг и что в нём

Ключи:
  --config ПУТЬ          другой файл конфига (то же: STEAMINV_CONFIG)
  --apps 730,753         ограничить играми на этот запуск
  --no-steam / --steam   спрашивать ли Steam Market
  --steam-budget N       сколько имён максимум спросить у Steam за запуск
  --steam-delay MS       пауза между запросами к Steam
  --lang russian         язык названий предметов
  --proxy URL            ходить в Steam через прокси (http://... или socks5://...)
  --cookie VALUE         steamLoginSecure сессии — снимает лимит 429 на инвентарь
  --top N                строк в топе (по умолчанию 20)
  --limit N              сколько точек истории показать
  --json f.json          выгрузить последний отчёт в JSON
  --csv f.csv            выгрузить предметы в CSV
  --check                живы ли источники цен и курсы
""");
