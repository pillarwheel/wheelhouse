# Contributing to WheelHouse

Thanks for your interest in improving WheelHouse! This guide keeps contributions smooth.

## Getting set up

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. `git clone` your fork and `cp .env.example .env`.
3. Build and run the tests:
   ```bash
   dotnet build
   dotnet test
   ```

## Ground rules

- **Keep it green.** The offline test suite (`dotnet test`) must pass. Live API tests are gated
  behind `WHEELHOUSE_LIVE_TESTS=1` and shouldn't run in normal CI.
- **Schema changes use migrations.** After editing an entity, add a migration:
  ```bash
  dotnet ef migrations add <Name> \
    --project src/WheelHouse.Infrastructure \
    --startup-project src/WheelHouse.Web \
    --output-dir Persistence/Migrations
  ```
  It's applied automatically on the next launch — never use `EnsureCreated`.
- **Match the surrounding style.** Nullable reference types are on; prefer small, pure, testable
  helpers in `WheelHouse.Core` and keep DB/process code in `WheelHouse.Infrastructure`.
- **No secrets in commits.** `.env` is git-ignored; put example values in `.env.example`.

## Project layout

```
src/
  WheelHouse.Core/            domain models, interfaces, pure logic
  WheelHouse.Infrastructure/  EF Core, agents (Claude/Gemini), RAG, runners
  WheelHouse.Web/             Blazor Server UI
  WheelHouse.Desktop/         Photino native shell
tests/
  WheelHouse.Tests/           xUnit tests
```

## Pull requests

- Keep PRs focused; describe the change and how you verified it.
- Add or update tests for new logic.
- If a change is observable in the UI, a screenshot helps.

## Reporting issues

Open an issue with steps to reproduce, expected vs actual behavior, and your OS / .NET version.
For security-sensitive reports, please contact the maintainers privately rather than filing a public issue.
