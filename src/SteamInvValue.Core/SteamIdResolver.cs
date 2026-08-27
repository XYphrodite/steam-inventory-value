using System.Text.RegularExpressions;

namespace SteamInvValue.Core;

public sealed record SteamProfile(string SteamId64, string? PersonaName, string ProfileUrl, bool ProfilePublic);

/// <summary>Превращает ссылку/ник/SteamID в SteamID64 через публичный XML профиля (ключ API не нужен).</summary>
public static partial class SteamIdResolver
{
    [GeneratedRegex(@"steamcommunity\.com/(profiles|id)/([^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileLink();

    [GeneratedRegex(@"<steamID64>(\d+)</steamID64>")]
    private static partial Regex Id64Tag();

    [GeneratedRegex(@"<steamID><!\[CDATA\[(.*?)\]\]></steamID>", RegexOptions.Singleline)]
    private static partial Regex PersonaTag();

    public static async Task<SteamProfile> ResolveAsync(string input, CancellationToken ct = default)
    {
        input = input.Trim();
        string? vanity = null, id64 = null;

        var m = ProfileLink().Match(input);
        if (m.Success)
        {
            if (m.Groups[1].Value.Equals("profiles", StringComparison.OrdinalIgnoreCase)) id64 = m.Groups[2].Value;
            else vanity = m.Groups[2].Value;
        }
        else if (Regex.IsMatch(input, @"^7656\d{13}$")) id64 = input;
        else vanity = input;

        var xmlUrl = id64 is not null
            ? $"https://steamcommunity.com/profiles/{id64}?xml=1"
            : $"https://steamcommunity.com/id/{Uri.EscapeDataString(vanity!)}?xml=1";

        var xml = await Http.Client.GetStringAsync(xmlUrl, ct);
        var idMatch = Id64Tag().Match(xml);
        if (!idMatch.Success)
            throw new InvalidOperationException($"Не удалось определить SteamID по '{input}'. Профиль не найден или скрыт.");

        var resolved = idMatch.Groups[1].Value;
        var persona = PersonaTag().Match(xml) is { Success: true } p ? p.Groups[1].Value : null;
        var isPublic = !xml.Contains("<privacyState>private", StringComparison.OrdinalIgnoreCase);

        return new SteamProfile(resolved, persona, $"https://steamcommunity.com/profiles/{resolved}", isPublic);
    }
}
