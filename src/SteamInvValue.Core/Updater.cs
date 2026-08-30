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

    /// <summary>Тег последнего релиза, его файлы и описание — нужно для самообновления.</summary>
    public static async Task<(string Tag, IReadOnlyList<ReleaseAsset> Assets, string Notes)> GetLatestAsync(
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

        var notes = doc.RootElement.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        return (tag, assets, notes);
    }

    /// <summary>
    /// Достаёт SHA-256 файла из описания релиза. Хеш публикуется строкой вида
    /// «- `steaminv-cli-win-x64.zip` - SHA-256 `&lt;64 знака&gt;`»; ленивый поиск важен —
    /// в самом имени файла тоже встречаются шестнадцатеричные буквы.
    /// </summary>
    public static string? ExpectedHash(string notes, string assetName)
    {
        if (string.IsNullOrEmpty(notes)) return null;

        // Пропуск любых символов, а не «не-hex»: между именем и хешем стоит слово SHA-256,
        // где A — тоже шестнадцатеричная буква.
        var pattern = System.Text.RegularExpressions.Regex.Escape(assetName) + @"[\s\S]{0,60}?([0-9a-fA-F]{64})";
        var match = System.Text.RegularExpressions.Regex.Match(notes, pattern);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    /// <summary>Считает SHA-256 файла в том же виде, в каком он публикуется.</summary>
    public static async Task<string> FileHashAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Скачивает файл, сообщая о прогрессе (скачано, всего).
    ///
    /// Если сервер умеет отдавать куски (<c>206 Partial Content</c>), файл берётся несколькими
    /// соединениями сразу. Скорость обычно режут на одно соединение, поэтому на медленном
    /// маршруте это помогает в разы; на быстром — не мешает. Любая осечка роняет нас обратно
    /// на обычную загрузку в один поток.
    /// </summary>
    public static async Task DownloadAsync(string url, string destination,
        Action<long, long>? progress = null, CancellationToken ct = default, int segments = 4)
    {
        var total = await GetLengthIfRangesSupportedAsync(url, ct);

        // Мелочь дробить незачем: накладные расходы съедят выигрыш.
        if (total is > 4 * 1024 * 1024 && segments > 1)
        {
            try
            {
                await DownloadInSegmentsAsync(url, destination, total.Value, segments, progress, ct);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch { /* не вышло — качаем как обычно */ }
        }

        await DownloadSingleAsync(url, destination, progress, ct);
    }

    private static async Task DownloadSingleAsync(string url, string destination,
        Action<long, long>? progress, CancellationToken ct)
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

    /// <summary>Размер файла, если сервер согласен отдавать его кусками; иначе null.</summary>
    private static async Task<long?> GetLengthIfRangesSupportedAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);

            using var response = await Http.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode != System.Net.HttpStatusCode.PartialContent) return null;

            return response.Content.Headers.ContentRange?.Length;
        }
        catch { return null; }
    }

    private static async Task DownloadInSegmentsAsync(string url, string destination, long total,
        int segments, Action<long, long>? progress, CancellationToken ct)
    {
        var temp = destination + ".part";
        long done = 0;

        try
        {
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.Write))
                file.SetLength(total);

            var size = total / segments;
            var tasks = new List<Task>();

            for (var i = 0; i < segments; i++)
            {
                var from = i * size;
                var to = i == segments - 1 ? total - 1 : from + size - 1;

                tasks.Add(Task.Run(async () =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(from, to);

                    using var response = await Http.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();

                    await using var input = await response.Content.ReadAsStreamAsync(ct);
                    using var handle = File.OpenHandle(temp, FileMode.Open, FileAccess.Write);

                    var buffer = new byte[131072];
                    var position = from;
                    int read;
                    while ((read = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, read), position, ct);
                        position += read;
                        progress?.Invoke(Interlocked.Add(ref done, read), total);
                    }
                }, ct));
            }

            await Task.WhenAll(tasks);

            if (new FileInfo(temp).Length != total)
                throw new InvalidOperationException("размер скачанного файла не совпал");

            File.Move(temp, destination, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch { /* уберётся при следующей попытке */ }
            throw;
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
