using SteamInvValue.Core;

namespace SteamInvValue.Cli;

/// <summary>Строки консоли. Русский и английский лежат рядом — см. <see cref="Loc"/>.</summary>
public static class T
{
    private static string P(string ru, string en) => Loc.Pick(ru, en);

    // --- аргументы и общие ошибки ---
    public static string NoValueFor(string key) => P($"У ключа {key} нет значения", $"Option {key} needs a value");
    public static string UnknownOption(string key) => P($"Неизвестный ключ {key}", $"Unknown option {key}");
    public static string ExtraArgument(string arg) => P($"Лишний аргумент {arg}", $"Unexpected argument {arg}");
    public static string Cancelled => P("Прервано.", "Cancelled.");
    public static string Error(string msg) => P($"Ошибка: {msg}", $"Error: {msg}");
    public static string ErrorFor(string name, string msg) => P($"Ошибка ({name}): {msg}", $"Error ({name}): {msg}");

    // --- add / rm ---
    public static string NeedLink => P(
        "Укажи ссылку на профиль: steaminv add <ссылка>",
        "Provide a profile link: steaminv add <link>");
    public static string Added(string name, string id, string? apps) => P(
        $"Добавлен: {name} ({id})" + (apps is null ? "" : $", игры: {apps}"),
        $"Added: {name} ({id})" + (apps is null ? "" : $", games: {apps}"));
    public static string NeedKey => P(
        "Укажи имя или SteamID: steaminv rm <ключ>",
        "Provide a name or SteamID: steaminv rm <key>");
    public static string NotFound(string key) => P($"Профиль '{key}' не найден.", $"Profile '{key}' not found.");
    public static string Removed(string name, string id) => P($"Удалён: {name} ({id})", $"Removed: {name} ({id})");

    // --- list ---
    public static string EmptyList => P(
        "Список пуст. Добавь: steaminv add https://steamcommunity.com/id/nickname",
        "Nothing is being watched. Add one: steaminv add https://steamcommunity.com/id/nickname");
    public static string ConfigAt(string path) => P($"Конфиг: {path}", $"Config: {path}");
    public static string ColName => P("Имя", "Name");
    public static string ColGames => P("Игры", "Games");
    public static string ColUpdated => P("Обновлён", "Updated");
    public static string ColValue => P("Стоимость, ₽", "Value, RUB");
    public static string AllGames => P("все", "all");
    public static string Disabled => P("  (выключен)", "  (disabled)");

    // --- history ---
    public static string NothingToShow => P("Нечего показывать.", "Nothing to show.");
    public static string NoHistory => P("  истории нет — запусти оценку", "  no history yet — run a valuation");
    public static string ColDate => P("Дата", "Date");
    public static string ColChange => P("изм.", "change");
    public static string ColItems => P("предметов", "items");

    // --- config ---
    public static string FileAt(string path) => P($"Файл: {path}", $"File: {path}");
    public static string ProxyCookie(bool proxy, bool cookie) => P(
        $"Прокси: {(proxy ? "задан" : "нет")}   Cookie Steam: {(cookie ? "задан" : "нет")}",
        $"Proxy: {(proxy ? "set" : "none")}   Steam cookie: {(cookie ? "set" : "none")}");

    // --- прогон ---
    public static string NoInventories => P(
        "В конфиге нет ни одного инвентаря.", "There are no inventories in the config.");
    public static string AddHint => P(
        "Добавь: steaminv add https://steamcommunity.com/id/nickname",
        "Add one: steaminv add https://steamcommunity.com/id/nickname");
    public static string GrandTotalHeader => P("ИТОГО ПО ВСЕМ ИНВЕНТАРЯМ", "ALL INVENTORIES");
    public static string NotCounted => P("не посчитан", "not counted");
    public static string GrandTotal => P("ВСЕГО", "TOTAL");

    // --- отчёт ---
    public static string Profile => P("Профиль", "Profile");
    public static string CollectedAt => P("Собрано", "Collected");
    public static string RateNote(decimal rub) => P($"курс ЦБ: {rub:N2} ₽/$", $"CBR rate: {rub:N2} RUB/USD");
    public static string ItemsLine(int total, int unique) => P(
        $"Предметов        : {total} шт ({unique} уникальных)",
        $"Items            : {total} ({unique} unique)");
    public static string TradableLine(int tradable, int marketable) => P(
        $"Торгуемых        : {tradable} шт, продаваемых на маркете: {marketable} шт",
        $"Tradable         : {tradable}, marketable: {marketable}");
    public static string PricedLine(int priced, int unpriced) => P(
        $"С ценой          : {priced} позиций, без цены: {unpriced}",
        $"Priced           : {priced} positions, no price: {unpriced}");
    public static string UnsellableLine(int items, int positions) => P(
        $"Продать нельзя   : {items} шт ({positions} позиций) — в суммы не входят",
        $"Cannot be sold   : {items} items ({positions} positions) — excluded from totals");

