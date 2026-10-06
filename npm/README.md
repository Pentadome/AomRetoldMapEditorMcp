# aom-retold-editor-mcp

**Unofficial community project. Not affiliated with, endorsed, or supported by Microsoft.**

Windows x64 + Node.js 20+ required. Bundles self-contained .NET host, native bridge, reviewed layouts and trigger template. No .NET install, MSVC, postinstall script or runtime download. Game files and CryBar are not bundled.

## Quick start

Add this to MCP client configuration:

```json
{
  "mcpServers": {
    "aom-editor": {
      "command": "npx",
      "args": ["-y", "aom-retold-editor-mcp"]
    }
  }
}
```

No separate setup or CryBar required to start server and use native editor tools. Game must be installed at default Steam location:

```text
C:\Program Files (x86)\Steam\steamapps\common\Age of Mythology Retold\AoMRT_s.exe
```

For another location, append `"--exe", "C:\\Your Steam Library\\Age of Mythology Retold\\AoMRT_s.exe"` to `args`. Game build must match bundled reviewed layout. Open offline scenario editor before game operations; startup itself makes no game calls.

If client cannot launch Windows npm shim, use `command: "cmd"` and `args: ["/d", "/c", "npx", "-y", "aom-retold-editor-mcp"]`.

All native server options pass through unchanged (`--exe`, `--toolset core|full`, etc.). Default startup exposes core tools. Stdout is inherited JSON-RPC; errors go to stderr. Stdin EOF reaches host; exit status and termination signals propagate.

Global install also works: `npm install -g aom-retold-editor-mcp`, then configure `command: "aom-editor-mcp"`.

## Optional game-data metadata

CryBar is needed **only** to generate game-data catalogs/dependencies and decode XML-driven UI actions. Without metadata, catalog/dependency lookups refuse with generation guidance; decoded XML action aliases are absent. Executable-derived native commands and installed editor hotkeys do not need CryBar. Full documented tool counts assume generated metadata.

To enable these extras, pin same release version in setup commands and client config (example `0.1.0`; replace with published release):

```powershell
$runtime = npx -y aom-retold-editor-mcp@0.1.0 --runtime-dir
# Download CryBar CLI separately; place crybar.exe + dependencies in $runtime\crybar\.
npx -y aom-retold-editor-mcp@0.1.0 setup -GameExe 'C:\Program Files (x86)\Steam\steamapps\common\Age of Mythology Retold\AoMRT_s.exe'
```

[CryBar CLI](https://github.com/CryShana/CryBarEditor) stays separate; wrapper does not download or execute it automatically. Optional setup reuses release `setup.ps1`: checks game hash, generates metadata, prints direct-executable MCP configuration without overwriting client config. PowerShell execution policy remains unchanged.

Metadata and separately supplied CryBar live inside installed package's `runtime` folder. Setup and client must launch **same package copy**. Rerun optional setup after upgrading/reinstalling package or clearing npm cache, and after supported game updates. Pinning package version avoids unexpected upgrades. No automatic layout activation or copied offsets.

Use only offline scenario editor, never multiplayer. Retain normal MCP approval/security policies. [Full documentation, limitations and source](https://github.com/Pentadome/AomRetoldMapEditorMcp).

## Maintainer packaging

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

## License

MIT; see included `LICENSE`. CryBar, game resources and third-party runtime components retain their own licenses and ownership.
