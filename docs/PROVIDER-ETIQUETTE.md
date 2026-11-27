# Provider etiquette

This page explains how SSE's market client treats poe.ninja, GGG and any other price provider. The code is in `StillSaneExile.Domain.Market`. Live prices in the app (#13) send every poe.ninja request through it; [poe.ninja](#poeninja) below says how. No GGG endpoint is called yet. What SSE requests, and how to turn it off, is in [Capture and privacy](CAPTURE-AND-PRIVACY.md#network).

## The rule

- **One client, one address, one account (or none).** SSE never rotates proxies or IP addresses and never spreads requests over extra accounts to get around a limit.
- **Nothing retries automatically.** A 4xx or 5xx goes back to the caller unchanged. The governor then decides when the next attempt may go, and that is always later, never sooner.
- **When a provider says slow down or stop, we do.** That includes routes it did not name, if they might share the limit.
- **The defaults are deliberately slow.** Prices refresh hourly, so SSE never needs to get close to a provider's limits.

## poe.ninja

**Each SSE install fetches poe.ninja itself, hourly.** That is the owner's choice: the shared economy cache (#11), which would fetch once for many players, is not being built now. So each install has to be light on its own:

- **Only when asked for.** Prices are fetched only for a live profile with a confirmed league and "Live prices from poe.ninja" on. The demo never fetches. A key press never fetches: the HUD reads the local price book and has no network client.
- **Hourly.** The first refresh comes 5–15 s after start (random, so installs don't line up), or an hour after the last refresh if that is sooner than an hour ago; then every hour, plus up to a minute of jitter. A refresh that fails waits at least 10 minutes, longer if the governor says so. A league switch starts the new league's schedule and cancels the refresh in flight.
- **Light.** One request per category in `data/poe-ninja.json`: 12 exchange types and 8 unique types, about 20 requests per refresh. poe.ninja sends no rate-limit headers, so its routes stay in conservative mode: one request at a time, at least 5 s apart, about two minutes for a whole refresh. A type that comes back empty or 404 is skipped (poe.ninja renames types between leagues); a 429, a 5xx or a hold ends the refresh at once, and the last good snapshot stays.
- **Cache headers honoured.** The exchange overview says `max-age=1800`, so a second refresh within 30 min reuses it without a request. The item overview says `no-cache`, so it is fetched at most once per refresh, conditionally (`If-None-Match`) when poe.ninja sent an `ETag`. The league list says `max-age=300`.
- **Identified, and nothing more.** Requests carry `league=` and `type=` only, no cookies, and the User-Agent `StillSaneExile/<version> (+https://github.com/AutumnTrace/still-sane-exile)`, so poe.ninja can see who is asking and where to complain.
- **Routes.** `poe.ninja` is the integration; the routes are `poe2/api/data/index-state`, `poe2/api/economy/exchange/current/overview` and `poe2/api/economy/stash/current/item/overview`, one each whatever the league or type.

**What the numbers are.** poe.ninja's values are its own aggregates for the whole league: exchange lines become exchange estimates and unique lines category baselines, in Divine Orbs, with `core.rates` for Exalted and Chaos Orbs. SSE does not look at individual listings here. The owner's rule is that prices should mainly come from instant buyout listings, because players who list at 1 ex without instant buyout are often unsure of the price; that rule belongs to listing-level searches, not to poe.ninja's estimates, which SSE shows as they are and labels as poe.ninja's.

## How a request goes

`MarketProviderHandler` is a `DelegatingHandler`, so any `HttpClient` can use it:

1. A GET with a fresh cached copy is answered from the cache. No request is made.
2. A stale copy that has a validator turns the request into a conditional one (`If-None-Match`, `If-Modified-Since`).
3. `ProviderGovernor` is asked whether the request may go. It answers in one of three ways:
   - **Send now.** The request goes.
   - **Short pacing hold** (keeping headroom, or conservative spacing). The handler waits it out, for up to 30 s.
   - **Any other hold.** The handler throws `ProviderHoldException` at once instead of sleeping. It carries the reason, the time to try again (`RetryAt`), a plain-English message for the UI, and the stale cached copy when its headers allow showing one.
4. The answer is recorded: limits, restrictions, errors, cache.

Tag each request with its provider and endpoint, for example `request.ForRoute("ggg", "currency-exchange")`. Without a tag, the host is the provider and the URL path is the route. That is wrong for templated URLs: each hour of the exchange digest would look like a new route with unknown limits.

## Rate limits

GGG documents these headers. All values are in seconds.

| Header | Example | Meaning |
|---|---|---|
| `X-Rate-Limit-Policy` | `trade-fetch-request-limit` | Routes that return the same policy share its buckets |
| `X-Rate-Limit-Rules` | `Ip,Account` | Rules in force |
| `X-Rate-Limit-<Rule>` | `5:10:60,30:300:1800` | Per window: hits allowed, window length, penalty if exceeded |
| `X-Rate-Limit-<Rule>-State` | `3:10:0,12:300:0` | Per window: hits counted so far, window length, restriction time left |

- Every window of every rule is tracked, and the one closest to full decides when the next request may go.
- **Headroom:** each window keeps 10% of its hits unused, and always at least one. A 5-per-10 s window therefore allows 4, and a 45-per-60 s window allows 40. A hit is treated as still in its window for 1 s longer than the window, to cover clock skew and latency.
- The server also counts other clients on the same IP or account, such as the trade site or other tools. When its count is higher than ours, we use its count.
- A slot is taken when a request is sent, so requests running at the same time count against each other.

## 429 Too Many Requests

- The pause lasts until the longest of these ends: `Retry-After` (in seconds or as an HTTP date) and every window's restriction time left. A small random jitter of 1–5 s is then added, so routes, and other copies of the app, do not all come back at the same instant.
- If the 429 names a policy, every route sharing that policy waits. Routes of the same provider whose limits are unknown wait too, because they might share it. Other providers are unaffected.
- If the 429 has no readable policy, the whole provider waits.
- If it says nothing about how long, the pause is the policy's harshest penalty, or 60 s.
- A 429 also counts towards the circuit (below).

## Missing or malformed headers: conservative mode

A route with no rate-limit headers, or with any value that cannot be read in full, goes into conservative mode. Examples include a missing state header, a window in only one of the two headers, a non-number or an absurd value. In conservative mode, requests to that provider go **one at a time, at least 5 s apart**. Parsing never throws. The route leaves conservative mode as soon as it returns a complete, readable set of headers. poe.ninja does not send GGG's headers, so it always runs in this mode.

## Errors

| Answer | What happens |
|---|---|
| 401, 403 | The integration is **disabled** until someone fixes the cause and calls `Enable`. No amount of waiting turns it back on. |
| 3 other 4xx with no success in between (429 included) | **Circuit open** for 5 min. After that, one trial request is allowed and nothing else goes while it runs. A failed trial reopens the circuit for twice as long (up to 1 h). A success closes it. |
| 5xx, or no answer at all | **Backoff** of 10 s, doubling with each failure in a row, capped at 15 min, plus jitter. A longer `Retry-After` is honoured. Any non-5xx answer resets the count. |
| Cancelled by the caller | The slot is freed. The hit stays counted, because it may have reached the server. |

## Cache

`HttpCache` is a private, in-memory cache for GET responses, keyed by URL (256 entries).

- `Cache-Control: max-age` sets how long a response stays fresh, less its `Age`. Without `max-age`, `Expires` decides. With neither, the response is revalidated every time; freshness is never guessed, because prices change.
- `no-store` is never kept. `no-cache` is kept but revalidated before every use. `must-revalidate` (and `no-cache`) mean a stale copy is never offered while the provider cannot be asked.
- A 304 reuses the stored body, takes any new validators and directives from the 304, and restarts the copy's freshness from now. The 304 still counts against rate limits, because it is a request.
- Responses that vary by request headers are not supported. `Vary: *` is never stored.

## Persistence

`ICooldownStore` keeps the governor's state between runs. `JsonFileCooldownStore` writes it to one JSON file, replaced whole on each save; the app keeps it as `provider-cooldowns.json` in its data folder. The state is saved after every answer and holds:

- each policy's windows, with the send times of hits still inside them, and when its restriction ends;
- which policy each route reported;
- per provider: disabled after 401/403, the circuit, the backoff, a provider-wide pause, and the last conservative send.

Every time is an absolute UTC instant, never "seconds left", so a restart during a cooldown keeps requests blocked until it really ends. If the file cannot be read, every route starts in conservative mode, and the next 429 teaches the cooldown again. If a save fails, the in-memory state still governs. No single header can make SSE wait more than 24 h; a longer ban is simply learned again from the next 429.

## Defaults

All defaults are in `GovernorOptions`.

| Setting | Default |
|---|---|
| Headroom per window | 10%, at least 1 hit |
| Window margin | 1 s |
| Conservative spacing | 1 request at a time, 5 s apart |
| 429 with no length | policy's harshest penalty, else 60 s |
| Longest wait taken from a header | 24 h |
| Circuit | opens after 3 × 4xx with no success in between; 5 min, doubling to 1 h |
| 5xx backoff | 10 s, doubling to 15 min |
| Jitter | 1–5 s, added once to each cooldown and backoff |
| Pacing wait inside the handler | up to 30 s |