    /// <summary>Хвост строки в топе: продаж за сутки на Steam.</summary>
    public static string Sales(SteamInvValue.Core.PricedItem p) =>
        p.Steam is null ? "" :
        p.SteamVolume > 0 ? P($"{p.SteamVolume} прод./сут", $"{p.SteamVolume} sales/day")
                          : P("не продаётся", "no sales");

    public static string NoSalesLine(int positions, decimal rub) => P(
        $"Не продаётся    : {positions} позиций на {rub:N0} ₽ — за сутки на Steam ни одной продажи",
        $"No buyers       : {positions} positions worth {rub:N0} RUB — zero Steam sales in 24 hours");

    public static string ValueHeader => P("СТОИМОСТЬ", "VALUE");
    public static string CashRow => P("  Живые деньги    : ", "  Real money      : ");
    public static string CashNote => P(
        "   (только сторонние площадки, лучшая цена по каждому предмету)",
        "   (third-party marketplaces only, best price per item)");
    public static string WalletRow => P("  Кошелёк Steam   : ", "  Steam wallet    : ");
    public static string WalletNote => P(
        "   (весь инвентарь на Steam-маркете, минус 15%; вывести нельзя)",
        "   (everything sold on the Steam Market, minus 15%; cannot be withdrawn)");
    public static string GrossRow => P("  Steam, ценник   : ", "  Steam list price: ");
    public static string GrossNote => P(
        "   (столько платит покупатель, до комиссии)", "   (what the buyer pays, before the fee)");
    public static string MaxRow => P("  Максимум всего  : ", "  Maximum overall : ");
    public static string MixNote(decimal cashRub, decimal walletRub) => P(
        $"                    из них {cashRub,10:N0} ₽ живыми деньгами и {walletRub:N0} ₽ в кошелёк Steam",
        $"                    of which {cashRub,10:N0} RUB in real money and {walletRub:N0} RUB into the Steam wallet");
    public static string SteamOnlyNote(decimal rub) => P(
        $"                    {rub:N0} ₽ из них не продать нигде, кроме Steam",
        $"                    {rub:N0} RUB of that cannot be sold anywhere but Steam");
    public static string GainRow(decimal gain, int covered, int priced) => P(
        $"  Выгода vs Steam : {gain:+0.0;-0.0}%  (по {covered} из {priced} позиций, где есть обе цены)",
        $"  Gain vs Steam   : {gain:+0.0;-0.0}%  (over {covered} of {priced} positions priced by both)");
    public static string SkippedNote(int skipped) => P(
        $"                    Steam не опросил {skipped} имён (лимит) — «Кошелёк Steam» занижен, сравнивай по проценту выше.",
        $"                    Steam did not query {skipped} names (rate limit) — \"Steam wallet\" is understated, use the percentage above.");

    public static string ProvidersHeader => P(
        "ПО ПЛОЩАДКАМ (если продать там всё, что площадка принимает)",
        "BY MARKETPLACE (selling everything the marketplace accepts)");
    public static string ColMarketplace => P("Площадка", "Marketplace");
    public static string ColList => P("ценник, $", "list, $");
    public static string ColPayout => P("на руки, $", "payout, $");
    public static string ColPayoutRub => P("на руки, ₽", "payout, RUB");
    public static string ColPositions => P("позиций", "positions");

    public static string AppsHeader => P("ПО ИГРАМ", "BY GAME");
    public static string Pcs => P("шт", "pcs");
    public static string TopHeader(int n) => P($"ТОП-{n} ПО СТОИМОСТИ", $"TOP {n} BY VALUE");
    public static string NotesHeader => P("ЗАМЕЧАНИЯ", "NOTES");

    // --- веб ---
    public static string WebMissing(string dir) => Loc.Pick(
        $"Веб-морда не установлена в {dir}. Поставь её: steaminv update, либо установщиком с -Components web.",
        $"The web app is not installed in {dir}. Get it with: steaminv update, or the installer with -Components web.");

