# Contributing

1. Read [CLAUDE.md](CLAUDE.md) for conventions, commands and architecture rules.
2. Branch from `main`, keep changes focused, and use [Conventional Commits](https://www.conventionalcommits.org/).
3. Before pushing:
   ```bash
   dotnet format RegReturns.slnx --verify-no-changes
   dotnet build RegReturns.slnx            # zero warnings
   dotnet test --project tests/RegReturns.UnitTests
   dotnet test --project tests/RegReturns.IntegrationTests   # needs Docker
   ```
4. Add or update tests with every behaviour change, and an ADR in `docs/adr` for any significant design decision.
5. Never commit secrets, real personal data, or real institution names or branding.

Security issues: please do not open a public issue; see [SECURITY.md](SECURITY.md).
