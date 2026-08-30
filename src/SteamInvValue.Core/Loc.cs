namespace SteamInvValue.Core;

/// <summary>
/// Язык интерфейса. Строки лежат парами «русский / английский» рядом, чтобы перевод нельзя
/// было потерять при правке: меняешь сообщение — видишь оба варианта.
/// </summary>
public static class Loc
{
    private static string _lang = "ru";

    /// <summary>
    /// "ru" или "en". Ставится один раз при старте из конфига. Заодно переключает формат чисел
    /// и дат, иначе английский отчёт печатал бы «1 834,00» вместо «1,834.00».
    /// </summary>
    public static string Lang
    {
        get => _lang;
        set
        {
            _lang = Normalize(value);
            var culture = System.Globalization.CultureInfo.GetCultureInfo(IsEn ? "en-US" : "ru-RU");
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
            System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
        }
    }

    public static bool IsEn => _lang.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    public static string Pick(string ru, string en) => IsEn ? en : ru;

    /// <summary>Приводит что угодно к "ru"/"en"; null — берём язык системы.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";
        return value.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "ru";
    }
}

/// <summary>Сообщения ядра: чтение инвентаря, опрос цен, замечания к отчёту.</summary>
public static class S
{
    public static string ResolvingProfile => Loc.Pick("Определяю профиль…", "Resolving profile…");

    public static string NoPersona => Loc.Pick("без ника", "no persona");

    public static string CannotResolve(string input) => Loc.Pick(
        $"Не удалось определить SteamID по '{input}'. Профиль не найден или скрыт.",
        $"Could not resolve a SteamID from '{input}'. The profile does not exist or is hidden.");

    public static string ProfilePrivate => Loc.Pick(
        "Профиль или инвентарь закрыт настройками приватности.",
        "The profile or its inventory is private.");

    public static string InventoryForbidden => Loc.Pick(
        "Инвентарь закрыт (403).", "The inventory is private (403).");

    public static string CannotReadAppList => Loc.Pick(
        "Не удалось прочитать список инвентарей со страницы профиля.",
        "Could not read the inventory list from the profile page.");

    public static string EmptyAppList => Loc.Pick(
        "страница профиля вернула пустой список", "the profile page returned an empty list");

    public static string NothingFound => Loc.Pick(
        "Инвентарь пуст, скрыт настройками приватности или Steam временно ограничил запросы (429). " +
        "Попробуй через 10-15 минут.",
        "The inventory is empty, private, or Steam is rate-limiting requests (429). Try again in 10-15 minutes.");

    public static string ProbingApps(string why) => Loc.Pick(
        $"Список игр со страницы профиля не получен ({why}) — перебираю известные игры.",
        $"Could not get the game list from the profile page ({why}) — probing known games.");

    public static string ProbedNote(int known, string why) => Loc.Pick(
        $"Список игр получен перебором {known} известных инвентарей, а не со страницы профиля ({why}). " +
        "Инвентари других игр в отчёт не попали — перезапусти, когда Steam перестанет ограничивать запросы.",
        $"The game list came from probing {known} known inventories instead of the profile page ({why}). " +
        "Inventories of other games are missing from this report — re-run once Steam stops rate-limiting.");

    public static string ContextsFromCache(int count) => Loc.Pick(
        $"Список игр взят из кэша: {count}", $"Game list taken from cache: {count}");

    public static string FoundInventories(int count, string list) => Loc.Pick(
        $"Найдено инвентарей: {count} ({list})", $"Inventories found: {count} ({list})");

    public static string FoundInventory(string app, int count) => Loc.Pick(
        $"  найден инвентарь {app}: {count} шт", $"  found inventory {app}: {count} items");

    public static string FromCache(string app, int count) => Loc.Pick(
        $"  {app}: взят из кэша, {count} шт", $"  {app}: taken from cache, {count} items");

    public static string PageRead(string app, int page, int total) => Loc.Pick(
        $"  {app}: страница {page}, предметов {total}", $"  {app}: page {page}, {total} items so far");

    public static string NotRead(string app, string error) => Loc.Pick(
        $"{app}: не прочитан ({error})", $"{app}: could not be read ({error})");

    public static string SteamSaid(int code, double seconds) => Loc.Pick(
        $"  Steam ответил {code}, жду {seconds:0}с", $"  Steam returned {code}, waiting {seconds:0}s");

    public static string AskingProvider(string app, string provider, int names) => Loc.Pick(
        $"{app} → {provider}: {names} имён", $"{app} → {provider}: {names} names");

    public static string SteamPlan(int cached, int asking, int skipped, double minutes) => Loc.Pick(
        $"  Steam: из кэша {cached}, опрашиваю {asking}" + (skipped > 0 ? $", отложено {skipped}" : "") +
        $" (~{minutes:0.0} мин)",
        $"  Steam: {cached} from cache, querying {asking}" + (skipped > 0 ? $", {skipped} deferred" : "") +
        $" (~{minutes:0.0} min)");