    // --- экспорт ---
    public static string JsonWritten(string path) => P($"JSON: {path}", $"JSON: {path}");
    public static string CsvWritten(string path) => P($"CSV: {path}", $"CSV: {path}");

    // --- self-check ---
    public static string Checking => P("Проверяю источники…", "Checking price sources…");
    public static string CheckOk(int count, string sample, decimal price) => P(
        $"OK  {count} позиций  пример: {sample} = {price:N2} $",
        $"OK  {count} entries  sample: {sample} = {price:N2} $");
    public static string CheckFail(string msg) => P($"ОШИБКА  {msg}", $"FAILED  {msg}");
    public static string Rates => P("Курсы", "FX rates");
    public static string RatesOk(decimal usdRub, decimal btc) => P(
        $"OK  1 $ = {usdRub:N2} ₽, BTC = {btc:N0} $", $"OK  1 $ = {usdRub:N2} RUB, BTC = {btc:N0} $");
    public static string SteamOk(decimal price) => P(
        $"OK  AK-47 Redline FT = {price:N2} $", $"OK  AK-47 Redline FT = {price:N2} $");
    public static string SteamNoAnswer => P(
        "НЕТ ОТВЕТА (скорее всего лимит 429, попробуй позже)",
        "NO ANSWER (most likely the 429 rate limit, try later)");

    public static string Help => Loc.IsEn ? HelpEn : HelpRu;

    private const string HelpRu = """
steaminv — оценка инвентарей Steam. Ссылки хранятся в конфиге и читаются при запуске.

  steaminv                       оценить все инвентари из конфига
  steaminv run <ключ>            оценить один (ключ — имя или SteamID64)
  steaminv <ссылка>              разовая оценка, не сохраняя в конфиг

  steaminv add <ссылка> [--name Имя] [--apps 730,753]
  steaminv rm <ключ>
  steaminv list                  что под наблюдением и на сколько
  steaminv history [ключ]        как менялась стоимость от запуска к запуску
  steaminv config                где лежит конфиг и что в нём
  steaminv web                   открыть веб-панель на http://localhost:5188
  steaminv update                скачать и поставить свежий релиз

Ключи:
  -v, --version          версия программы
  --update-check         проверить обновления в этот запуск
  --no-update-check      не проверять
  --ui ru|en             язык интерфейса (то же: STEAMINV_UI, поле interfaceLanguage)
  --config ПУТЬ          другой файл конфига (то же: STEAMINV_CONFIG)
  --apps 730,753         ограничить играми на этот запуск
  --no-steam / --steam   спрашивать ли Steam Market
  --count-unsellable     считать и то, что продать нельзя (по умолчанию не считается)
  --fresh                перечитать инвентарь заново, не брать из кэша (кэш живёт 30 мин)
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
""";

    private const string HelpEn = """
steaminv — Steam inventory valuation. Profile links live in the config and are read at startup.

  steaminv                       value every inventory in the config
  steaminv run <key>             value one (key is a name or a SteamID64)
  steaminv <link>                one-off valuation, not saved to the config

  steaminv add <link> [--name Name] [--apps 730,753]
  steaminv rm <key>
  steaminv list                  what is watched and what it is worth
  steaminv history [key]         how the value changed from run to run
  steaminv config                where the config lives and what is in it
  steaminv web                   open the web panel at http://localhost:5188
  steaminv update                download and install the latest release

Options:
  -v, --version          program version
  --update-check         check for updates on this run
  --no-update-check      do not check
  --ui ru|en             interface language (also: STEAMINV_UI, interfaceLanguage field)
  --config PATH          use a different config file (also: STEAMINV_CONFIG)
  --apps 730,753         restrict to these games for this run
  --no-steam / --steam   whether to query the Steam Market
  --count-unsellable     also count what cannot be sold (excluded by default)
  --fresh                re-read the inventory instead of using the cache (30 min)
  --steam-budget N       how many names to ask Steam for per run
  --steam-delay MS       pause between Steam Market requests
  --lang russian         language of item names
  --proxy URL            reach Steam through a proxy (http://... or socks5://...)
  --cookie VALUE         steamLoginSecure session cookie — lifts the 429 inventory limit
  --top N                rows in the top list (default 20)
  --limit N              how many history points to show
  --json f.json          export the report to JSON
  --csv f.csv            export the items to CSV
  --check                are the price sources and FX rates alive
""";
}
