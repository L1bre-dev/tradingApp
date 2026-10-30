# Trade Analysis App: build plan (v1, for review — 2026-10-08)

Built from the agreed design: `trade-analysis-app-design-v3.md`. Working name of the app: **L1bre.Journal**, served on **fta.trading**.

---

## 1. Repository layout (new repo `L1bre-dev/tradingApp`, PE 2026-10-08)

The app lives in its own repo, separate from the cBots in `L1bre-dev/trading`. Phase 1 doesn't need L1bre.Core; when the trend replay comes (phase 2), L1bre.Core will be shared as a private NuGet package (GitHub Packages) rather than copied.

```
src/
  L1bre.Journal.Domain/    plain C#, no web, no cTrader: all the rules, fully unit-tested
    DealsToTrades.cs        group cTrader fills by position → Trade (direction, fill, SL/TP, P&L, win/loss)
    Qualification.cs        bot trade (label + comment) vs manual trade "to qualify"
    CutCandle.cs            build the fill candle cut at the fill moment (M1 bars + ticks)
    Scoring.cs              total = trend × technique × risk
    BlindRule.cs            who can see which analysis, lock once both are submitted
    Stats.cs                win rate, profit factor, expectancy, drawdown, score bands, self-vs-coach gap
  L1bre.Journal.CTrader/   the only code that talks to cTrader Open API (OAuth, deals, candles, ticks)
  L1bre.Journal/           the Blazor Server web app + EF Core (PostgreSQL) + sync background worker
tests/
  L1bre.Journal.Domain.Tests/
deploy/
  docker-compose.yml        caddy + app + postgres
  Caddyfile                 fta.trading → app (HTTPS automatic)
  backup.sh                 nightly pg_dump, 14 days kept
  README.md                 VPS install guide, step by step
.github/workflows/journal.yml  build + test + publish the Docker image on each merge
```

### Why this split
Same idea as `L1bre.Core` for the cBots: every decision (how fills become a trade, how the fill candle is cut, who sees which analysis, how statistics are computed) lives in `L1bre.Journal.Domain`, which knows nothing about the web or cTrader. I can build and test it entirely in this cloud environment with hand-made data. The cTrader part is thin and replaceable, so a cTrader API change only touches one project.

---

## 2. Milestones — one PR each, each one usable on its own

| # | Delivers | You can check |
|---|---|---|
| **1. Skeleton online** | Blazor Server app with login, invitations, roles (trader / coach / admin), PostgreSQL, Docker Compose, Caddy, GitHub Actions image, VPS install guide | Open https://fta.trading, accept your admin invitation, log in |
| **2. Link cTrader** | OAuth with view-only scope, list of accounts of each cTrader ID, tick the accounts to import, encrypted tokens + automatic refresh, re-link flow | Link your cTrader ID, see your Fusion demo account listed |
| **3. Sync trades + candles** | Every 5 min + Sync now; first import of history (throttled); fills → trades; bot trades qualified from label/comment, manual trades "to qualify"; 54 candles stored (1 TF for bot trades, M1→H4 for manual), last one cut at the fill | Your Frame demo trades appear with the right TF, P&L and win/loss |
| **4. Trades and charts** | Trades list with filters; trade page with the 54-candle chart (Lightweight Charts), fill / SL / TP markers; "To qualify" screen with instant TF switch | Open a Frame trade and compare the chart with cTrader |
| **5. Analysis and coaching** | Self + coach forms (3 scores, total, comment), blind rule, lock, coach invitations and revocation, coach view | Score a trade as trader, invite a coach account, score it blind as coach |
| **6. Dashboards** | Results, by technique / timeframe, score evolution, score vs result, self vs coach gap, review progress | Dashboard on your demo account |

Phase 2 ideas (trend replay with L1bre.Core, Discord/email notification, CSV export, two-factor login) stay out until the six milestones are done.

---

## 3. How I test

- **Domain rules** (`L1bre.Journal.Domain.Tests`): unit tests on hand-made fills, candles and ticks. Examples: a partial close gives one trade, not two; a Buy fill at Ask sits one spread above the cut candle close; a 0 % trend makes the total 0 %; a coach cannot read the self analysis before submitting.
- **cTrader client**: tested against a small fake cTrader server (`tests/FakeCTrader`) that answers the OAuth and Open API messages as documented, because **this cloud environment cannot reach the cTrader servers** (`openapi.ctrader.com` is refused by its network policy). So the **first real test with your account happens on the VPS**. Each link shows its last cTrader error on the accounts page; an admin diagnostics page comes with the sync (milestone 3), where most calls happen.
- **Web app**: started locally in this environment with a test database and checked in a headless browser before each PR.

---

## 4. What I need from you, and when

| When | What |
|---|---|
| Before milestone 1 | DNS: an **A record `fta.trading` → your VPS IPv4** (and AAAA if it has IPv6). VPS OS: Ubuntu 26.04 LTS (recommended 2026-10-08). |
| Milestone 1 install | Follow `deploy/README.md` on the VPS once: install Docker, copy two files, create `.env` with passwords, `docker compose up -d`. I can't SSH to your VPS from here, so you run the commands. |
| Before milestone 2 | Register an **Open API application** on your cTrader ID (openapi.ctrader.com → Applications): redirect URL `https://fta.trading/ctrader/callback`. Keep the client ID and secret for your `.env`, never paste the secret in this chat. Tell me if Spotware asks for approval and how long it takes. |
| Each milestone | Merge the PR, then `docker compose pull && docker compose up -d` on the VPS (2 commands, also in the README). |

---

## 5. Open points resolved during the build (no decision needed now)

- Whether cTrader candles from the Open API are Bid-based (expected, as in the cBots) — checked at milestone 3 by comparing with your cTrader chart.
- How far back the deal history goes — seen at the first import.
- Whether one Open API application accepts several redirect URLs (for the later move to the L1bre domain).
- Milestone 3 notes:
  - Label and comment come from the order behind the fill (`ProtoOAOrderDetailsReq`), not from the fill itself as design v3 said.
  - The first import starts at the account's registration date and reads one week per request (cTrader's limit), spaced by 250 ms (historical requests are limited to 5 per second).
  - Net P&L = gross + swap + commission + conversion fee, from the closing fills. Still to compare with your cTrader history: whether that commission covers both sides.
  - "Unlink" now keeps the imported trades (they will carry analyses); it erases the tokens and stops the imports.
- Milestone 2 decision: no `cTrader.OpenAPI.Net` (it targets .NET 6). The app has its own small client using the Open API's **JSON over WebSocket** (port 5036), which needs no protobuf code.

---

## 6. Technical choices (for reference)

.NET 10 (LTS; .NET 8 support ends in November 2026), Blazor Server (interactive server rendering), ASP.NET Core Identity, EF Core + Npgsql with migrations, PostgreSQL 17, Caddy 2, TradingView Lightweight Charts (shipped with the app, no CDN), Docker image published to GitHub Container Registry (private), xUnit for tests.
