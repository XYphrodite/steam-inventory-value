*[Русский](README.md) · English*

# SteamInvValue — Steam inventory watcher

Watches several Steam inventories and works out what they are worth: prices from the Steam
Community Market and from third-party marketplaces, converted to ₽ / $ / USDT / BTC, with a
history of how the value moves. Profile links live in a config file and are read at startup.

```
src/SteamInvValue.Core   core: config, inventory reading, price providers, FX, history
src/SteamInvValue.Cli    console: list, valuation, history, JSON/CSV export
src/SteamInvValue.Web    local site: inventory list, item cards, value chart
```

## Running

Requires the .NET 10 SDK.

```
dotnet run --project src/SteamInvValue.Cli -- add https://steamcommunity.com/id/nickname
dotnet run --project src/SteamInvValue.Cli                    # value everything in the config
dotnet run --project src/SteamInvValue.Cli -- list            # what is being watched
dotnet run --project src/SteamInvValue.Cli -- history         # how the value changed
dotnet run --project src/SteamInvValue.Cli -- --check         # are the price sources alive
```

Web:

```
dotnet run --project src/SteamInvValue.Web
```

Then open http://localhost:5188 (change the port with `STEAMINV_URL`). The page shows the
watched inventories with their current value on the left, and on the right the summary tiles,
the history chart, per-marketplace and per-game tables, and a filterable item grid.
Inventories are added and removed right there; settings are edited in the Settings panel and
written back to the same config file.

## Config

`%LOCALAPPDATA%\SteamInvValue\config.json`, created on first run. Use a different path with
the `--config` flag or the `STEAMINV_CONFIG` environment variable.

```json
{
  "profiles": [
    { "id": "76561198000000000", "name": "Main", "input": "https://steamcommunity.com/id/nickname",
      "apps": [730, 753], "enabled": true }
  ],
  "steam": { "enabled": true, "budget": 400, "delayMs": 3500 },
  "language": "english",
  "autoRefreshMinutes": 0,
  "proxy": null,
  "cookie": null
}
```

* `id` — SteamID64; it is also the key for the report and the history, and is filled in
  automatically by `add`.
* `apps` — restrict to these games; `null` or empty means every inventory on the profile.
* `enabled: false` — skip during a bulk run without losing the history.
* `autoRefreshMinutes` — in web mode, re-value the inventories no more often than this;
  `0` means manual only.

Results are stored next to the config: `reports/<steamid>.json` holds the last full report
(the web UI opens it instantly without touching Steam), and `history/<steamid>.jsonl` holds
one line per run, which is what the chart and the "change" column are built from.

## One-off valuation

A link that is not in the config is valued as given and stored nowhere:

```
dotnet run --project src/SteamInvValue.Cli -- https://steamcommunity.com/id/somebody --no-steam
dotnet run --project src/SteamInvValue.Cli -- nickname --json report.json --csv items.csv
```

## What the numbers mean

| Number | Meaning |
|---|---|
| **Steam, list price** | sum of `lowest_price` — what a buyer pays on the Steam Market |
| **Steam, net** | minus the 15% fee; the money stays in the Steam wallet and cannot be withdrawn |
| **Best marketplace** | every item priced at whichever marketplace pays the most, after its fee — this is real money |
| **Per marketplace** | what you get if you sell everything a given marketplace accepts there |

Seller fees are hard-coded per provider (`PayoutRate`): Steam 15%, Skinport ~12%,
Waxpeer ~6%, Market.CSGO ~5%. Each one is edited in a single place — its provider class.

## Price sources

| Marketplace | Games | How it is fetched |
|---|---|---|
| Steam Market | everything, trading cards (753/6) included | `priceoverview`, one name per request, harshly rate-limited |
| Skinport | 730, 440, 570, 252490 | public price list for the whole catalogue in one request |
| Waxpeer | 730, 570, 440, 252490 | same |
| Market.CSGO | 730 | same |

Marketplace price lists are cached for 30 minutes and individual Steam prices for 12 hours,
under `%LOCALAPPDATA%\SteamInvValue\cache`.

## About Steam rate limits

This is the main constraint on the whole idea:

* Steam has no bulk price endpoint — one name per request, and it starts returning 429 after
  a few dozen requests in a row. Hence the defaults: a 3.5 s pause and at most 400 names per
  run (`--steam-budget`, `--steam-delay`), with exponential backoff on 429.
* Whatever does not fit the budget is reported as "not queried". Run it again — the cache
  accumulates between runs and coverage grows.
* The inventory endpoint is rate-limited per IP too, and on some networks (Russian ones in
  particular) anonymous requests to `/inventory/` hit 429 almost immediately. Two ways
  around it:

  ```
  --cookie "<steamLoginSecure from your browser>"   # an authenticated session, far softer limits
  --proxy  http://user:pass@host:port               # or socks5://host:port
  ```

  The same values are read from `STEAMINV_COOKIE` and `STEAMINV_PROXY` — the web UI only
  takes them from there. Grab the `steamLoginSecure` cookie in DevTools → Application →
  Cookies → steamcommunity.com; it is full access to the account, so never show it to anyone.
* For CS2 skins the third-party marketplaces are faster and more complete: `--no-steam`
  returns in seconds. Steam is mostly needed for trading cards and anything that is not CS.

## What is missing

* Buff163 — the most representative marketplace for CS2, but it has no public API and needs
  cookies. Not wired up.
* Steam buy orders (the instant-sell price) — that requires scraping the listing page for
  `item_nameid`, one more request per item on top of the rate limit.
* Instant sell to bots (usually 40–60% of the price) — the marketplaces publish listings,
  not their buyout prices.
* CS2 floats and patterns — prices are looked up by `market_hash_name`, i.e. by name and
  wear. A rare float or pattern is worth more than this report shows.

## Privacy

Everything is computed from public data. If an inventory is set to private, neither this tool
nor anyone else can see it; no Steam API key is needed and none is sent anywhere.
