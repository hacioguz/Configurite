# Contributing to Configurite

Thanks for the interest! Configurite is small and the bar for contributions is "make it better and don't make it weirder".

## Quick start

```bash
git clone https://github.com/hacioguz/configurite.git
cd configurite
dotnet build Configurite.slnx
dotnet test tests/Configurite.Tests/Configurite.Tests.csproj
```

You'll need .NET 8, 9, and 10 SDKs installed (see `global.json` if added).

## Code expectations

- **Bilingual XML doc comments** on every public/internal type and member, in the format:
  ```csharp
  /// <summary>
  /// EN: One-line English description.
  /// TR: Tek satırlık Türkçe açıklama.
  /// </summary>
  ```
- `dotnet format Configurite.slnx --verify-no-changes --severity warn` must pass.
- New tests for new behaviour — we run on net8/9/10, so don't break any TFM.
- Conventional commits (`feat:`, `fix:`, `docs:`, `perf:`, etc.).
- Each meaningful change gets a `docs/chat-log/NN-short-name.md` entry.

## Versioning

We follow **TFM-major versioning** — the package major mirrors the .NET target. See [`docs/en/versioning.md`](docs/en/versioning.md). Don't bump versions in PRs; releases are tag-driven.

## Pull requests

1. Fork, branch off `main`.
2. Run the test matrix locally (`dotnet test --framework net8.0` etc.).
3. Open the PR against `main`. CI runs the full 3 OS × 3 TFM matrix.
4. One reviewer approval + green CI → merge.

## Forking the repo / setting up your own deployment

If you fork this repo and want CI + Pages + Releases to work on your fork,
two manual one-time setups are required (GitHub's `GITHUB_TOKEN` is too
restricted to do these automatically):

### 1. GitHub Pages (for the docs site)

```bash
# A) UI step:  Settings → Pages → Source: "GitHub Actions"
# B) CLI step (creates the Pages site object — UI alone is insufficient):
gh api repos/<your-username>/<your-repo>/pages -X POST -F build_type=workflow
```

Without step B, `pages.yml` fails with `Get Pages site failed: Not Found`.

### 2. NuGet publishing (for the release workflow)

```
Settings → Secrets and variables → Actions → New repository secret
Name:  NUGET_API_KEY
Value: <your nuget.org API key, scoped to your package glob>
```

Without this, `release.yml` packs successfully but emits a `::warning::` and
skips the nuget.org push.

## Reporting bugs

Use **GitHub Issues**. Template will guide you through the basics: repro, expected vs actual, environment.

## Reporting security vulnerabilities

See [`SECURITY.md`](SECURITY.md). **Do not open public issues for security**.

## Code of conduct

Be kind. Assume good faith. Disagree on the merits, not the person. We follow the [Contributor Covenant](https://www.contributor-covenant.org/) by default — formal `CODE_OF_CONDUCT.md` lands when the community is large enough to need one.
