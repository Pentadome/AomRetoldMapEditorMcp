# Age of Mythology: Retold Editor MCP

> [!IMPORTANT]
> **Unofficial community project.** Not affiliated with, endorsed or supported by Microsoft.

Let an AI agent (Claude, ChatGPT, Copilot, etc.) build scenarios in the **Age of Mythology: Retold scenario editor**. The agent can place units and buildings, paint terrain, shape elevation, move the camera, edit triggers, set up players and diplomacy, take screenshots and more.

It works through the game's own editor commands. It doesn't patch the game or bypass any protection.

> [!WARNING]
> This is beta software. If a tool fails or crashes the game, ask your agent to open an issue in this repo. If your agent keeps falling back to screenshots and mouse clicks, ask it which tool it was missing and consider opening an issue or PR for it.

## Requirements

- Windows x64
- Age of Mythology: Retold (a supported game version)
- [Node.js](https://nodejs.org/) 20 or newer (for the npm install)
- An MCP-capable AI client

## Install

Add this to your MCP client's configuration:

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

If the game isn't installed in Steam's default location, add the path to the game exe:

```json
"args": ["-y", "aom-retold-editor-mcp", "--exe", "D:\\Games\\Age of Mythology Retold\\AoMRT_s.exe"]
```

Then start the game, open the **scenario editor**, and ask your agent to build something.

More install options are in [npm/README.md](npm/README.md).

### Without Node.js

Download `AomRetoldMapEditorMcp-win-x64.zip` from [Releases](https://github.com/Pentadome/AomRetoldMapEditorMcp/releases), extract it and point your MCP client at `AomMcp.exe` (add `--exe <game path>` if needed). Nothing else to install.

## Optional: game-data catalogs

Some tools look up game data, like exact unit names for a pantheon, gods, techs and terrain types. They need metadata generated from your own game files. Ask the agent to run `editor_generate_catalog` (uses the bundled [CryBar](https://github.com/CryShana/CryBarEditor) library).

For writing AI, random map and trigger scripts, `editor_xs_api` looks up engine functions and the game's shipped script libraries. The project ships only their signatures and its own short summaries; the game's official help text is read from your install.

## Good to know

- **Offline editor only.** Never use it in multiplayer.
- **Save your work first.** The agent can change and delete things. Edits aren't atomic, and a failed step isn't rolled back.
- **Game updates** can break support until the project is updated for the new build. Unknown versions are refused instead of guessed.
- The server starts with a smaller **core** tool set (75 tools). From core the agent can still find and call any of the 900+ **full** tools through `editor_search_tools` and `editor_call`, or switch to the full set when needed.
- Normal and alternative editor UIs are both supported. Best tested at 2560×1440 and 1920×1080.

## More docs

- [Technical reference](docs/TECHNICAL.md): building from source, the full tool list, the generator and what's been verified
- [TOOLS.md](TOOLS.md): reference for every tool
- [research/](research/): detailed notes and evidence
- [Releasing](docs/RELEASING.md): for maintainers

## Credits

Thanks to **CryShana** for [CryBar / CryBarEditor](https://github.com/CryShana/CryBarEditor), whose library (git submodule `lib/CryBarEditor`, bundled in releases) reads the game's data archives.

## License

[MIT](LICENSE). Game assets, CryBar and third-party components keep their own licenses. This license doesn't imply any Microsoft endorsement.

This project was quickly "vibe-coded" using ChatGPT 6.1-sol and then Claude Opus 5.5.
