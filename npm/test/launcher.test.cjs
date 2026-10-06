'use strict';

const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const { join, dirname } = require('node:path');
const { readFileSync } = require('node:fs');
const { EventEmitter } = require('node:events');
const { runInNewContext } = require('node:vm');
const test = require('node:test');
const { command } = require('../bin/aom-editor-mcp.cjs');
const launcher = join(__dirname, '..', 'bin', 'aom-editor-mcp.cjs');
const runtime = join(__dirname, '..', 'runtime');

test('native options pass through unchanged, including spaces and backslashes', () => {
  const args = ['--exe', 'C:\\Steam Library\\AoMRT_s.exe', '--toolset', 'full'];
  assert.deepEqual(command(args), [join(runtime, 'AomMcp.exe'), args]);
  assert.deepEqual(command([]), [join(runtime, 'AomMcp.exe'), []]);
});

test('setup reuses release script without shell evaluation', () => {
  const args = ['-GameExe', 'C:\\Steam Library\\AoMRT_s.exe', '-Toolset', 'core'];
  assert.deepEqual(command(['setup', ...args]), ['powershell.exe', [
    '-NoLogo', '-NoProfile', '-NonInteractive', '-File', join(runtime, 'setup.ps1'), ...args,
  ]]);
});

test('help and runtime path work before game setup', () => {
  const help = spawnSync(process.execPath, [launcher, '--help'], { encoding: 'utf8' });
  assert.equal(help.status, 0, help.stderr);
  assert.match(help.stdout, /Windows x64 only/);
  assert.match(help.stdout, /setup -GameExe/);
  const path = spawnSync(process.execPath, [launcher, '--runtime-dir'], { encoding: 'utf8' });
  assert.equal(path.status, 0, path.stderr);
  assert.equal(path.stdout.trim(), runtime);
});

test('stdio, signals, exit status and spawn failures propagate without stdout noise', () => {
  // Isolated process stub lets Linux CI check Windows-only launch behavior without native effects.
  for (const failure of [false, true]) {
    const child = new EventEmitter();
    const fakeProcess = Object.assign(new EventEmitter(), { platform: 'win32', arch: 'x64' });
    const errors = [];
    let killed;
    child.kill = signal => { killed = signal; };
    const module = { exports: {} };
    runInNewContext(readFileSync(launcher, 'utf8'), {
      __dirname: dirname(launcher), module, process: fakeProcess,
      console: { error: text => errors.push(text), log: () => assert.fail('Unexpected stdout') },
      require: name => name === 'node:child_process' ? {
        spawn: (exe, args, options) => {
          assert.equal(exe, join(runtime, 'AomMcp.exe'));
          assert.deepEqual([...args], ['--toolset', 'full']);
          assert.equal(options.stdio, 'inherit');
          assert.equal(options.shell, false);
          return child;
        },
      } : require(name),
    });
    module.exports.main(['--toolset', 'full']);
    if (failure) {
      child.emit('error', new Error('spawn ENOENT'));
      child.emit('close', -4058, null);
      assert.equal(fakeProcess.exitCode, 1);
      assert.deepEqual(errors, ['aom-editor-mcp: spawn ENOENT']);
    } else {
      fakeProcess.emit('SIGTERM');
      assert.equal(killed, 'SIGTERM');
      child.emit('close', 7, null);
      assert.equal(fakeProcess.exitCode, 7);
      assert.deepEqual(errors, []);
    }
    assert.equal(fakeProcess.listenerCount('SIGTERM'), 0);
    assert.equal(fakeProcess.listenerCount('SIGINT'), 0);
  }
});

if (process.platform !== 'win32' || process.arch !== 'x64') {
  test('unsupported platform refuses with stderr only', () => {
    const result = spawnSync(process.execPath, [launcher], { encoding: 'utf8' });
    assert.equal(result.status, 1);
    assert.equal(result.stdout, '');
    assert.match(result.stderr, /Windows x64 required/);
  });
}
