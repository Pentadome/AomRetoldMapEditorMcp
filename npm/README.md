# aom-retold-editor-mcp

**Unofficial community project.** Not affiliated with, endorsed or supported by Microsoft.

MCP server that lets an AI agent build scenarios in the **Age of Mythology: Retold** scenario editor. Everything needed is bundled, so you don't need to install .NET separately.

**Requires:** Windows x64, Node.js 20+, Age of Mythology: Retold.

## Setup

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

Then start the game, open the **scenario editor**, and talk to your agent.

**Game in a different folder?** Add the exe path to `args`:

```json
"args": ["-y", "aom-retold-editor-mcp", "--exe", "D:\\Games\\Age of Mythology Retold\\AoMRT_s.exe"]
```

The default path is `C:\Program Files (x86)\Steam\steamapps\common\Age of Mythology Retold\AoMRT_s.exe`.

**Client can't start `npx`?** Use `"command": "cmd"` with `"args": ["/d", "/c", "npx", "-y", "aom-retold-editor-mcp"]`.

**Prefer a global install?** Run `npm install -g aom-retold-editor-mcp`, then use `"command": "aom-editor-mcp"`.

## Optional game-data catalogs

Tools that look up game data (unit names per pantheon, gods, techs, terrain, etc.) need metadata generated from your game files. Everything else works without it.

Ask the agent to run the `editor_generate_catalog` tool. It needs no running game and uses the bundled [CryBar](https://github.com/CryShana/CryBarEditor) library.

Or run setup:
   ```powershell
   npx -y aom-retold-editor-mcp@0.1.0 setup -GameExe 'C:\Program Files (x86)\Steam\steamapps\common\Age of Mythology Retold\AoMRT_s.exe'
   ```

Replace `0.1.0` with the current version, and use the same pinned version in your MCP config. Metadata lives inside that package copy, so rerun setup after upgrading the package or updating the game.

## Notes

- Use only in the offline editor, never in multiplayer.
- Save your scenario before letting the agent make big changes.

[Full documentation and source](https://github.com/Pentadome/AomRetoldMapEditorMcp)

## License

MIT. Game assets, CryBar and third-party components keep their own licenses.
