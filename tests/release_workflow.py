"""Dependency-free release guard/dist-tag check. Never invokes real npm publish."""
import os
import shutil
import subprocess
import textwrap
from pathlib import Path

bash = shutil.which('bash')
assert bash, 'Bash required to check release shell commands.'
workflow = (Path(__file__).resolve().parents[1] / '.github/workflows/release.yml').read_text()
publish = workflow.split('\n  publish-npm:\n', 1)[1].split('\n  release:\n', 1)[0]
assert 'needs: validate' in publish
assert "if: github.event_name == 'push' && startsWith(github.ref, 'refs/tags/v')" in publish
assert 'contents: read' in publish and 'id-token: write' in publish
assert workflow.count('id-token: write') == 1
assert 'NODE_AUTH_TOKEN' not in publish and 'NPM_TOKEN' not in publish
assert "node-version: '24'" in publish
script = textwrap.dedent(publish.split('        run: |\n', 1)[1])
assert script.index('sha256sum --check') < script.index('npm publish')
# Shell functions intercept both external commands; no files/network/registry writes.
stubs = '''set -eu
sha256sum() { test "$1" = --check && test "$2" = npm-package.sha256; }
npm() { printf '%s\\n' "$@"; }
'''
for version, expected in [('v1.2.3', 'latest'), ('v1.2.3-rc.1', 'next'),
                          ('v1.2.3+build-1', 'latest')]:
    # Bytes keep LF intact when invoking Bash from Windows Python.
    result = subprocess.run([bash, '-s'], input=(stubs + script).encode(),
                            capture_output=True,
                            env={**os.environ, 'RELEASE_TAG': version})
    assert result.returncode == 0, result.stderr.decode()
    args = result.stdout.decode().splitlines()
    assert args[0] == 'publish' and '--ignore-scripts' in args
    assert args[args.index('--tag') + 1] == expected, (version, args)
    assert args[args.index('--registry') + 1] == 'https://registry.npmjs.org'
    assert args[args.index('--access') + 1] == 'public'
blocked = subprocess.run([bash, '-s'], capture_output=True,
                         input=('set -eu\nsha256sum() { return 1; }\nnpm() { echo unexpected-publish; }\n' + script).encode(),
                         env={**os.environ, 'RELEASE_TAG': 'v1.2.3'})
assert blocked.returncode != 0 and not blocked.stdout, 'Checksum failure must block publishing.'
print('PASS: validated/tag-only OIDC job, stable/prerelease dist-tags, checksum refusal; no real publish.')
