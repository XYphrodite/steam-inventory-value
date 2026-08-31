using System.Net;

namespace SteamInvValue.Core;

/// <summary>
/// Единый HTTP-клиент. Steam жёстко лимитирует анонимные запросы к /inventory/ по IP —
/// поэтому поддерживаются прокси и cookie авторизованной сессии: с ними лимит заметно мягче.
/// </summary>
public static class Http
{
    private static HttpClient? _client;
    private static HttpClientHandler? _handler;
    private static string? _proxy = Environment.GetEnvironmentVariable("STEAMINV_PROXY");
    private static string? _cookie = Environment.GetEnvironmentVariable("STEAMINV_COOKIE");

    /// <summary>Кому вообще позволено видеть cookie сессии.</summary>
    private static readonly string[] CookieHosts =
        ["https://steamcommunity.com", "https://store.steampowered.com", "https://api.steampowered.com"];

    public static HttpClient Client => _client ??= Create();

    /// <param name="proxy">http://user:pass@host:port или socks5://host:port</param>
    /// <param name="cookie">значение steamLoginSecure или готовая строка Cookie целиком</param>
    public static void Configure(string? proxy = null, string? cookie = null)
    {
        if (proxy is not null) _proxy = proxy;
        if (cookie is not null) _cookie = cookie;
        _client?.Dispose();
        _client = null;
        _handler = null;
    }

    public static bool HasCookie => !string.IsNullOrWhiteSpace(_cookie);
    public static bool HasProxy => !string.IsNullOrWhiteSpace(_proxy);

    /// <summary>
    /// Уйдёт ли cookie сессии на этот адрес. Существует ради проверки: steamLoginSecure — полный
    /// пропуск в аккаунт, и попасть он должен только в Steam, но не в площадки, курсы и GitHub.
    /// </summary>
    public static bool SendsCookieTo(string url)
    {
        _ = Client; // клиент собирается лениво — без этого контейнера ещё нет
        return _handler is { } h && h.CookieContainer.GetCookies(new Uri(url)).Count > 0;
    }

    private static HttpClient Create()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            CookieContainer = new CookieContainer(),
        };

        if (!string.IsNullOrWhiteSpace(_proxy))
        {
            handler.Proxy = new WebProxy(_proxy);
            handler.UseProxy = true;
        }

        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126 Safari/537.36");
        c.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "br, gzip, deflate");
        c.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "ru,en;q=0.9");
        c.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://steamcommunity.com/");

        // Cookie кладётся в контейнер, а не в общий заголовок. Заголовок по умолчанию уходит на
        // КАЖДЫЙ хост, а через этот клиент ходят и площадки, и курсы валют, и GitHub — то есть
        // пропуск в аккаунт уезжал бы посторонним. Контейнер отдаёт его только своему домену.
        if (!string.IsNullOrWhiteSpace(_cookie))
            foreach (var host in CookieHosts)
                foreach (var (name, value) in ParseCookies(_cookie))
                    try { handler.CookieContainer.Add(new Uri(host), new Cookie(name, value)); }
                    catch (CookieException) { /* значение, которое Steam всё равно не примет */ }

        _handler = handler;
        return c;
    }

    /// <summary>Разбирает и «steamLoginSecure=...; sessionid=...», и голое значение пропуска.</summary>
    private static IEnumerable<(string Name, string Value)> ParseCookies(string raw)
    {
        if (!raw.Contains('='))
        {
            yield return ("steamLoginSecure", raw.Trim());
            yield break;
        }

        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0) yield return (part[..eq].Trim(), part[(eq + 1)..].Trim());
        }
    }
}
