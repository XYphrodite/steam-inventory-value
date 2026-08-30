using System.Text.Json;
using System.Text.RegularExpressions;

namespace SteamInvValue.Core;

/// <summary>
/// Читает публичный инвентарь Steam: сначала со страницы профиля берёт список игр
/// (g_rgAppContextData), затем постранично тянет /inventory/{id}/{app}/{ctx}.
/// </summary>
public sealed partial class SteamInventoryClient(Action<string>? log = null)
{
    private readonly Action<string> _log = log ?? (_ => { });

    public async Task<IReadOnlyList<InventoryContext>> GetContextsAsync(string steamId64, CancellationToken ct = default)
    {
        var html = await Http.Client.GetStringAsync(
            $"https://steamcommunity.com/profiles/{steamId64}/inventory/", ct);

        var json = ExtractJson(html, "g_rgAppContextData");
        if (json is null)
        {
            if (html.Contains("This profile is private", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("профиль скрыт", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(S.ProfilePrivate);
            throw new InvalidOperationException(S.CannotReadAppList);
        }

        var result = new List<InventoryContext>();
        using var doc = JsonDocument.Parse(json);
        // пустой инвентарь Steam отдаёт как [] вместо {}
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;

        foreach (var app in doc.RootElement.EnumerateObject())
        {
            if (!int.TryParse(app.Name, out var appId)) continue;
            var appName = app.Value.TryGetProperty("name", out var n) ? n.GetString() ?? app.Name : app.Name;
            if (!app.Value.TryGetProperty("rgContexts", out var ctxs)) continue;

            foreach (var ctx in ctxs.EnumerateObject())
            {
                var count = ctx.Value.TryGetProperty("asset_count", out var c) ? c.GetInt32() : 0;
                if (count <= 0) continue;
                result.Add(new InventoryContext(appId, appName, ctx.Name, count));
            }
        }
        return result;
    }

    public async Task<List<InventoryItem>> GetItemsAsync(
        string steamId64, InventoryContext ctx, string language = "english", CancellationToken ct = default)
    {
        var items = new Dictionary<string, InventoryItem>();
        string? start = null;
        var page = 0;

        while (!ct.IsCancellationRequested)
        {
            var url = $"https://steamcommunity.com/inventory/{steamId64}/{ctx.AppId}/{ctx.ContextId}" +
                      $"?l={language}&count=2000" + (start is null ? "" : $"&start_assetid={start}");

            var body = await GetWithRetryAsync(url, ct);
            if (body is null) break;

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("assets", out var assets) ||
                !root.TryGetProperty("descriptions", out var descs)) break;

            var byKey = new Dictionary<string, JsonElement>();
            foreach (var d in descs.EnumerateArray())
                byKey[$"{d.GetProperty("classid").GetString()}_{Prop(d, "instanceid") ?? "0"}"] = d;

            foreach (var a in assets.EnumerateArray())
            {
                var key = $"{a.GetProperty("classid").GetString()}_{Prop(a, "instanceid") ?? "0"}";
                if (!byKey.TryGetValue(key, out var d)) continue;

                var amount = int.TryParse(Prop(a, "amount"), out var am) ? am : 1;
                if (items.TryGetValue(key, out var existing)) { existing.Count += amount; continue; }

                items[key] = new InventoryItem
                {
                    AppId = ctx.AppId,
                    ContextId = ctx.ContextId,
                    ClassId = a.GetProperty("classid").GetString() ?? "",
                    InstanceId = Prop(a, "instanceid") ?? "0",
                    Name = Prop(d, "name") ?? "(без имени)",
                    MarketHashName = Prop(d, "market_hash_name"),
                    IconUrl = Prop(d, "icon_url"),
                    Type = Prop(d, "type"),
                    Rarity = Tag(d, "Rarity"),
                    Exterior = Tag(d, "Exterior"),
                    Tradable = Num(d, "tradable") == 1,
                    TradableAfter = TradeHoldUntil(d),
                    Marketable = Num(d, "marketable") == 1,
                    Count = amount,
                };
            }

            page++;
            var more = root.TryGetProperty("more_items", out var mi) && mi.GetInt32() == 1;
            start = root.TryGetProperty("last_assetid", out var la) ? la.GetString() : null;
            _log(S.PageRead(ctx.AppName, page, items.Values.Sum(i => i.Count)));
            if (!more || start is null) break;
            await Task.Delay(1200, ct);
        }

        return items.Values.ToList();
    }

    private async Task<string?> GetWithRetryAsync(string url, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var resp = await Http.Client.GetAsync(url, ct);
            if (resp.IsSuccessStatusCode) return await resp.Content.ReadAsStringAsync(ct);

            if ((int)resp.StatusCode == 429 || (int)resp.StatusCode >= 500)
            {
                var wait = TimeSpan.FromSeconds(5 * Math.Pow(2, attempt));
                _log(S.SteamSaid((int)resp.StatusCode, wait.TotalSeconds));
                await Task.Delay(wait, ct);
                continue;
            }
            if ((int)resp.StatusCode == 403)
                throw new InvalidOperationException(S.InventoryForbidden);
            return null;
        }
        return null;
    }

    private static string? Prop(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.ToString(),
                _ => null
            }
            : null;

    private static int Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.Number => v.GetInt32(),
                JsonValueKind.String => int.TryParse(v.GetString(), out var i) ? i : 0,
                JsonValueKind.True => 1,
                _ => 0
            }
            : 0;

