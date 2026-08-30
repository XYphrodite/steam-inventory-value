using System.Reflection;
using System.Text.Json;

namespace SteamInvValue.Core;

/// <summary>Что нашлось в GitHub-релизах.</summary>
public sealed record UpdateInfo(string Current, string Latest, string Url, bool IsNewer);

/// <summary>
/// Версия сборки и проверка обновлений. Запрос уходит на api.github.com, поэтому делается
/// только с явного согласия пользователя (<see cref="AppConfig.CheckUpdates"/>) и не чаще
/// раза в сутки — ответ лежит в том же файловом кэше.
/// </summary>
public static class Updater
{
    public const string Repo = "XYphrodite/steam-inventory-value";
    public const string InstallScript =
        "https://raw.githubusercontent.com/XYphrodite/steam-inventory-value/main/install.ps1";

    /// <summary>Версия из атрибутов сборки; в сборке из исходников без тега — 0.0.0-dev.</summary>
    public static string CurrentVersion { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var raw = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                  ?? asm.GetName().Version?.ToString()
                  ?? "0.0.0";
        // SourceLink дописывает "+<хеш коммита>" — в глаза пользователю это не нужно.
        var plus = raw.IndexOf('+');
        var version = plus > 0 ? raw[..plus] : raw;
        return version == "1.0.0" ? "0.0.0-dev" : version;
    }

    /// <summary>Каталог, из которого запущена программа — туда же ставит обновление.</summary>
    public static string InstallDir => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    public static async Task<UpdateInfo?> CheckAsync(FileCache cache, CancellationToken ct = default)
    {
        var latest = await cache.GetOrAddAsync("update_latest", TimeSpan.FromHours(24), async () =>
        {
            try
            {
                var json = await Http.Client.GetStringAsync(
                    $"https://api.github.com/repos/{Repo}/releases/latest", ct);
                using var doc = JsonDocument.Parse(json);
                return new LatestRelease(
                    doc.RootElement.GetProperty("tag_name").GetString() ?? "",
                    doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "");
            }
            catch
            {
                return new LatestRelease("", ""); // сеть недоступна — молчим, это не ошибка запуска
            }
        });

        if (string.IsNullOrEmpty(latest.Tag)) return null;
        return new UpdateInfo(CurrentVersion, latest.Tag, latest.Url, IsNewer(latest.Tag, CurrentVersion));
    }

    /// <summary>Сравнивает "v0.2.0" с "0.1.0"; на непонятных строках честно отвечает «не новее».</summary>
    public static bool IsNewer(string latestTag, string current)
    {
        static Version? Parse(string s)
        {
            s = s.TrimStart('v', 'V');
            var cut = s.IndexOfAny(['-', '+']);
            if (cut > 0) s = s[..cut];
            return Version.TryParse(s, out var v) ? v : null;
        }

        var l = Parse(latestTag);
        var c = Parse(current);
        return l is not null && c is not null && l > c;
    }

    private sealed record LatestRelease(string Tag, string Url);

    /// <summary>Файл релиза: имя архива и ссылка на скачивание.</summary>
    public sealed record ReleaseAsset(string Name, string Url, long Size);

    /// <summary>Тег последнего релиза и его файлы — нужно для самообновления.</summary>
    public static async Task<(string Tag, IReadOnlyList<ReleaseAsset> Assets)> GetLatestAsync(
        CancellationToken ct = default)
    {
        using var response = await Http.Client.GetAsync(
            $"https://api.github.com/repos/{Repo}/releases/latest", ct);

        // Аноним получает 60 запросов в час на IP. Голый «403» об этом не говорит ничего.
        if (response.StatusCode is System.Net.HttpStatusCode.Forbidden
                                or System.Net.HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException(S.GitHubRateLimited);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        var assets = new List<ReleaseAsset>();
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
            assets.Add(new ReleaseAsset(
                a.GetProperty("name").GetString() ?? "",
                a.GetProperty("browser_download_url").GetString() ?? "",
                a.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0));

        return (tag, assets);
    }

    /// <summary>Скачивает файл, сообщая о прогрессе (скачано, всего).</summary>
    public static async Task DownloadAsync(string url, string destination,
        Action<long, long>? progress = null, CancellationToken ct = default)
    {
        using var response = await Http.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0;
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(destination);

        var buffer = new byte[131072];
        long done = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            progress?.Invoke(done, total);
        }
    }

    /// <summary>
    /// Кладёт новый файл на место старого. Работающий exe перезаписать нельзя, но переименовать
    /// можно — поэтому старый уезжает в .old и удаляется при следующем запуске.
    /// </summary>
    public static void ReplaceFile(string target, string fresh)
    {
        if (File.Exists(target))
        {
            var old = target + ".old";
            if (File.Exists(old)) TryDelete(old);
            File.Move(target, old);
        }
        File.Move(fresh, target);
    }

    /// <summary>Подчищает файлы, оставшиеся от прошлого обновления.</summary>
    public static void CleanupOld()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(InstallDir, "*.old")) TryDelete(f);
        }
        catch { /* не нашли и ладно */ }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* ещё занят — удалится в следующий раз */ }
    }
}
