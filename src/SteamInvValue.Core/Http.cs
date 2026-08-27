using System.Net;

namespace SteamInvValue.Core;

/// <summary>
/// Единый HTTP-клиент. Steam жёстко лимитирует анонимные запросы к /inventory/ по IP —
/// поэтому поддерживаются прокси и cookie авторизованной сессии: с ними лимит заметно мягче.
/// </summary>
public static class Http
{
    private static HttpClient? _client;
    private static string? _proxy = Environment.GetEnvironmentVariable("STEAMINV_PROXY");
    private static string? _cookie = Environment.GetEnvironmentVariable("STEAMINV_COOKIE");

    public static HttpClient Client => _client ??= Create();

    /// <param name="proxy">http://user:pass@host:port или socks5://host:port</param>
    /// <param name="cookie">значение steamLoginSecure или готовая строка Cookie целиком</param>
    public static void Configure(string? proxy = null, string? cookie = null)
    {
        if (proxy is not null) _proxy = proxy;
        if (cookie is not null) _cookie = cookie;
        _client?.Dispose();
        _client = null;
    }

    public static bool HasCookie => !string.IsNullOrWhiteSpace(_cookie);
    public static bool HasProxy => !string.IsNullOrWhiteSpace(_proxy);

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

        if (!string.IsNullOrWhiteSpace(_cookie))
        {
            var value = _cookie.Contains('=') ? _cookie : $"steamLoginSecure={_cookie}";
            c.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", value);
        }

        return c;
    }
}