    /// <summary>
    /// Срок временной блокировки обмена. Steam кладёт его в owner_descriptions строкой вида
    /// «Tradable After Sep 5, 2026 (07:00:00) GMT», а в отсутствие таковой у заблокированного
    /// предмета годится cache_expiration. Оба поля приходят не всегда: owner_descriptions
    /// Steam отдаёт владельцу, а не стороннему наблюдателю.
    /// </summary>
    private static DateTimeOffset? TradeHoldUntil(JsonElement d)
    {
        if (d.TryGetProperty("owner_descriptions", out var owner) && owner.ValueKind == JsonValueKind.Array)
            foreach (var line in owner.EnumerateArray())
            {
                var value = Prop(line, "value");
                if (value is null || !value.Contains("After", StringComparison.OrdinalIgnoreCase)) continue;

                var match = TradableAfterPattern().Match(value);
                if (!match.Success) continue;

                var text = $"{match.Groups[1].Value} {match.Groups[2].Value}";
                if (DateTimeOffset.TryParseExact(text, "MMM d, yyyy HH:mm:ss",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                    return parsed;
            }

        // Запасной путь: у заблокированного предмета Steam обычно держит здесь конец блокировки.
        if (Num(d, "tradable") == 0 && Prop(d, "cache_expiration") is { } cache &&
            DateTimeOffset.TryParse(cache, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var expiration) &&
            expiration > DateTimeOffset.Now)
            return expiration;

        return null;
    }

    // Steam пишет и «Tradable After», и «Tradable/Marketable After» — префикс должен быть гибким.
    [GeneratedRegex(@"(?:Tradable|Marketable)[^ ]* After ([A-Za-z]{3} \d{1,2}, \d{4}) \((\d{2}:\d{2}:\d{2})\)")]
    private static partial Regex TradableAfterPattern();

    private static string? Tag(JsonElement d, string category)
    {
        if (!d.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array) return null;
        foreach (var t in tags.EnumerateArray())
        {
            var cat = Prop(t, "category");
            if (string.Equals(cat, category, StringComparison.OrdinalIgnoreCase))
                return Prop(t, "localized_tag_name") ?? Prop(t, "name");
        }
        return null;
    }

    /// <summary>
    /// Ищет «{name} = ...» и вырезает следующий за ним JSON. На странице таких вхождений
    /// бывает несколько (в том числе внутри кода), поэтому перебираем, пока не распарсится.
    /// </summary>
    private static string? ExtractJson(string html, string name)
    {
        var from = 0;
        while (true)
        {
            var idx = html.IndexOf(name + " = ", from, StringComparison.Ordinal);
            if (idx < 0) return null;
            from = idx + name.Length;

            var p = idx + name.Length + 3;
            while (p < html.Length && char.IsWhiteSpace(html[p])) p++;
            if (p >= html.Length) return null;
            if (html[p] == '[') return "[]"; // пустой список инвентарей
            if (html[p] != '{') continue;

            var candidate = ExtractObject(html, p);
            if (candidate is null) continue;
            try { using var _ = JsonDocument.Parse(candidate); return candidate; }
            catch { /* это был не тот кусок — ищем дальше */ }
        }
    }

    /// <summary>Вырезает сбалансированный JSON-объект, начиная с позиции открывающей скобки.</summary>
    private static string? ExtractObject(string html, int start)
    {

        var depth = 0; var inStr = false; var esc = false;
        for (var i = start; i < html.Length; i++)
        {
            var ch = html[i];
            if (inStr)
            {
                if (esc) esc = false;
                else if (ch == 0x5C) esc = true;
                else if (ch == '"') inStr = false;
                continue;
            }
            if (ch == '"') inStr = true;
            else if (ch == '{') depth++;
            else if (ch == '}' && --depth == 0) return html[start..(i + 1)];
        }
        return null;
    }

    /// <summary>
    /// Запасной путь: если страница профиля недоступна или пуста, пробуем популярные инвентари
    /// напрямую — по одному лёгкому запросу на игру.
    /// </summary>
    public async Task<IReadOnlyList<InventoryContext>> ProbeContextsAsync(
        string steamId64, int[]? onlyApps = null, CancellationToken ct = default)
    {
        var found = new List<InventoryContext>();
        foreach (var (appId, name, ctxId) in KnownApps.All)
        {
            if (ct.IsCancellationRequested) break;
            if (onlyApps is { Length: > 0 } && !onlyApps.Contains(appId)) continue;
            var url = $"https://steamcommunity.com/inventory/{steamId64}/{appId}/{ctxId}?l=english&count=1";
            var body = await GetWithRetryAsync(url, ct);
            if (body is null) continue;

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("total_inventory_count", out var t)) continue;
                var count = t.GetInt32();
                if (count <= 0) continue;
                _log(S.FoundInventory(name, count));
                found.Add(new InventoryContext(appId, name, ctxId, count));
            }
            catch { /* пустой/чужой ответ — пропускаем */ }

            await Task.Delay(900, ct);
        }
        return found;
    }
}
