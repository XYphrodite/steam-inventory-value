namespace SteamInvValue.Core;

/// <summary>Инвентари, которые встречаются чаще всего — используются как запасной путь,
/// если страница профиля не отдала g_rgAppContextData (например, Steam режет запросы).</summary>
public static class KnownApps
{
    public static readonly (int AppId, string Name, string ContextId)[] All =
    [
        (753, "Steam (карточки, фоны, эмодзи)", "6"),
        (730, "Counter-Strike 2", "2"),
        (570, "Dota 2", "2"),
        (440, "Team Fortress 2", "2"),
        (252490, "Rust", "2"),
        (578080, "PUBG", "2"),
        (232090, "Killing Floor 2", "2"),
        (304930, "Unturned", "2"),
        (218620, "PAYDAY 2", "2"),
        (322330, "Don't Starve Together", "2"),
        (291550, "Brawlhalla", "2"),
        (238460, "BattleBlock Theater", "2"),
        (620, "Portal 2", "2"),
        (250820, "SteamVR", "2"),
        (2923300, "Banana", "2"),
        (221100, "DayZ", "2"),
        (346110, "ARK: Survival Evolved", "2"),
    ];

    /// <summary>Сколько игр знает перебор — нужно, чтобы честно написать в отчёте, что список неполный.</summary>
    public static int Count => All.Length;
}