    public static string SteamThrottled(double seconds) => Loc.Pick(
        $"  Steam: 429, пауза {seconds:0}с", $"  Steam: 429, pausing {seconds:0}s");

    public static string SteamGivingUp => Loc.Pick(
        "  Steam: подряд 5 неудач — прекращаю опрос, остальное возьмётся в следующий раз из кэша.",
        "  Steam: 5 failures in a row — stopping; the rest will come from cache on the next run.");

    public static string SteamProgress(int done, int total) => Loc.Pick(
        $"  Steam: {done}/{total}", $"  Steam: {done}/{total}");

    public static string DelayTuned(int from, int to) => Loc.Pick(
        $"  Steam: пауза подстроилась {from} → {to} мс",
        $"  Steam: delay tuned {from} -> {to} ms");

    public static string SteamSkippedNote(int skipped) => Loc.Pick(
        $"Steam: {skipped} имён не опрошено (лимит запросов). Запусти ещё раз — кэш накопится и покрытие вырастет.",
        $"Steam: {skipped} names were not queried (rate limit). Run again — the cache grows and coverage improves.");

    public static string UnsellableNote(int items, int positions) => Loc.Pick(
        $"{items} шт ({positions} позиций) продать нельзя — ни обмена, ни маркета; в суммы не входят.",
        $"{items} items ({positions} positions) cannot be sold — no trading, no market; excluded from the totals.");

    public static string AskUpdates => Loc.Pick(
        "Проверять обновления при запуске? Это запрос к api.github.com не чаще раза в сутки. [д/н]",
        "Check for updates on start? That is one request to api.github.com per day at most. [y/n]");

    public static string UpdatesOn => Loc.Pick(
        "Хорошо, буду проверять. Выключить: поле checkUpdates в конфиге или ключ --no-update-check.",
        "Fine, I will check. To turn it off: the checkUpdates field in the config or --no-update-check.");

    public static string UpdatesOff => Loc.Pick(
        "Понял, не проверяю. Включить: поле checkUpdates в конфиге или ключ --update-check.",
        "Understood, no checks. To enable: the checkUpdates field in the config or --update-check.");

    public static string UpdateAvailable(string latest, string current) => Loc.Pick(
        $"Доступна версия {latest}, у тебя {current}. Обновиться: steaminv update",
        $"Version {latest} is available, you have {current}. Update with: steaminv update");

    public static string UpToDate(string current) => Loc.Pick(
        $"Версия {current} — обновлений нет.", $"Version {current} — no updates.");

    public static string UpdateLooking => Loc.Pick("Смотрю, что есть в релизах…", "Checking the releases…");

    public static string UpdateAlready(string current) => Loc.Pick(
        $"Уже последняя версия: {current}.", $"Already on the latest version: {current}.");

    public static string UpdateFound(string from, string to) => Loc.Pick(
        $"Обновляю {from} → {to}", $"Updating {from} -> {to}");

    public static string UpdateDownloading(string name, double mb) => Loc.Pick(
        $"  качаю {name} ({mb:N1} МБ)", $"  downloading {name} ({mb:N1} MB)");

    public static string UpdateReplaced(string name) => Loc.Pick(
        $"  заменено: {name}", $"  replaced: {name}");

    public static string UpdateFinished(string version) => Loc.Pick(
        $"Готово, версия {version}. Она заработает при следующем запуске.",
        $"Done, version {version}. It takes effect the next time you start the program.");

    public static string UpdateRestartWeb => Loc.Pick(
        "Веб-морда запущена — перезапусти её, чтобы подхватила новую версию.",
        "The web app is running — restart it to pick up the new version.");

    public static string GitHubRateLimited => Loc.Pick(
        "GitHub временно ограничил запросы (60 в час на адрес) — попробуй через час.",
        "GitHub is rate-limiting requests (60 per hour per address) — try again in an hour.");

    public static string UpdateFailed(string error) => Loc.Pick(
        $"Обновить не вышло: {error}", $"Update failed: {error}");

    public static string UpdateStarting(string dir) => Loc.Pick(
        $"Запускаю установщик для {dir}. Программа сейчас закроется, чтобы освободить файл.",
        $"Launching the installer for {dir}. This program will now exit so the file can be replaced.");

    public static string UpdateNoPowerShell => Loc.Pick(
        "Не нашёл powershell.exe — обнови вручную командой из README.",
        "Could not find powershell.exe — update manually with the command from the README.");

    public static string ConfigUnreadable(string path, string error) => Loc.Pick(
        $"Конфиг {path} не читается: {error}", $"Config {path} could not be read: {error}");
}
