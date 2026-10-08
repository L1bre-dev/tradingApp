# Trade Analysis App — Design v3, agreed (2026-10-08)

Goal (PE): user accounts; each user links one or more cTrader accounts; the app extracts their trades, stores the 54 candles up to each trade's fill to redraw the chart, lets the trader (self) and a coach score each trade, and shows dashboards for improvement. Runs on PE's OVH VPS. Keep it simple.

Legend: **[verified]** = checked in cTrader documentation (sources at the end). **[assumed]** = to confirm while building. **[decision]** = choice for PE (default proposed).

**Changes from v2 (PE's answers, 2026-10-08):** the fill candle is **cut at the fill moment** (section 4); **blind scoring**: neither analysis is visible to the other side until both are submitted (section 5); timeframes M1-H4, permanent candle storage and "win = net P&L > 0" confirmed.

**Changes from v1 (PE's answers, 2026-10-06):** anchor at **fill time**; chart = **54 candles, the last one being the fill candle**, nothing after; only win/lose (and P&L) of the outcome is used; analysis = **3 scores (trend, technique, risk management) multiplied + a comment**; tags and 1-5 rating dropped; manual trades are **qualified in the app** (timeframe + technique); Blazor Server and invitation-only sign-up confirmed; domain `fta.trading`; VPS = OVH VPS-2-2026 (6 vCores, 12 GB, 100 GB).

---

## 1. The big picture

```
 Browser (trader / coach)
        │ HTTPS  (fta.trading)
 ┌──────▼─────────────── OVH VPS-2-2026 (Docker Compose) ───────────────┐
 │  Caddy (HTTPS, auto certificate)                                     │
 │     │                                                                │
 │  L1bre.Journal  (one ASP.NET Core .NET 10 app)                         │
 │     ├─ Web UI (Blazor Server)                                        │
 │     ├─ Sync worker (background service, every 5 min)  ───────────────┼──► cTrader Open API
 │     └─ uses L1bre.Core (shared with the cBots)                       │     (OAuth, deals, candles)
 │     │                                                                │
 │  PostgreSQL (one database)  ── nightly backup                        │
 └──────────────────────────────────────────────────────────────────────┘
```

Three containers, one app, one database. The VPS is far bigger than needed (this app would run on 2 vCores / 4 GB), so there is room for other things later.

Why: one C# codebase shared with the cBots (L1bre.Core); Blazor Server = pages in C# + HTML, no separate JavaScript project; PostgreSQL easy to back up; Caddy renews HTTPS certificates by itself; charts drawn with TradingView *Lightweight Charts* (free, Apache 2.0).

---

## 2. Where the data comes from: cTrader Open API

Verified:
- Any cTrader ID can register an Open API application; works with demo and live accounts of any cTrader broker (Fusion Markets included).
- Linking = OAuth on `id.ctrader.com`. We ask only for scope `accounts` = **view only** (the app can never trade).
- One token covers **all trading accounts of a cTrader ID**; the user ticks which ones to import.
- Access token ~30 days, refresh token never expires; the app refreshes automatically.
- `ProtoOADealListReq` returns fills between two dates (paged). Each fill carries the order's **label** and **comment** → Frame trades arrive already qualified (label `Frame`, comment `M15`).
- `ProtoOAGetTrendbarsReq` returns candles for a symbol, timeframe and date range, with a `count` (max 14,000 bars per request; we need 54).
- Rate limit: **5 requests/s for history** (deals, candles). Only matters for the first import.

Not verified yet (checked when PE registers the app): whether Spotware must activate a new application; how far back deal history goes; whether several redirect URLs can be set on one application (useful for the domain move, section 7).

Once, PE registers one Open API application (client id + secret, redirect URL `https://fta.trading/ctrader/callback`). All users link through it.

---

## 3. Users and roles

- **Trader**: links cTrader IDs, sees their own accounts, trades, analyses, dashboards; qualifies their manual trades.
- **Coach**: sees the trades of traders who invited them; writes coach analyses; cannot change trader data. A trader can revoke a coach at any time.
- **Admin** (PE): invites users.
- Login: email + password, sign-up **by invitation only** (confirmed).

---

## 4. A trade and its candles

**Anchor = fill time.** A trade starts when its position is opened (the fill), whatever the order type.

**Chart window = 54 candles, the last one is the candle in which the order was filled.** Nothing after the fill is drawn. The fill price is marked on that last candle, with the SL and TP levels as lines.
**The fill candle is cut at the fill moment**, so the chart shows exactly what the market looked like when the trade was taken:
- open = the candle's real open; high / low = extremes from the candle open **up to the fill time**; close = the price at the fill time.
- How it is built: the complete M1 candles between the candle open and the fill minute, plus the ticks of the fill minute up to the fill millisecond (`ProtoOAGetTickDataReq` **[verified it exists; returns ticks between two timestamps, paged]**). One M1 request + one tick request build the cut candle for every timeframe at once (M1 to H4).
- Price series: cTrader candles are built from **Bid** prices **[assumed, to confirm at build]**, so the cut candle uses Bid ticks too, to stay consistent with the 53 candles before it. The fill price is drawn as its own marker: for a Buy it sits about one spread above the candle's close (a Buy fills at Ask). That gap is real, not a bug.

**Outcome** used for statistics: win / loss (win = net P&L > 0 **[decision]**), plus net P&L and pips for the money dashboards. Exit time and price are kept in the database but not drawn.

**Timeframe and technique of a trade:**
- **Bot trade** (label = a technique name): technique = label, timeframe = comment. Qualified automatically; the trader can correct it.
- **Manual trade** (no label): status **"To qualify"**. At detection the app stores the 54 candles ending at the fill candle for **every timeframe M1, M5, M15, M30, H1, H4** **[decision: confirm this list]**. The trader picks timeframe + technique in the app and the chart switches instantly, without a new request to cTrader.
- Keeping all 6 timeframes **permanently** is simpler than deleting them after qualification (no cleanup job) and costs nothing: 6 × 54 candles ≈ 15 KB per trade, so 10,000 trades ≈ 150 MB. It also lets a coach look at another timeframe later. **[decision, default: keep]**
- Techniques list: Frame, FTM, Family, Jump (editable by admin).

**Replacing the Discord bot:** the "To qualify" list is shown as a counter on every page and as the first screen after login. A notification (Discord or email) pointing to that list can be added later; v1 is in-app only **[decision]**.

---

## 5. Analysis

Each trade can have one **self** analysis and one **coach** analysis, same form:

| Field | Value |
|---|---|
| Trend | 0-100 % — was the trend valid at the fill candle? |
| Technique | 0-100 % — was the technique correctly applied? |
| Risk management | 0-100 % — were SL/TP and size correct? |
| **Total** | Trend × Technique × Risk (e.g. 80 % × 90 % × 100 % = 72 %) — computed, not typed |
| Comment | free text |

The multiplication means one zero (e.g. trend invalid) makes the whole trade 0 %, which matches "a trade against the rules is a bad trade even if it won".

Input: three sliders or number fields in steps of 5 % **[decision]**.

**Blind scoring** (PE: the coach must not be influenced in any way by the self analysis):
- The coach never sees the self analysis of a trade before submitting their own, and the trader doesn't see the coach analysis before submitting theirs.
- Once **both** are submitted, they are shown side by side and both are **locked** (no editing after seeing the other one, otherwise the comparison loses its meaning) **[decision]**.
- Lists show only "self ✓ / coach ✓" until both are in, never the scores.
- Dashboards that compare self vs coach use only trades where both are submitted.

---

## 6. Data model

```
User ─┬─< CTraderLink (one per cTrader ID: encrypted refresh token, expiry)
      │        └─< TradingAccount (ctidTraderAccountId, broker, live/demo, currency, selected, last sync)
      │                 └─< Trade (one per positionId)
      │                        ├─< Deal (open/close fills, raw from cTrader)
      │                        ├─< Candle (timeframe, time, open, high, low, close, volume) — 54 per stored timeframe
      │                        └─< Analysis (author, kind self|coach, trend %, technique %, risk %, comment, date)
      └─< CoachAccess (trader → coach, granted date, revoked date)
Technique (name)
```

**Trade** fields: account, symbol, direction, volume, fill time, fill price, SL, TP, close time, close price, net P&L, commission, swap, pips, result (win/loss), technique, timeframe, origin (bot/manual), status (to qualify / qualified).

---

## 7. Sync

1. **Link**: *Link cTrader* → OAuth → tokens stored encrypted → accounts listed → trader ticks the ones to import.
2. **Every 5 minutes**, per selected account: fetch new fills → group by `positionId` → on a new position, create the Trade and store its candles (1 timeframe for bot trades, 6 for manual trades) → when the position closes, add the outcome.
3. **First import**: same loop walking back through history, throttled under 5 requests/s.
4. **Sync now** button; a failed token refresh flags the account and asks the trader to re-link.

Trades still open are listed (to qualify / analyse early) but excluded from win/loss statistics until closed.

---

## 8. Screens

1. **To qualify**: manual trades waiting for timeframe + technique (chart switches as you choose).
2. **Trades list**: filters (account, period, technique, timeframe, win/loss, analysed or not, score range); columns: date, technique, TF, direction, result, P&L, self total, coach total.
3. **Trade page**: 54-candle chart (last candle cut at the fill) with fill, SL and TP; trade facts; your own analysis form; the other analysis revealed once both are submitted; previous / next buttons.
4. **Dashboard** (per account or all, by period):
   - results: trades, win rate, net P&L, average win / loss, profit factor, equity curve;
   - by technique and by timeframe: win rate and P&L;
   - **scores**: average trend / technique / risk / total (self and coach), and their evolution over time — the improvement curve;
   - **score vs result**: win rate of trades with total ≥ 80 % vs < 80 % — shows whether following the rules pays;
   - **self vs coach gap**: average difference per score — shows where the trader judges themselves wrongly;
   - review progress: % of trades qualified, self-analysed, coach-analysed.
5. **Coach view**: their traders, trades waiting for a coach analysis.
6. **Admin**: invitations, techniques.

---

## 9. Deployment on OVH

- VPS-2-2026 (6 vCores, 12 GB, 100 GB), Ubuntu, Docker Compose: `caddy`, `app`, `postgres`. Secrets in a `.env` on the VPS only.
- Domain **`fta.trading`** (owned until Feb 2027). Moving to `trading.L1bre.org` later = point the new DNS, change one line in the Caddy config, add the new redirect URL to the cTrader app. Users' links keep working because tokens don't depend on the domain **[assumed]**.
- Nightly `pg_dump`, kept 14 days on the VPS + a copy off the VPS (e.g. OVH Object Storage) **[decision]**.
- GitHub Actions builds the image on each merge; update on the VPS with `docker compose pull && docker compose up -d`.
- Code in repo `L1bre-dev/trading` as a new project `L1bre.Journal`, referencing L1bre.Core.

---

## 10. Security

Read-only cTrader access; tokens encrypted at rest; HTTPS only; hashed passwords; coaches only see traders who invited them; firewall open on 80/443 and SSH by key only; PostgreSQL not exposed.

---

## 11. Build phases

- **Phase 1 (MVP)**: users + invitations, link cTrader, sync trades + candles, qualification of manual trades, trade list and chart, self + coach analysis, dashboard. Deploy on fta.trading.
- **Phase 2 (ideas, not committed)**: replay the trend engine (L1bre.Core) on the stored candles to show the trend score at the fill candle and pre-fill the trend score; Discord/email notification for trades to qualify; CSV export; two-factor login.

---

## 12. Decisions taken by default in v3 (say if you want them changed)

1. An analysis is locked once both self and coach are submitted.
2. The trader also can't see the coach analysis before submitting their own (blind in both directions).
3. Score inputs move in steps of 5 %.

---

## Sources

- OAuth / tokens / multi-account: https://help.ctrader.com/open-api/account-authentication/
- Messages (deal list, trendbars, hasMore): https://help.ctrader.com/open-api/messages/
- Model messages (deal label/comment): https://help.ctrader.com/open-api/model-messages/
- Rate limits, demo/live, any broker: https://ctrader.com/faq/open-api
- 14,000 bar limit: https://community.ctrader.com/forum/connect-api-support/24731/
- .NET library: https://github.com/spotware/OpenAPI.Net
