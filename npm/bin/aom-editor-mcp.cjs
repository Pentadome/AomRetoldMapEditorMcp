#!/usr/bin/env node
'use strict';

const { spawn } = require('node:child_process');
const { join } = require('node:path');
const { constants } = require('node:os');
const runtime = join(__dirname, '..', 'runtime');

function command(args) {
  if (args[0] === 'setup') {
    return ['powershell.exe', [
      '-NoLogo', '-NoProfile', '-NonInteractive', '-File', join(runtime, 'setup.ps1'),
      ...args.slice(1),
    ]];
  }
  return [join(runtime, 'AomMcp.exe'), args];
}

function main(args) {
  if (args[0] === '--help') {
    console.log(`aom-editor-mcp [native server options]
aom-editor-mcp setup -GameExe <path> [-Toolset core|full]
aom-editor-mcp --runtime-dir

Windows x64 only. Bundled .NET runtime; no SDK needed.
Game-data catalogs: call MCP tool editor_generate_catalog, or run setup.
Run setup once, then configure MCP client to launch aom-editor-mcp.
Server options pass through unchanged, e.g. --exe <path> --toolset core|full.
Use only in offline scenario editor, never multiplayer.`);
    return;
  }
  if (args[0] === '--runtime-dir') {
    console.log(runtime);
    return;
  }
  if (process.platform !== 'win32' || process.arch !== 'x64') {
    console.error('aom-editor-mcp: Windows x64 required.');
    process.exitCode = 1;
    return;
  }
  const [exe, forwarded] = command(args);
  // Inherit all streams: stdout stays JSON-RPC; stdin EOF reaches native host.
  const child = spawn(exe, forwarded, { stdio: 'inherit', shell: false });
  const handlers = new Map();
  for (const signal of ['SIGINT', 'SIGTERM']) {
    const handler = () => child.kill(signal);
    handlers.set(signal, handler);
    process.on(signal, handler);
  }
  child.once('error', (error) => {
    console.error(`aom-editor-mcp: ${error.message}`);
    process.exitCode = 1;
  });
  child.once('close', (code, signal) => {
    for (const [name, handler] of handlers) process.removeListener(name, handler);
    const status = code ?? (signal ? 128 + constants.signals[signal] : 1);
    process.exitCode = status < 0 ? 1 : status;
  });
}

module.exports = { command, main };
if (require.main === module) main(process.argv.slice(2));
