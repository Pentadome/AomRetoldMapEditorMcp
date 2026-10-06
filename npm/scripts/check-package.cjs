'use strict';

const assert = require('node:assert/strict');
const { copyFileSync, existsSync, readFileSync, readdirSync } = require('node:fs');
const { join } = require('node:path');
const runtime = join(__dirname, '..', 'runtime');
try {
  // npm includes root LICENSE automatically; keep single source of license text in repository.
  copyFileSync(join(__dirname, '..', '..', 'LICENSE'), join(__dirname, '..', 'LICENSE'));
  for (const name of [
    'AomMcp.exe', 'AomMcp.dll', 'AomMcp.deps.json', 'AomMcp.runtimeconfig.json',
    'AomEditorBridge.dll', 'trigger-controller-template.trg', 'setup.ps1', 'TOOLS.md',
    'Sdcb.SimdPaddleOCR.dll', 'Sdcb.SimdPaddleOCR.Models.ChineseV6Tiny.dll',
  ]) {
    assert(existsSync(join(runtime, name)), `Missing runtime/${name}; copy CI release payload before packing.`);
  }
  const config = JSON.parse(readFileSync(join(runtime, 'AomMcp.runtimeconfig.json'), 'utf8'));
  assert(config.runtimeOptions.includedFrameworks?.length, 'Runtime must be self-contained.');
  assert(!config.runtimeOptions.framework && !config.runtimeOptions.frameworks, 'External runtime not allowed.');
  assert(readdirSync(join(runtime, 'layouts')).some(name => /^[a-f0-9]{64}\.json$/.test(name)),
    'Reviewed layouts required.');
  assert(existsSync(join(runtime, 'uilayouts', 'alt-2560x1440.json')),
    'Reviewed alternative-UI layout required.');
  for (const name of ['ocr-control.bgra', 'ocr-pop.bgra', 'ocr-food.bgra', 'ocr-age.bgra']) {
    assert(existsSync(join(runtime, 'fixtures', name)), `Missing OCR fixture ${name}.`);
  }
  console.error('PASS: npm runtime payload ready.');
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
