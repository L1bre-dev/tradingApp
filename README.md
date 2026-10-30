# L1bre Journal (tradingApp)

Web app to review trades: traders link their cTrader accounts, their trades are imported with the
54 candles up to the fill, and each trade is scored by the trader and, blind, by a coach.
Runs at https://fta.trading. The cBots themselves live in the separate `L1bre-dev/trading` repository.

- Design: [docs/design.md](docs/design.md)
- Build plan and milestones: [docs/build-plan.md](docs/build-plan.md)
- Installing and running it on the VPS: [deploy/README.md](deploy/README.md)

## Layout

```
src/L1bre.Journal/              Blazor Server web app (.NET 10), EF Core + PostgreSQL, ASP.NET Identity
src/L1bre.Journal.Domain/       plain C# rules, no web or database code (unit-tested): fills → trades, cut candle, qualification
src/L1bre.Journal.CTrader/      the only code talking to cTrader Open API (OAuth + JSON over WebSocket)
tests/L1bre.Journal.Domain.Tests/
tests/L1bre.Journal.CTrader.Tests/  cTrader client tests, run against the fake server below
tests/FakeCTrader/              a small fake cTrader (OAuth pages + Open API messages) for tests and local runs
deploy/                         docker-compose (Caddy + app + PostgreSQL), Caddyfile, backup script, VPS guide
.github/workflows/ci.yml        tests on every PR; publishes the Docker image on every merge to main
```

## Running it locally

Needs the .NET 10 SDK and a PostgreSQL server.

```bash
# once: a database matching appsettings.json
sudo -u postgres psql -c "CREATE USER journal WITH PASSWORD 'journal' CREATEDB;" -c "CREATE DATABASE journal OWNER journal;"

cd src/L1bre.Journal
App__AdminEmail=you@example.com dotnet run
```

The database schema is created at start-up. While no admin exists, the console prints
`FIRST ADMIN INVITATION for ...: <link>`: open it to create the admin account.

Tests: `dotnet test`

### Linking cTrader locally

The real cTrader servers need an approved Open API application and a public redirect URI, so locally the app
talks to the fake server instead:

```bash
dotnet run --project tests/FakeCTrader          # listens on http://127.0.0.1:5099

cd src/L1bre.Journal
CTrader__ClientId=fake-client-id CTrader__ClientSecret=fake-client-secret \
CTrader__AuthorizeUrl=http://127.0.0.1:5099/authorize CTrader__TokenUrl=http://127.0.0.1:5099/apps/token \
CTrader__DemoEndpoint=ws://127.0.0.1:5099/ CTrader__LiveEndpoint=ws://127.0.0.1:5099/ dotnet run
```

"Link a cTrader ID" then links a fake cTrader ID with one demo and one live account. Clicking Import on one
imports the fake history (a Frame M15 win, a manual loss, a Frame H1 closed in two parts, a rejected order
that must not appear, and a Frame M5 still open) with its candles. `curl http://127.0.0.1:5099/test/close-open-position`
closes the open one, to check that the next sync updates it.

The sync runs every 5 minutes (`Sync__IntervalMinutes`) and on "Sync now".

## Database changes

The schema is defined by the classes in `src/L1bre.Journal/Data` and versioned as EF Core migrations in
`src/L1bre.Journal/Data/Migrations`. After changing an entity:

```bash
dotnet tool install --global dotnet-ef     # once
cd src/L1bre.Journal && dotnet ef migrations add <WhatChanged> -o Data/Migrations
```

The app applies pending migrations itself when it starts (on the VPS too).
