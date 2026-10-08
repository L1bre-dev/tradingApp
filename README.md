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
src/L1bre.Journal.Domain/       plain C# rules, no web or database code (unit-tested)
tests/L1bre.Journal.Domain.Tests/
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

## Database changes

The schema is defined by the classes in `src/L1bre.Journal/Data` and versioned as EF Core migrations in
`src/L1bre.Journal/Data/Migrations`. After changing an entity:

```bash
dotnet tool install --global dotnet-ef     # once
cd src/L1bre.Journal && dotnet ef migrations add <WhatChanged> -o Data/Migrations
```

The app applies pending migrations itself when it starts (on the VPS too).
