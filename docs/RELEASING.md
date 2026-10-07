# Releasing

Pushing a new `vX.Y.Z` tag builds Windows release on Linux, validates ZIP/npm installation on Windows, then publishes validated tarball to npm and creates GitHub Release. Package version comes from tag; no manual `package.json` version bump needed. Stable versions update `latest`; prerelease tags (e.g. `v0.2.0-rc.1`) publish under `next` so ordinary `npx` users stay on stable release. PRs, branch pushes and manual workflow dispatch never publish.

One-time npm setup: open `aom-retold-editor-mcp` package **Settings → Trusted Publisher**, choose **GitHub Actions**, and enter:

- Organization/user: `Pentadome`
- Repository: `AomRetoldMapEditorMcp`
- Workflow filename: `release.yml` (not full path)
- Environment: leave blank (workflow uses none)

[Trusted publishing](https://docs.npmjs.com/trusted-publishers/) uses GitHub OIDC, not an npm token. Only tag-gated `publish-npm` job has `id-token: write`; provenance is generated automatically. No `NPM_TOKEN`/`NODE_AUTH_TOKEN` secret needed. Publisher settings must exactly match repository/workflow identity; configure them before pushing release tag.

```bash
git tag v0.1.1
git push origin v0.1.1
```

Example only: use an unpublished semver version each time. npm versions are immutable; publishing existing version fails. GitHub Release and npm publishing run independently after validation; rerun failed publish job after correcting authentication/settings, without rebuilding or republishing successful GitHub Release.

From source, copy clean self-contained release payload to `npm/runtime` (no game files), then run `npm pack` inside `npm`. Missing payload fails prepack. Package has no Node dependencies.
