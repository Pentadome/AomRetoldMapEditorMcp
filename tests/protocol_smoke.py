"""Stdlib MCP smoke check. --live adds read-only state/image + one harmless deselect.
No live unit placement/game save/load/import or protection bypass. Local file fixtures use temp copies only.
"""
import argparse
import base64
import hashlib
import json
import pathlib
import re
import shutil
import struct
import subprocess
import tempfile
import time
import zlib
from workflow_fixtures import trigger_fixture, checkpoint_fixture
from datetime import datetime, timezone, timedelta

root = pathlib.Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--live', action='store_true')
parser.add_argument('--app', type=pathlib.Path, default=root / 'src/AomMcp/bin/Release/net10.0-windows/AomMcp.dll')
options = parser.parse_args()
app = options.app.resolve()
subprocess.run(['dotnet', str(app), '--self-test'], check=True, cwd=root)
# Local metadata/preflight tests use impossible PID (Int32.MaxValue), proving no Game connection.
host_args = ['dotnet', str(app), '--toolset', 'full'] + ([] if options.live else ['--pid', '2147483647'])
p = subprocess.Popen(host_args, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                     stderr=subprocess.PIPE, text=True, cwd=root, creationflags=subprocess.CREATE_NO_WINDOW)
sequence = 0
notifications = []
workflow_names = {'editor_unit_notes', 'editor_scenario_diff', 'editor_install_ai', 'editor_startup_orders',
                  'editor_runtime_probe', 'editor_runtime_report', 'editor_playtest'}

def send(method, params=None):
    global sequence
    sequence += 1
    assert p.stdin is not None
    message = {'jsonrpc': '2.0', 'id': sequence, 'method': method}
    if params is not None:
        message['params'] = params
    p.stdin.write(json.dumps(message) + '\n')
    p.stdin.flush()
    return sequence

def notify(method, params=None):
    assert p.stdin is not None
    p.stdin.write(json.dumps({'jsonrpc': '2.0', 'method': method, 'params': params or {}}) + '\n')
    p.stdin.flush()

def receive():
    assert p.stdout is not None
    while True:
        line = p.stdout.readline()
        assert line, 'MCP process exited'
        message = json.loads(line)
        if message.get('method') == 'notifications/tools/list_changed':
            assert 'id' not in message, message
            notifications.append(message)
            continue
        return message

def request(method, params=None, error_code=None):
    expected = send(method, params)
    message = receive()
    assert message.get('id') == expected, message
    if error_code is not None:
        assert message['error']['code'] == error_code, message
        return message['error']
    assert 'error' not in message, message
    return message['result']

def full_specs_annotations(tools):
    return {t['name']: t.get('annotations', {}).get('readOnlyHint') for t in tools}


def tool(name, args=None):
    return request('tools/call', {'name': name, 'arguments': args or {}})

def assert_workflow_tools():
    def success(name, args):
        response = tool(name, args)
        assert not response['isError'], response
        return response['structuredContent']

    def refusal(name, args):
        response = tool(name, args)
        assert response['isError'] and 'Process with an Id' not in str(response), response
        return response

    with tempfile.TemporaryDirectory(prefix='aom-workflow-protocol-') as tmp:
        folder = pathlib.Path(tmp)
        before = folder / 'before.mythscn'
        after = folder / 'after.mythscn'
        before.write_bytes(checkpoint_fixture())
        after.write_bytes(checkpoint_fixture(note='changed note'))
        sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
        notes_args = {'scenarioPath': str(before), 'expectedSha256': sha(before), 'player': 6,
                      'noteContains': '塔', 'unitIds': [100], 'limit': 1}
        notes = success('editor_unit_notes', notes_args)
        assert notes['total'] == 1 and notes['units'][0]['note'] == '塔 — Δέντρο 🌴', notes
        assert success('editor_unit_notes', {**notes_args, 'offset': 999})['units'] == []
        refusal('editor_unit_notes', {**notes_args, 'expectedSha256': '0' * 64})
        unsupported = folder / 'unsupported.mythscn'
        unsupported.write_bytes(checkpoint_fixture(world_header=999))
        refusal('editor_unit_notes', {'scenarioPath': str(unsupported), 'expectedSha256': sha(unsupported)})
        comparison = {'beforePath': str(before), 'expectedBeforeSha256': sha(before),
                      'afterPath': str(after), 'expectedAfterSha256': sha(after), 'offset': 999, 'limit': 1,
                      'assertions': [{'scope': 'entities', 'policy': 'preserve', 'fields': ['note']}]}
        diff = success('editor_scenario_diff', comparison)
        assert not diff['universalUnchangedVerified'] and not diff['decodingComplete'], diff
        assert 'fail' in str(diff['assertions']), diff
        xs = folder / 'source.xs'
        xs.write_text('void main() {}\n', encoding='utf-8')
        ai = success('editor_install_ai', {'sourcePath': str(xs), 'expectedSha256': sha(xs), 'destination': folder.name + '.xs'})
        assert ai['preview'] and not ai['runtimeVerified'] and not ai['compilationVerified'], ai
        refusal('editor_install_ai', {'sourcePath': str(xs), 'expectedSha256': sha(xs), 'destination': '../stock.xs'})
        scene = folder / 'workers.mythscn'
        scene.write_bytes(checkpoint_fixture(units=[(100, 1, 'VillagerAztec', 10, 0, 20, None),
            (101, 1, 'VillagerAztec', 12, 0, 22, None), (200, 1, 'Farm', 11, 0, 21, None), (201, 1, 'Farm', 13, 0, 23, None)]))
        tr = folder / 'startup.trg'
        tr.write_bytes(trigger_fixture(selection=0, trailer=0))
        startup = {'operation': 'plan', 'path': str(tr), 'expectedSha256': sha(tr), 'scenarioPath': str(scene),
                   'expectedScenarioSha256': sha(scene), 'triggerId': 704, 'expectedName': 'Startup',
                   'workers': [{'unitId': i, 'player': 1, 'proto': 'VillagerAztec'} for i in [100, 101]],
                   'targets': [{'unitId': i, 'player': 1, 'proto': 'Farm'} for i in [200, 201]],
                   'player': 1, 'targetPlayer': 1, 'workerProtos': ['VillagerAztec'], 'targetProtos': ['Farm'],
                   'job': 'farm', 'maxDistance': 100, 'templateEffectIndex': 0}
        plan = success('editor_startup_orders', startup)
        assert len(plan['preserved']) == 1 and plan['assignments'][0]['workerId'] == 101 and plan['assignments'][0]['targetId'] == 201, plan
        output = folder / 'planned.trg'
        written = success('editor_startup_orders', {**startup, 'operation': 'write', 'outputPath': str(output), 'confirmWrite': True})
        assert written['sha256'] == sha(output)
        refusal('editor_startup_orders', {**startup, 'operation': 'write', 'outputPath': str(output), 'confirmWrite': True})
        probe_args = {'runId': 'protocol_fixture', 'player': 6, 'runtimeIdentityReviewed': True, 'includePlans': True,
                      'selectors': [{'key': 'workers', 'kind': 'worker', 'runtimeProtoId': 12, 'runtimeStateId': 2, 'maxMatches': 44, 'savedIdAnnotation': 31332}]}
        probe = success('editor_runtime_probe', probe_args)
        assert not probe['runtimeVerified'] and 'xsSetContextPlayer' not in probe['code'], probe
        assert 'kbUnitGetActionType(31332)' not in probe['code']
        probe_path = folder / 'probe.xs'
        probe = success('editor_runtime_probe', {**probe_args, 'preview': False, 'outputPath': str(probe_path), 'confirmWrite': True})
        assert probe['sha256'] == sha(probe_path)
        refusal('editor_runtime_probe', {**probe_args, 'preview': False, 'outputPath': str(probe_path), 'confirmWrite': True})
        transcript = folder / 'evidence.txt'
        prefix = 'AOMMCP1|protocol_fixture|5|6|'
        transcript.write_text(prefix + 'BEGIN\n' + prefix + 'UNIT|workers|700|12|2|9|900|10|20\n'
                              + prefix + 'PRESENCE|workers|1\n' + prefix + 'PLAN_COUNT|0\n' + prefix + 'END\n', encoding='utf-8')
        now = datetime.now(timezone.utc)
        evidence = {'evidencePath': str(transcript), 'expectedSha256': sha(transcript), 'runId': 'protocol_fixture', 'player': 6,
                    'runStartedAtUtc': (now - timedelta(seconds=5)).isoformat(), 'capturedAtUtc': now.isoformat(), 'minGameTime': 5, 'maxGameTime': 5,
                    'offset': 999, 'limit': 1, 'assertions': [{'check': 'workerTarget', 'key': 'workers', 'runtimeUnitId': 700, 'expectedTargetKbId': 900}, {'check': 'compilation'}]}
        report = success('editor_runtime_report', evidence)
        assert report['assertions'][0]['status'] == 'pass' and report['assertions'][1]['status'] == 'unsupported' and report['units'] == [], report
        assert not report['engineTransportVerified'] and not report['compilationVerified'], report
        refusal('editor_runtime_report', {**evidence, 'runId': 'wrong_run'})
        refusal('editor_runtime_report', {**evidence, 'capturedAtUtc': (now - timedelta(hours=1)).isoformat()})
        play_caps = success('editor_playtest', {'operation': 'preview'})
        assert play_caps['available'] and play_caps['profileCount'] == 4 and len(play_caps['profiles']) == 4 and not play_caps['nativeStartTest'] and not play_caps['nativeLoad'], play_caps
        profile_path = app.parent / play_caps['profile']
        profile_preview = success('editor_playtest', {'operation': 'preview', 'profilePath': str(profile_path), 'expectedProfileSha256': sha(profile_path)})
        assert profile_preview['reviewed'] and not profile_preview['inputSent'] and not profile_preview['runtimeTelemetry']['available'], profile_preview
        for registered in play_caps['profiles']:
            other = app.parent / registered['profile']
            assert sha(other) == registered['expectedProfileSha256'], registered
            assert success('editor_playtest', {'operation': 'preview', 'profilePath': str(other), 'expectedProfileSha256': registered['expectedProfileSha256']})['reviewed'], registered
        refusal('editor_playtest', {'operation': 'quit', 'token': 'foreign_host', 'confirmQuit': True})
        caps = success('editor_capabilities', {})['workflowTools']
        assert set(caps['names']) == workflow_names and caps['playtest']['available'] and caps['playtest']['profileCount'] == 4 and not caps['telemetry']['available'], caps


def assert_tool_search(full_specs, expected_core, mode):
    schema = full_specs['editor_search_tools']
    assert schema['annotations'] == {'readOnlyHint': True, 'destructiveHint': False,
                                     'idempotentHint': True, 'openWorldHint': False}, schema
    assert schema['inputSchema']['required'] == ['query']
    assert schema['inputSchema']['properties']['query'] == {'type': 'string', 'minLength': 1, 'maxLength': 256}
    assert schema['inputSchema']['properties']['limit'] == {'type': 'integer', 'minimum': 1, 'maximum': 50}

    def check(query, offset=0, limit=10, defaults=False):
        arguments = {'query': query} if defaults else {'query': query, 'offset': offset, 'limit': limit}
        response = tool('editor_search_tools', arguments)
        assert not response['isError'], response
        result = response['structuredContent']
        assert json.loads(response['content'][0]['text']) == result
        normalized = query.strip()
        terms = normalized.lower().split()
        matches = [spec for spec in full_specs.values() if all(
            term in spec['name'].lower() or term in spec['description'].lower() for term in terms)]

        def rank(spec):
            name = spec['name'].lower()
            priority = (0 if name == normalized.lower() else 1 if normalized.lower() in name
                        else 2 if all(term in name for term in terms) else 3)
            return priority, spec['name']

        matches.sort(key=rank)
        expected = [{'name': spec['name'], 'description': spec['description'],
                     'available': mode == 'full' or spec['name'] in expected_core,
                     'requiredToolset': 'core' if spec['name'] in expected_core else 'full'}
                    for spec in matches[offset:offset + limit]]
        assert set(result) == {'toolset', 'query', 'total', 'offset', 'limit', 'nextOffset', 'tools', 'guidance'}, result
        assert result['toolset'] == mode and result['query'] == normalized, result
        assert (result['total'], result['offset'], result['limit']) == (len(matches), offset, limit), result
        assert result['tools'] == expected, result
        next_offset = offset + len(expected) if offset + len(expected) < len(matches) else None
        assert result['nextOffset'] == next_offset, result
        assert 'editor_toolset mode=full' in result['guidance'] and 'tools/list' in result['guidance']
        return result

    action = next(name for name in sorted(full_specs) if name.startswith('action_'))
    for name in ('editor_search_tools', 'editor_place_unit', 'editor_undo', 'editor_uiPlaceAtPointer', action):
        result = check(name)
        assert result['tools'][0]['name'] == name, result  # Exact name outranks description references.
    for query in ('uiplaceatpointer', 'EDITOR_UIPLACEATPOINTER', 'one-shot',
                  'Native\t placement', 'dispatcher return', 'editor_place_unit one-shot', 'editor\tui', 'Shipped editor UI action:'):
        check(query)
    description_only = check('one-shot')
    assert any(t['name'] == 'editor_place_unit' for t in description_only['tools']), description_only
    assert all('one-shot' not in t['name'].lower() for t in description_only['tools']), description_only
    assert check('editor', defaults=True) == check('editor'), 'Default bounds differ'
    first = check('editor', limit=7)
    assert first == check('editor', limit=7), 'Search ordering is unstable'
    second = check('editor', offset=first['nextOffset'], limit=7)
    assert not {t['name'] for t in first['tools']} & {t['name'] for t in second['tools']}
    collected = []
    offset = 0
    while True:
        page = check('editor', offset=offset, limit=50)
        collected.extend(t['name'] for t in page['tools'])
        if page['nextOffset'] is None:
            break
        offset = page['nextOffset']
    assert len(collected) == len(set(collected)) and set(collected) == set(full_specs), collected
    assert 'editor_loadScenario' not in collected
    assert all(t['name'] != 'editor_loadScenario' for t in check('loadScenario')['tools'])


def assert_search_validation():
    invalid = [{}, {'query': ''}, {'query': ' \t\r\n'}, {'query': 'x' * 257},
               *({'query': value} for value in (None, False, 1, [], {})),
               {'query': 'editor', 'extra': True}, {'query': 'editor', 'mode': 'full'},
               *({'query': 'editor', 'offset': value} for value in (-1, 2147483648, False, None, '0', 0.5)),
               *({'query': 'editor', 'limit': value} for value in (-1, 0, 51, True, None, '10', 1.5))]
    for arguments in invalid:
        # An invalid later search must stop whole batch before earlier game-backed step.
        for refused in (tool('editor_search_tools', arguments), tool('editor_batch', {'steps': [
                {'name': 'editor_status'}, {'name': 'editor_search_tools', 'arguments': arguments}]})):
            assert refused['isError'] and 'Process with an Id' not in str(refused), refused
            assert refused['structuredContent']['code'] == 'INVALID_ARGUMENT', refused
            assert refused['structuredContent']['nativeDispatched'] is False, refused
    for arguments in ({'query': 'x' * 256}, {'query': 'no_such_tool_0174babe'},
                      {'query': 'editor', 'offset': 2147483647, 'limit': 50}):
        result = tool('editor_search_tools', arguments)
        assert not result['isError'], result
        assert result['structuredContent']['tools'] == [] and result['structuredContent']['nextOffset'] is None, result
    padded = tool('editor_search_tools', {'query': ' \teditor_search_tools\n '})
    assert not padded['isError'] and padded['structuredContent']['query'] == 'editor_search_tools', padded
    total = padded['structuredContent']['total']
    for offset in (total, total + 1):
        result = tool('editor_search_tools', {'query': 'editor_search_tools', 'offset': offset, 'limit': 1})
        assert not result['isError'] and result['structuredContent']['tools'] == [], result
        assert result['structuredContent']['total'] == total and result['structuredContent']['nextOffset'] is None, result


def assert_search_isolation(full_specs, expected_core, mode):
    before = len(notifications)
    listing = request('tools/list')
    status = tool('editor_toolset')['structuredContent']
    assert status['toolset'] == mode
    assert_tool_search(full_specs, expected_core, mode)
    assert_search_validation()
    action = next(name for name in sorted(full_specs) if name.startswith('action_'))
    queries = ('editor_search_tools', 'editor_uiPlaceAtPointer', action, 'Shipped editor UI action:')
    standalone = [tool('editor_search_tools', {'query': query}) for query in queries]
    # Impossible-PID core/default hosts prove metadata-only batch never opens Game.
    batch = tool('editor_batch', {'steps': [
        {'name': 'editor_search_tools', 'arguments': {'query': query}} for query in queries]})
    assert not batch['isError'] and batch['structuredContent']['completed'] == len(queries), batch
    assert batch['structuredContent']['attempted'] == len(queries), batch
    assert not batch['structuredContent']['stoppedOnError'], batch
    for result, step in zip(standalone, batch['structuredContent']['steps']):
        assert not result['isError'] and not step['isError'], (result, step)
        assert result['structuredContent'] == step['structuredContent'], (result, step)
    helper = standalone[0]['structuredContent']['tools'][0]
    native = standalone[1]['structuredContent']['tools'][0]
    alias = standalone[2]['structuredContent']['tools'][0]
    assert helper['available'] and helper['requiredToolset'] == 'core', helper
    for hidden in (native, alias):
        assert hidden['available'] == (mode == 'full') and hidden['requiredToolset'] == 'full', hidden
    assert request('tools/list') == listing, 'Search changed tool list'
    assert tool('editor_toolset')['structuredContent'] == status, 'Search changed toolset'
    assert len(notifications) == before, 'Search emitted list_changed'


def assert_removed_loader():
    # Native reload crashed live game; confirmation/full mode must never restore this tool.
    # Impossible-PID runs prove direct and whole-batch refusals occur before any connection.
    catalog = tool('editor_catalog', {'filter': 'loadScenario'})
    assert not catalog['isError'] and catalog['structuredContent']['commands'] == [], catalog
    for arguments in ({}, {'scenarioName': 'do-not-load'},
                      {'scenarioName': 'do-not-load', 'confirmDestructive': True}):
        for refused in (tool('editor_loadScenario', arguments), tool('editor_batch', {'steps': [
                {'name': 'editor_uiClearSelection'},
                {'name': 'editor_loadScenario', 'arguments': arguments}]})):
            assert refused['isError'] and 'Native loadScenario tool removed' in str(refused), refused
            assert 'Process with an Id' not in str(refused), refused
            if refused.get('structuredContent') is not None:
                assert refused['structuredContent']['nativeDispatched'] is False, refused
    assert 'Native loadScenario tool removed' in str(tool('editor_LOADSCENARIO', {
        'scenarioName': 'do-not-load', 'confirmDestructive': True}))

try:
    init = request('initialize', {'protocolVersion': '2025-11-25', 'capabilities': {}, 'clientInfo': {'name': 'smoke', 'version': '1'}})
    assert init['protocolVersion'] == '2025-11-25'
    assert init['serverInfo']['name'] == 'aom-retold-editor'
    assert 'editor_search_tools' in init['instructions'], init
    assert init['capabilities']['tools']['listChanged'] is True
    notify('notifications/initialized')
    assert request('ping') == {}
    assert request('resources/list')['resources'] == []
    assert request('prompts/list')['prompts'] == []
    request('nonexistent_method', error_code=-32601)
    # MCP permits omitted params; same first page/cursor as explicit empty object.
    assert request('tools/list') == request('tools/list', {})
    request('tools/list', {'cursor': '-1'}, error_code=-32602)
    request('tools/call', {'name': 'editor_catalog', 'arguments': {'filter': 'x' * 2_000_000}}, error_code=-32602)
    assert request('ping') == {}  # Oversize refusal must not break transport.
    assert not request('tools/call', {'name': 'editor_catalog'})['isError']
    caps = tool('editor_capabilities')['structuredContent']
    assert caps['toolset'] == 'full' and caps['uiMetadataAvailable']
    assert r'INSTALLPATH\game\ai' in caps['aiScripts'] and 'trigger directory' in caps['aiScripts']
    tools = []
    params = {}
    while True:
        page = request('tools/list', params)
        tools.extend(page['tools'])
        if 'nextCursor' not in page:
            break
        params = {'cursor': page['nextCursor']}
    names = [t['name'] for t in tools]
    assert len(names) == len(set(names)) and all(re.fullmatch(r'[A-Za-z0-9_.-]{1,128}', n) for n in names)
    assert all(t['inputSchema']['type'] == 'object' and t['inputSchema'].get('additionalProperties') is False for t in tools)
    assert {'editor_place_unit', 'editor_uiPlaceAtPointer', 'editor_undo', 'editor_redo', 'editor_saveScenario', 'editor_uiScenarioLoad'} <= set(names)
    assert 'editor_loadScenario' not in names
    assert_removed_loader()
    assert len(names) >= 700, 'Run generator first for complete XML/hotkey surface'
    # Live import replaced whole trigger set; raw export overwrote its native staging reservation.
    # Native routes must share helper data-loss confirmation, even when callers skip editor_triggers.
    for native in ('editor_uiLoadTriggers', 'editor_uiSaveTriggers'):
        schema = next(t['inputSchema'] for t in tools if t['name'] == native)
        assert 'confirmDestructive' in schema['required']
        assert schema['properties']['confirmDestructive']['const'] is True
        for arguments in ({'filename': 'fixture'}, {'filename': 'fixture', 'confirmDestructive': False}):
            refused = tool(native, arguments)
            assert refused['isError'] and 'Process with an Id' not in str(refused), refused
        refused = tool('editor_batch', {'steps': [
            {'name': 'editor_uiClearSelection'},
            {'name': native, 'arguments': {'filename': 'fixture'}},
        ]})
        assert refused['isError'] and 'Process with an Id' not in str(refused), refused
    assert tool('nonexistent_tool')['isError']
    assert 'editor_batch' in names and 'editor_pantheon' in names and 'editor_units' in names
    assert {'editor_place_formation', 'editor_save_checkpoint'} <= set(names)
    formation = {'proto': 'Hoplite', 'shape': 'rows', 'count': 4,
                 'spacingPixels': 20, 'x': 100, 'y': 100, 'columns': 2}
    preview = tool('editor_place_formation', formation)
    assert not preview['isError'], preview
    assert preview['structuredContent']['preview'] and preview['structuredContent']['attempted'] == 0
    assert preview['structuredContent']['points'] == [
        {'x': 90, 'y': 90}, {'x': 110, 'y': 90}, {'x': 90, 'y': 110}, {'x': 110, 'y': 110}]
    ring = {k: v for k, v in formation.items() if k != 'columns'} | {'shape': 'ring', 'count': 2}
    assert tool('editor_place_formation', ring)['structuredContent']['points'] == [{'x': 110, 'y': 100}, {'x': 90, 'y': 100}]
    formation_batch = tool('editor_batch', {'steps': [
        {'name': 'editor_place_formation', 'arguments': formation},
        {'name': 'editor_place_formation', 'arguments': ring},
    ]})
    assert not formation_batch['isError'], formation_batch
    for invalid in (formation | {'count': 33}, formation | {'player': 13},
                    formation | {'spacingPixels': 0}, formation | {'x': 0},
                    formation | {'preview': False}, formation | {'columns': 5},
                    formation | {'shape': 'ring'}, formation | {'proto': ''}):
        result = tool('editor_place_formation', invalid)
        assert result['isError'] and 'Process with an Id' not in str(result), result
    with tempfile.TemporaryDirectory(prefix='aom-checkpoint-test-') as temporary:
        directory = pathlib.Path(temporary)
        profile = directory / 'scenario'
        profile.mkdir()
        existing = directory / 'existing.mythscn'
        existing.write_bytes(b'USER FILE MUST REMAIN UNCHANGED')
        for invalid in (
            {'path': str(existing), 'confirmWrite': True, 'profileDirectory': str(profile)},
            {'path': str(directory / 'new.mythscn'), 'profileDirectory': str(profile)},
            {'path': str(directory / 'new.mythscn'), 'confirmWrite': False, 'profileDirectory': str(profile)},
            {'path': 'relative.mythscn', 'confirmWrite': True},
            {'path': str(directory / 'wrong.trg'), 'confirmWrite': True, 'profileDirectory': str(profile)},
        ):
            result = tool('editor_save_checkpoint', invalid)
            assert result['isError'] and 'Process with an Id' not in str(result), result
        refused = tool('editor_batch', {'steps': [
            {'name': 'editor_uiClearSelection'},
            {'name': 'editor_save_checkpoint', 'arguments': {'path': str(existing), 'confirmWrite': True, 'profileDirectory': str(profile)}},
        ]})
        assert refused['isError'] and 'overwrite' in str(refused), refused
        assert existing.read_bytes() == b'USER FILE MUST REMAIN UNCHANGED'
        assert not (directory / 'new.mythscn').exists()
        # Forward slashes normalize before preflight; impossible PID fails only at game connection.
        slashed = tool('editor_save_checkpoint', {
            'path': str(directory / 'new.mythscn').replace('\\', '/'),
            'confirmWrite': True, 'profileDirectory': str(profile).replace('\\', '/')})
        assert slashed['isError'] and 'Process with an Id' in str(slashed), slashed
        assert not (directory / 'new.mythscn').exists()
    with tempfile.TemporaryDirectory(prefix='aom-recovery-test-') as temporary:
        directory = pathlib.Path(temporary)
        profile = directory / 'trigger'
        profile.mkdir()
        staged = profile / 'AomMcp-trigger-export-fixture.trg'
        contents = (root / 'research/BaseDefense.trg').read_bytes()
        staged.write_bytes(contents)
        args = {'operation': 'inspect', 'stagedPath': str(staged), 'profileDirectory': str(profile)}
        inspected = tool('editor_export_recovery', args)['structuredContent']
        assert inspected['fileVerified'] and inspected['sha256'] and inspected['nativeDispatched'] is False
        recovered = directory / 'recovered.trg'
        written = tool('editor_export_recovery', {**args, 'operation': 'recover',
            'outputPath': str(recovered), 'expectedSha256': inspected['sha256'], 'confirmWrite': True})
        assert not written['isError'] and recovered.read_bytes() == contents, written
        for bad in ({**args, 'operation': 'recover', 'outputPath': str(recovered),
                     'expectedSha256': inspected['sha256'], 'confirmWrite': True},
                    {**args, 'operation': 'recover', 'outputPath': str(directory / 'other.trg'),
                     'expectedSha256': '0' * 64, 'confirmWrite': True}):
            failed = tool('editor_export_recovery', bad)
            assert failed['isError'] and failed['structuredContent']['nativeDispatched'] is False, failed
        failed_batch = tool('editor_batch', {'steps': [{'name': 'editor_export_recovery', 'arguments': {
            **args, 'operation': 'recover', 'outputPath': str(directory / 'other.trg'),
            'expectedSha256': '0' * 64, 'confirmWrite': True}}]})
        assert failed_batch['isError'] and failed_batch['structuredContent']['steps'][0]['structuredContent']['code'] == 'STAGING_CHANGED'
    unit_spec = next(t for t in tools if t['name'] == 'editor_units')
    assert unit_spec['annotations']['readOnlyHint'] and not unit_spec['annotations']['destructiveHint']
    # Unit paging/player/world-area policies: reject invalid nested arguments before any Game connection.
    for invalid in ({'limit': 0}, {'limit': 201}, {'offset': -1}, {'player': 13},
                    {'proto': 4}, {'extra': True}, {'area': {'x': 0, 'z': 0}},
                    {'area': {'x': 0, 'z': 0, 'radius': -1}},
                    {'area': {'x': 0, 'z': 0, 'radius': 1, 'y': 4}}):
        result = tool('editor_units', invalid)
        assert result['isError'] and 'Process with an Id' not in str(result), result
    refused_units_batch = tool('editor_batch', {'steps': [
        {'name': 'editor_uiClearSelection'},
        {'name': 'editor_units', 'arguments': {'area': {'x': 0, 'z': 0, 'radius': -1}}},
    ]})
    assert refused_units_batch['isError'] and 'bounds' in str(refused_units_batch), refused_units_batch
    for name, invalid in (
        ('editor_inspect_selection', {'limit': 201}),
        ('editor_inspect_selection', {'offset': -1}),
        ('editor_map_info', {'world': [1, 2]}),
        ('editor_map_info', {'terrainAt': [0, 0, 0]}),
        ('editor_map_info', {'screen': [0, 'bad']}),
        ('editor_map_info', {'planeY': 4}),
    ):  # Invalid page policy, vector lengths/types and missing ray for explicit plane.
        spec = next(t for t in tools if t['name'] == name)
        assert spec['annotations']['readOnlyHint'] and not spec['annotations']['destructiveHint']
        result = tool(name, invalid)
        assert result['isError'] and 'Process with an Id' not in str(result), result
    # Trigger inspection/patching uses temp copies only, with impossible PID proving no Game connection.
    assert 'editor_triggers' in names
    original = root / 'research/BaseDefense.trg'
    listed = tool('editor_trigger_list', {'path': str(original), 'limit': 1})
    assert not listed['isError'] and listed['structuredContent']['triggers'][0]['name'] == 'Defense_Controller', listed
    trigger_id = listed['structuredContent']['triggers'][0]['id']
    detailed = tool('editor_trigger_list', {'path': str(original), 'triggerId': trigger_id})
    assert not detailed['isError'] and detailed['structuredContent']['trigger']['effects'][0]['args'], detailed
    player_snapshot = tool('editor_players', {'path': str(root / 'research/BaseDefense.mythscn'), 'player': 1})
    assert not player_snapshot['isError'] and player_snapshot['structuredContent']['players'][0]['id'] == 1, player_snapshot
    for bad in ({'path': str(original), 'limit': 201},
                {'path': str(original), 'triggerId': 2000000}):
        assert tool('editor_trigger_list', bad)['isError'], bad
    saved = player_snapshot['structuredContent']
    stance = saved['players'][0]['diplomacy'][2]['stance']
    workflow_args = {'operation': 'preview', 'scenarioPath': str(root / 'research/BaseDefense.mythscn'),
        'expectedSha256': saved['sha256'], 'player': 1, 'target': 2,
        'expectedStance': stance, 'desiredStance': 1 if stance != 1 else 2, 'direction': 'oneWay'}
    player_preview = tool('editor_set_diplomacy', workflow_args)
    assert not player_preview['isError'] and player_preview['structuredContent']['liveAutomationAvailable'] is True, player_preview
    refused_apply = tool('editor_set_diplomacy', {**workflow_args, 'operation': 'apply'})
    assert refused_apply['isError'] and refused_apply['structuredContent']['nativeDispatched'] is False, refused_apply
    unmodified = tool('editor_set_diplomacy', {**workflow_args, 'operation': 'verify',
        'verificationPath': workflow_args['scenarioPath']})
    assert not unmodified['isError'] and not unmodified['structuredContent']['verified'], unmodified
    matrix = {key: workflow_args[key] for key in ('scenarioPath', 'expectedSha256')}
    matrix.update(operation='preview', changes=[{'player': 1, 'target': 2, 'expected': stance,
        'desired': 1 if stance != 1 else 2}])
    matrix_preview = tool('editor_set_diplomacy', matrix)
    assert not matrix_preview['isError'] and matrix_preview['structuredContent']['clicks'] in (1, 2), matrix_preview
    assert tool('editor_set_diplomacy', {**matrix, 'changes': matrix['changes'] * 2})['isError']
    assert tool('editor_set_diplomacy', {**matrix, 'operation': 'apply'})['isError']  # No confirmations, no input.
    food = int(saved['players'][0]['resources']['food'])
    setting = {'operation': 'preview', 'scenarioPath': workflow_args['scenarioPath'],
        'expectedSha256': saved['sha256'], 'changes': [{'player': 1, 'field': 'food',
            'expected': str(food), 'desired': str(food + 1)}]}
    setting_preview = tool('editor_player_settings', setting)
    assert not setting_preview['isError'] and setting_preview['structuredContent']['preview'], setting_preview
    assert tool('editor_player_settings', {**setting, 'operation': 'apply'})['isError']  # No confirmations, no input.
    assert tool('editor_player_settings', {**setting, 'changes': setting['changes'] * 2})['isError']
    age_change = {'player': 1, 'field': 'startAge', 'expected': str(saved['players'][0]['startAge']),
        'desired': str((saved['players'][0]['startAge'] + 1) % 4)}
    assert tool('editor_player_settings', {**setting, 'changes': [age_change]})['isError']  # Missing minor-god assertions.
    assert tool('editor_ui_read', {'field': 'players.1.pop', 'region': [0, 0, 20, 20]})['isError']
    edit_args = {'operation': 'patch', 'path': str(original), 'triggerId': trigger_id,
                 'expectedSha256': listed['structuredContent']['sha256'], 'expectedName': 'Defense_Controller',
                 'newName': 'Defense_Controller_preview', 'active': False}
    edit_preview = tool('editor_trigger_edit', edit_args)
    assert not edit_preview['isError'] and edit_preview['structuredContent']['preview'], edit_preview
    assert edit_preview['structuredContent']['diff'] and edit_preview['structuredContent']['path'] is None
    assert tool('editor_trigger_edit', {**edit_args, 'expectedSha256': '0' * 64})['isError']
    inspected = tool('editor_triggers', {'operation': 'inspect', 'path': str(original)})
    assert not inspected['isError'], inspected
    controller = inspected['structuredContent']
    assert controller['serializationRoundTripVerified']
    assert controller['triggers'][0]['name'] == 'Defense_Controller'
    with tempfile.TemporaryDirectory(prefix='aom-trigger-test-') as temporary:
        output = pathlib.Path(temporary) / 'patched.trg'
        patched = tool('editor_triggers', {'operation': 'patch', 'path': str(original),
            'outputPath': str(output), 'name': 'fixture_unicode_α', 'active': False,
            'code': 'trChatSend(1, "fixture 😀");\n', 'confirmWrite': True})
        assert not patched['isError'], patched
        edited = patched['structuredContent']['triggers'][0]
        assert edited['name'] == 'fixture_unicode_α' and not edited['active']
        assert edited['effects'][0]['parameters']['codeSnippet'].endswith('\r\n')
        prepared = pathlib.Path(temporary) / 'prepared.trg'
        prepared_result = tool('editor_trigger_edit', {**edit_args, 'preview': False,
            'outputPath': str(prepared), 'confirmWrite': True})
        assert not prepared_result['isError'] and prepared.exists(), prepared_result
        assert tool('editor_trigger_list', {'path': str(prepared), 'triggerId': trigger_id})['structuredContent']['trigger']['name'] == 'Defense_Controller_preview'
        assert tool('editor_trigger_edit', {**edit_args, 'preview': False,
            'outputPath': str(prepared), 'confirmWrite': True})['isError']  # No overwrite.
        # Independent synthetic TR verifies schemas, offline dispatch, immutable copies, and actual new-file readback.
        object_source = pathlib.Path(temporary) / 'objects.trg'
        object_source.write_bytes(trigger_fixture())
        object_sha = hashlib.sha256(object_source.read_bytes()).hexdigest()
        tuple_old = {'unitId': 100, 'player': 1, 'proto': 'VillagerAztec'}
        object_edit = {'operation': 'patch', 'triggerId': 704, 'expectedName': 'Startup',
            'copyEffects': [{'handle': 'east', 'sourceTriggerId': 704, 'expectedSourceName': 'Startup',
                             'effectIndex': 0, 'beforeEffectIndex': 0}],
            'objectReplacements': [{'copyHandle': 'east', 'parameter': 'SrcObject', 'expected': [tuple_old],
                                    'objects': [{**tuple_old, 'unitId': 333}]}]}
        object_args = {'path': str(object_source), 'expectedSha256': object_sha, **object_edit}
        object_preview = tool('editor_trigger_edit', object_args)
        assert not object_preview['isError'] and object_preview['structuredContent']['effectCount'] == 2, object_preview
        object_output = pathlib.Path(temporary) / 'objects-copy.trg'
        assert not object_output.exists()
        object_write = tool('editor_trigger_edit', {**object_args, 'preview': False,
            'outputPath': str(object_output), 'confirmWrite': True})
        assert not object_write['isError'] and object_write['structuredContent']['sha256'] == hashlib.sha256(object_output.read_bytes()).hexdigest(), object_write
        object_detail = tool('editor_trigger_list', {'path': str(object_output), 'triggerId': 704})['structuredContent']['trigger']
        assert object_detail['effects'][0]['args'][0]['objects'][0]['unitId'] == 333
        assert object_detail['effects'][1]['args'][0]['objects'][0]['unitId'] == 100
        assert object_detail['effects'][0]['args'][0]['selectionFlag'] == 1 and object_detail['effects'][0]['args'][0]['trailer'] == 127
        assert object_detail['effects'][0]['extras'] == object_detail['effects'][1]['extras']
        wrong_owner = {**object_edit, 'objectReplacements': [{**object_edit['objectReplacements'][0],
            'expected': [{**tuple_old, 'player': 2}]}]}
        assert tool('editor_trigger_edit', {**object_args, **wrong_owner})['isError']
        assert tool('editor_trigger_edit', {**object_args, 'expectedSha256': '0' * 64})['isError']
        assert tool('editor_trigger_edit', {**object_args, 'preview': False, 'outputPath': str(object_output), 'confirmWrite': True})['isError']
        assert hashlib.sha256(object_source.read_bytes()).hexdigest() == object_sha
        xs_source = pathlib.Path(temporary) / 'fixture-source.xs'
        xs_source.write_text('// draft; not a loaded personality\n', encoding='utf-8')
        xs_staged = pathlib.Path(temporary) / 'fixture-staged.xs'
        xs_args = {'sourcePath': str(xs_source), 'expectedSha256': hashlib.sha256(xs_source.read_bytes()).hexdigest(),
            'outputPath': str(xs_staged)}
        xs_preview = tool('editor_stage_ai', xs_args)
        assert not xs_preview['isError'] and not xs_staged.exists() and not xs_preview['structuredContent']['gameResolved'], xs_preview
        xs_written = tool('editor_stage_ai', {**xs_args, 'preview': False, 'confirmWrite': True})
        assert not xs_written['isError'] and xs_staged.read_bytes() == xs_source.read_bytes(), xs_written
        assert tool('editor_stage_ai', {**xs_args, 'preview': False, 'confirmWrite': True})['isError']
        assert controller == tool('editor_triggers', {'operation': 'validate', 'path': str(original)})['structuredContent']
        # Source remains unchanged; output exists means repeated patch refuses rather than overwrites.
        assert tool('editor_triggers', {'operation': 'patch', 'path': str(original),
            'outputPath': str(output), 'name': 'no-overwrite', 'confirmWrite': True})['isError']
        malformed = pathlib.Path(temporary) / 'bad.trg'
        malformed.write_bytes(b'TR' + bytes(8))
        assert tool('editor_triggers', {'operation': 'inspect', 'path': str(malformed)})['isError']
    campaign = pathlib.Path.home() / 'Games/Age of Mythology Retold/76561198036055929/scenario/om10-betrayed'
    campaign_tr = campaign / 'player6-original.trg'
    campaign_scene = campaign / 'om10-betrayed-before-player6.mythscn'
    if campaign_tr.is_file() and campaign_scene.is_file():  # Read originals IN PLACE; no copies/commits.
        source_digest = hashlib.sha256(campaign_tr.read_bytes()).hexdigest()
        listing = tool('editor_trigger_list', {'path': str(campaign_tr), 'filter': 'Tribute', 'limit': 2})
        assert not listing['isError'] and listing['structuredContent']['triggers'][0]['id'] == 754, listing
        detail = tool('editor_trigger_list', {'path': str(campaign_tr), 'triggerId': 694})
        assert not detail['isError'] and len(detail['structuredContent']['trigger']['effects']) >= 5, detail
        audit = tool('editor_player_dependency_audit', {'scenarioPath': str(campaign_scene),
            'triggerPath': str(campaign_tr), 'player': 6, 'limit': 4})
        assert not audit['isError'], audit
        evidence = audit['structuredContent']
        assert evidence['player']['aiPath'] == '' and evidence['player']['stances'][1]['stance'] == 1
        assert any(t['id'] == 754 for t in evidence['tribute'])
        assert {int(w['players'][0]['value']) for w in evidence['waveStarts']} >= {2, 3, 4, 5}
        assert not any(any(p['value'] == '6' for p in v['players']) for v in evidence['victoryChecks'])
        assert any(a['script']['waveStrategy'] and a['script']['resolved'] for a in evidence['referenceAis'])
        cloned = tool('editor_trigger_edit', {'operation': 'clone', 'path': str(campaign_tr),
            'triggerId': 694, 'expectedName': 'E10_Prepare_Timer_Ends', 'expectedSha256': source_digest,
            'newId': 9001, 'newName': 'Agent_Offline_Preview_Only', 'active': False,
            'removeEffects': [2, 3, 4],
            'replacements': [{'kind': 'effect', 'elementIndex': 1, 'parameter': 'Player',
                'expected': '2', 'value': '6'}]})
        assert not cloned['isError'] and cloned['structuredContent']['preview'] and cloned['structuredContent']['suffixBytesPreserved'] == 216, cloned
        assert hashlib.sha256(campaign_tr.read_bytes()).hexdigest() == source_digest
    session = root / '.tmp/agent'
    if (session / 'orig-01.trg').is_file() and (session / 'edit-09.trg').is_file():
        # Read-only sources; all nine campaign edits must collapse to one byte-identical NEW output.
        def duplicate(kind, index):
            return {'kind': kind, 'elementIndex': index}
        def replacement(kind, index, key, old='5', new='6'):
            return {'kind': kind, 'elementIndex': index, 'parameter': key, 'expected': old, 'value': new}
        def patch(identifier, name, **extra):
            return {'operation': 'patch', 'triggerId': identifier, 'expectedName': name, **extra}
        changes = [
            patch(694, 'E10_Prepare_Timer_Ends', duplicates=[duplicate('effect', 4)],
                  replacements=[replacement('effect', 17, 'Player')]),
            patch(699, 'E70_Enemies_Defeated', duplicates=[duplicate('condition', 3)],
                  replacements=[replacement('condition', 4, 'PlayerID')]),
            patch(805, 'CE_Capture_East', removeEffects=[15, 20, 21, 22, 23]),
            patch(442, 'Tech_Rules', duplicates=[duplicate('effect', i) for i in (3, 22, 23, 24, 25, 26, 27, 31)],
                  replacements=[replacement('effect', 34 + i, 'PlayerID') for i in range(8)]),
            patch(465, 'No_Starting_Units', duplicates=[duplicate('effect', 4)],
                  replacements=[replacement('effect', 5, 'Player')]),
            *[patch(i, name, duplicates=[duplicate('effect', j) for j in (6, 7)],
                    replacements=[replacement('effect', 10 + k, 'PlayerID') for k in range(2)])
              for i, name in ((872, 'Give_Enemies_MediumUnits'), (873, 'Give_Enemies_HeavyUnits'))],
            patch(874, 'Give_Enemies_t1_Armory', duplicates=[duplicate('effect', i) for i in (3, 8, 14)],
                  replacements=[replacement('effect', 16 + i, 'PlayerID') for i in range(3)]),
            patch(875, 'Give_Enemies_t2_Armory', duplicates=[duplicate('effect', i) for i in (3, 8, 13)],
                  replacements=[replacement('effect', 15 + i, 'PlayerID') for i in range(3)]),
        ]
        session_source = session / 'orig-01.trg'
        args = {'path': str(session_source), 'expectedSha256': hashlib.sha256(session_source.read_bytes()).hexdigest(),
                'edits': changes}
        assert tool('editor_trigger_edit', {**args, 'edits': changes + [changes[0]]})['isError']
        with tempfile.TemporaryDirectory(prefix='aom-batch-regression-') as tmp:
            output = pathlib.Path(tmp) / 'batch.trg'
            batch = tool('editor_trigger_edit', {**args, 'preview': False,
                'outputPath': str(output), 'confirmWrite': True})
            expected = hashlib.sha256((session / 'edit-09.trg').read_bytes()).hexdigest()
            assert not batch['isError'] and hashlib.sha256(output.read_bytes()).hexdigest() == expected, batch
        parity = tool('editor_trigger_player_parity', {'path': str(session_source),
            'templatePlayer': 5, 'targetPlayer': 6, 'limit': 200})
        assert not parity['isError'] and parity['structuredContent']['total'] > 0, parity
    for invalid in (
        {'operation': 'apply', 'path': str(original)},
        {'operation': 'patch', 'path': str(original)},
        {'operation': 'inspect', 'path': str(original), 'code': 'ignored'},
        {'operation': 'export', 'path': 'relative.trg', 'confirmWrite': True},
    ):
        result = tool('editor_triggers', invalid)
        assert result['isError'] and 'Process with an Id' not in str(result), result
    refused_trigger_batch = tool('editor_batch', {'steps': [
        {'name': 'editor_uiClearSelection'},
        {'name': 'editor_triggers', 'arguments': {'operation': 'apply', 'path': str(original)}},
    ]})
    assert refused_trigger_batch['isError'] and 'confirmDestructive' in str(refused_trigger_batch)
    local_trigger_batch = tool('editor_batch', {'steps': [
        {'name': 'editor_triggers', 'arguments': {'operation': 'inspect', 'path': str(original)}},
        {'name': 'editor_catalog', 'arguments': {'filter': 'uiLoadTriggers'}},
    ]})
    assert not local_trigger_batch['isError'], local_trigger_batch
    pantheon_spec = next(t for t in tools if t['name'] == 'editor_pantheon')
    assert pantheon_spec['annotations']['readOnlyHint'] and not pantheon_spec['annotations']['destructiveHint']
    # pantheon or its alias culture; one is required at call time.
    assert pantheon_spec['inputSchema']['required'] == [] and {'pantheon', 'culture'} <= set(pantheon_spec['inputSchema']['properties'])
    greek = tool('editor_pantheon', {'pantheon': ' greeks '})
    assert not greek['isError'], greek
    roster = greek['structuredContent']
    assert roster['pantheon'] == 'Greek'
    assert {'VillagerGreek', 'Hoplite', 'Minotaur', 'TitanCerberus'} <= set(roster['units'])
    assert {'MilitaryAcademy', 'TownCenter', 'House', 'Temple', 'Sentinel'} <= set(roster['buildings'])
    assert 'Barracks' not in roster['buildings'] and 'VillagerEgyptian' not in roster['units']
    assert 'TempleChineseSPC' not in roster['buildings'], 'Do not infer culture from reused artwork'
    assert roster['units'] == sorted(set(roster['units'])) and roster['buildings'] == sorted(set(roster['buildings']))
    assert 'Zeus' in roster['majorGods'] and 'Data.bar' in roster['source'] and not roster['unresolvedTechs']
    assert tool('editor_pantheon', {'pantheon': 'GREEK'})['structuredContent'] == roster
    assert tool('editor_pantheon', {'culture': 'greeks'})['structuredContent'] == roster
    egyptian = tool('editor_pantheon', {'pantheon': 'Egyptians'})['structuredContent']
    assert 'Barracks' in egyptian['buildings'] and 'MilitaryAcademy' not in egyptian['buildings']
    assert 'VillagerEgyptian' in egyptian['units']
    for args in [{}, {'pantheon': ''}, {'pantheon': 'unknown'}, {'pantheon': 42},
                 {'pantheon': 'Greek', 'unexpected': True}]:
        assert tool('editor_pantheon', args)['isError'], args
    lookup_batch = tool('editor_batch', {'steps': [
        {'name': 'editor_pantheon', 'arguments': {'pantheon': 'Greeks'}},
        {'name': 'editor_catalog', 'arguments': {'filter': 'uiSetProtoCursor'}}]})
    assert not lookup_batch['isError'] and lookup_batch['structuredContent']['completed'] == 2, lookup_batch
    assert lookup_batch['structuredContent']['steps'][0]['structuredContent'] == roster
    game_catalogs = ['editor_prototypes', 'editor_gods', 'editor_technologies',
                     'editor_god_powers', 'editor_terrain_types', 'editor_water_types']
    assert set(game_catalogs) <= set(names)
    for catalog_name in game_catalogs:
        spec = next(t for t in tools if t['name'] == catalog_name)
        assert spec['annotations']['readOnlyHint'] and not spec['annotations']['destructiveHint']
        assert spec['inputSchema']['properties']['limit']['maximum'] == 200
        first = tool(catalog_name, {'limit': 2})
        assert not first['isError'], first
        first_page = first['structuredContent']
        second_page = tool(catalog_name, {'offset': 2, 'limit': 2})['structuredContent']
        four = tool(catalog_name, {'limit': 4})['structuredContent']
        assert first_page['total'] > 4 and first_page['nextOffset'] == 2
        assert first_page['entries'] + second_page['entries'] == four['entries']
        assert all('definition' not in e and e['source'] for e in four['entries'])
        empty = tool(catalog_name, {'name': 'AOM_NONEXISTENT_CATALOG_IDENTIFIER'})['structuredContent']
        assert empty['total'] == 0 and empty['entries'] == [] and empty['nextOffset'] is None
        past_end = tool(catalog_name, {'offset': 2147483647})['structuredContent']
        assert past_end['entries'] == [] and past_end['nextOffset'] is None
        detailed = tool(catalog_name, {'name': first_page['entries'][0]['name'],
                                     'limit': 1, 'includeDefinition': True})['structuredContent']
        assert detailed['entries'][0]['definition'].startswith('<')
        for args in [{'filter': 42}, {'offset': -1}, {'limit': 0}, {'limit': 201},
                     {'limit': 1.5}, {'includeDefinition': 'true'}, {'unexpected': True}]:
            assert tool(catalog_name, args)['isError'], (catalog_name, args)
    trees = tool('editor_prototypes', {'category': 'trees', 'name': 'treepine'})['structuredContent']
    assert trees['total'] == 1 and trees['entries'][0]['name'] == 'TreePine'
    assert 'Tree' in trees['entries'][0]['unitTypes'] and 'resources' in trees['entries'][0]['categories']
    assert trees['entries'][0]['initialResources'][0] == {'resource': 'Wood', 'amount': '200.0100'}
    mine = tool('editor_prototypes', {'category': 'resources', 'unitType': 'GoldResource',
                                    'name': 'MineGoldSmall'})['structuredContent']
    assert mine['total'] == 1 and mine['entries'][0]['initialResources'][0]['resource'] == 'Gold'
    assert tool('editor_prototypes', {'category': 'trees', 'name': 'MilitaryAcademy'})['structuredContent']['total'] == 0
    academy = tool('editor_prototypes', {'category': 'buildings', 'pantheon': 'greeks',
                                       'name': 'MilitaryAcademy'})['structuredContent']
    assert academy['total'] == 1 and academy['entries'][0]['costs']
    villager = tool('editor_prototypes', {'category': 'units', 'name': 'VillagerGreek'})['structuredContent']
    assert villager['entries'][0]['stats']['maxhitpoints'] == ['55.0000']
    assert {'editor_dependencies', 'editor_validate_scenario'} <= set(names)
    dependencies = tool('editor_dependencies', {'proto': 'Hoplite', 'limit': 200})
    assert not dependencies['isError'], dependencies
    entries = dependencies['structuredContent']['entries']
    assert any(e['kind'] == 'prototype' and e['name'] == 'MilitaryAcademy' and e['relationship'] == 'train' for e in entries)
    minotaur = tool('editor_dependencies', {'proto': 'minotaur', 'limit': 200})
    assert not minotaur['isError'], minotaur
    entries = minotaur['structuredContent']['entries']
    assert any(e['kind'] == 'technology' and e['name'] == 'ClassicalAgeAthena' and e['prerequisites'] for e in entries)
    assert any(e['kind'] == 'god' and e['name'] == 'Zeus' and e.get('path') == ['ArchaicAgeZeus', 'ClassicalAgeAthena'] for e in entries)
    first = tool('editor_dependencies', {'proto': 'Hoplite', 'limit': 2})['structuredContent']
    second = tool('editor_dependencies', {'proto': 'Hoplite', 'limit': 2, 'offset': 2})['structuredContent']
    four = tool('editor_dependencies', {'proto': 'Hoplite', 'limit': 4})['structuredContent']
    assert first['entries'] + second['entries'] == four['entries']
    assert tool('editor_dependencies', {'proto': 'Hoplite', 'offset': 2147483647})['structuredContent']['entries'] == []
    for invalid in ({}, {'proto': 'unknown'}, {'proto': 4}, {'proto': 'Hoplite', 'limit': 201}):
        assert tool('editor_dependencies', invalid)['isError'], invalid
    dependency_batch = tool('editor_batch', {'steps': [
        {'name': 'editor_dependencies', 'arguments': {'proto': 'Hoplite', 'limit': 1}},
        {'name': 'editor_dependencies', 'arguments': {'proto': 'Minotaur', 'limit': 1}},
    ]})
    assert not dependency_batch['isError'], dependency_batch
    for invalid in ({'requiredUnitIds': [-1]}, {'requiredUnitIds': ['0']},
                    {'requireTownCenterPlayers': [13]}, {'limit': 201}, {'triggerPath': 'relative.trg'}):
        result = tool('editor_validate_scenario', invalid)
        assert result['isError'] and 'Process with an Id' not in str(result), result
    objects = tool('editor_prototypes', {'category': 'objects', 'limit': 1})['structuredContent']
    assert objects['total'] > 100
    zeus = tool('editor_gods', {'category': 'major', 'pantheon': 'Greek', 'name': 'Zeus'})['structuredContent']
    assert zeus['total'] == 1 and zeus['entries'][0]['startingUnits']
    athena = tool('editor_gods', {'category': 'minor', 'filter': 'Athena'})['structuredContent']
    assert athena['total'] == 1 and athena['entries'][0]['name'] == 'ClassicalAgeAthena'
    assert athena['entries'][0]['pantheons'] == ['Greek'] and athena['entries'][0]['directTechEffects']
    assert 'Restoration' in athena['entries'][0]['godPowers'] and not athena['entries'][0]['unresolvedTechs']
    tech = tool('editor_technologies', {'name': 'ClassicalAgeAthena'})['structuredContent']['entries'][0]
    assert tech['costs'] and tech['prerequisites'] and tech['effects']
    bolt = tool('editor_god_powers', {'name': 'Bolt', 'pantheon': 'Greeks'})['structuredContent']['entries'][0]
    assert bolt['powerType'] == 'Bolt' and 'ArchaicAgeZeus' in bolt['grantedByTechs']
    assert 'Zeus' in bolt['grantedByGods']
    terrain = tool('editor_terrain_types', {'name': 'default\\default', 'terrainType': 'PassableLand'})['structuredContent']
    assert terrain['total'] == 1 and terrain['entries'][0]['settings']['texture_scale'] == '20.0000'
    assert terrain['entries'][0]['label'] == 'Default'
    water = tool('editor_water_types', {'category': 'ocean', 'name': 'AztecAztlanSea'})['structuredContent']
    assert water['total'] == 1 and water['entries'][0]['settings']['surface'] == 'water'
    assert tool('editor_water_types', {'category': 'river', 'name': 'AztecAztlanSea'})['structuredContent']['total'] == 0
    for catalog_name in game_catalogs[:4]:
        assert tool(catalog_name, {'pantheon': 'unknown'})['isError']
    for args in [{'category': 'bad'}, {'unitType': 42}, {'pantheon': ''}]:
        assert tool('editor_prototypes', args)['isError'], args
    all_catalogs = tool('editor_batch', {'steps': [
        {'name': name, 'arguments': {'limit': 1}} for name in game_catalogs]})
    assert not all_catalogs['isError'] and all_catalogs['structuredContent']['completed'] == 6, all_catalogs
    preflight_bounds = tool('editor_batch', {'steps': [
        {'name': 'editor_uiClearSelection'},
        {'name': 'editor_prototypes', 'arguments': {'limit': 201}}]})
    assert preflight_bounds['isError'] and 'bounds' in preflight_bounds['content'][0]['text'], preflight_bounds
    refused_lookup = tool('editor_batch', {'steps': [
        {'name': 'editor_pantheon', 'arguments': {'pantheon': 42}}]})
    assert refused_lookup['isError'], refused_lookup
    batch = tool('editor_batch', {'steps': [
        {'name': 'editor_catalog', 'arguments': {'filter': 'uiClearCursor'}},
        {'name': 'editor_catalog', 'arguments': {'filter': 'uiSetProtoCursor'}}]})
    assert not batch['isError'] and batch['structuredContent']['completed'] == 2, batch
    assert not batch['structuredContent']['atomic']
    for steps in [[], [{'name': 'editor_batch'}], [{'name': 'nonexistent_tool'}],
                  [{'name': 'editor_catalog', 'delayMs': 2001}],
                  [{'name': 'editor_catalog', 'arguments': {'filter': 42}}],
                  [{'name': 'editor_catalog'}] * 33]:
        assert tool('editor_batch', {'steps': steps})['isError'], steps
    # Entire schema preflight occurs before connecting/sending the first command.
    refused = tool('editor_batch', {'steps': [
        {'name': 'editor_uiClearSelection'},
        {'name': 'editor_saveScenario', 'arguments': {'fname': 'do-not-save'}}]})
    assert refused['isError'] and 'confirmDestructive' in refused['content'][0]['text'], refused
    # Pipelined catalog-only batches must finish sequentially, without any game connection.
    pending = set()
    started = time.monotonic()
    for _ in range(3):
        pending.add(send('tools/call', {'name': 'editor_batch', 'arguments': {'steps': [
            {'name': 'editor_catalog', 'arguments': {'filter': 'uiClearCursor'}, 'delayMs': 100}]}}))
    while pending:
        message = receive()
        assert message['id'] in pending and not message['result']['isError'], message
        pending.remove(message['id'])
        assert message['result']['structuredContent']['completed'] == 1, message
    assert time.monotonic() - started >= 0.25, 'Tool calls overlapped'
    # Cancellation drops a queued call without breaking the running batch or subsequent calls.
    running = send('tools/call', {'name': 'editor_batch', 'arguments': {'steps': [
        {'name': 'editor_catalog', 'arguments': {'filter': 'uiClearCursor'}, 'delayMs': 1000}]}})
    assert request('ping') == {}  # Protocol requests stay responsive while a tool is running.
    time.sleep(0.1)
    cancelled = send('tools/call', {'name': 'editor_catalog', 'arguments': {'filter': 'uiClearCursor'}})
    notify('notifications/cancelled', {'requestId': cancelled, 'reason': 'smoke check'})
    following = send('tools/call', {'name': 'editor_catalog', 'arguments': {'filter': 'uiClearCursor'}})
    pending = {running, following}
    while pending:
        message = receive()
        assert message['id'] in pending and not message['result']['isError'], message
        pending.remove(message['id'])
    assert request('ping') == {}
    full_catalog = tool('editor_catalog')['structuredContent']
    assert full_catalog['toolset'] == 'full' and full_catalog['exposedToolCount'] == len(names)
    native_names = {'editor_' + command['name'] for command in full_catalog['commands']}
    helper_names = {name for name in names if name not in native_names and not name.startswith('action_')}
    # Core stays unchanged; new workflow helpers are full-only.
    assert workflow_names <= helper_names
    assert_workflow_tools()
    core_native = {'editor_undo', 'editor_redo', 'editor_uiClearSelection', 'editor_uiSelectType',
                   'editor_uiLookAtAndSelectUnit', 'editor_uiSetCameraStartLoc', 'editor_saveScenario',
                   'editor_uiLoadTriggers', 'editor_uiSaveTriggers'}
    expected_core = (helper_names - workflow_names) | core_native
    assert len(expected_core) == 72 and len(names) == 922
    world_tools = {'editor_terrain_info', 'editor_live_players', 'editor_edit_mode', 'editor_paint_world', 'editor_elevation',
                   'editor_transform_unit', 'editor_terrain_catalog', 'editor_camera_frame', 'editor_overview',
                   'editor_resource_balance', 'editor_mirror_units', 'editor_scatter'}
    assert world_tools <= expected_core, world_tools - expected_core
    scene_tools = {'editor_camera_look_at', 'editor_view_info', 'editor_ui_state', 'editor_place_at_world',
                   'editor_units_snapshot', 'editor_units_diff', 'editor_scene_summary', 'editor_delete_units',
                   'editor_check_footprints', 'editor_terrain_grid', 'editor_apply_layout'}
    assert scene_tools <= expected_core, scene_tools - expected_core
    # Scene helper guards refuse in preflight, before any game connection (impossible PID here).
    def preflight_refusal(name, args, code):
        response = tool(name, args)
        content = response['structuredContent']
        assert response['isError'] and content['code'] == code and content['nativeDispatched'] is False \
            and content['retrySafe'], (name, response)
    preflight_refusal('editor_delete_units', {'units': [{'unitId': 1, 'proto': 'Hoplite', 'player': 1}]}, 'INVALID_ARGUMENT')
    preflight_refusal('editor_delete_units', {'units': [{'unitId': 1, 'proto': 'Hoplite', 'player': 1}] * 2,
                                              'confirmDestructive': True}, 'INVALID_ARGUMENT')
    preflight_refusal('editor_apply_layout', {}, 'INVALID_ARGUMENT')
    # World helpers refuse destructive strokes without confirmation, before any game connection.
    preflight_refusal('editor_paint_world', {'kind': 'water', 'type': 'GreekLake', 'points': [[1, 1]]}, 'INVALID_ARGUMENT')
    preflight_refusal('editor_elevation', {'operation': 'set', 'minX': 0, 'minZ': 0, 'maxX': 10, 'maxZ': 10,
                                           'confirmDestructive': True}, 'INVALID_ARGUMENT')
    preflight_refusal('editor_transform_unit', {'unitId': 1, 'proto': 'House', 'player': 1}, 'INVALID_ARGUMENT')
    preflight_refusal('editor_apply_layout', {'items': [{'proto': 'Hoplite', 'x': 1, 'z': 1}], 'preview': False}, 'INVALID_ARGUMENT')
    preflight_refusal('editor_apply_layout', {'formation': {'proto': 'Hoplite', 'shape': 'ring', 'count': 3, 'spacing': 2,
                                                            'x': 5, 'z': 5, 'columns': 2}}, 'INVALID_ARGUMENT')
    preflight_refusal('editor_terrain_grid', {'minX': 5, 'minZ': 0, 'maxX': 5, 'maxZ': 9}, 'INVALID_ARGUMENT')
    preflight_refusal('editor_terrain_grid', {'minX': 0, 'minZ': 0, 'maxX': 5, 'maxZ': 9, 'flatSize': 4}, 'INVALID_ARGUMENT')
    preflight_refusal('editor_place_at_world', {'proto': 'Hopilte', 'x': 5, 'z': 5}, 'UNKNOWN_PROTO')
    assert 'Hoplite' in tool('editor_place_at_world', {'proto': 'Hopilte', 'x': 5, 'z': 5})['structuredContent']['message']
    preflight_refusal('editor_apply_layout', {'items': [{'proto': 'TownCentre', 'x': 5, 'z': 5}]}, 'UNKNOWN_PROTO')
    for name in ('editor_view_info', 'editor_ui_state', 'editor_scene_summary', 'editor_units_snapshot',
                 'editor_check_footprints', 'editor_terrain_grid', 'editor_units_diff'):
        assert full_specs_annotations(tools)[name] is True, name
    for name in ('editor_camera_look_at', 'editor_place_at_world', 'editor_delete_units', 'editor_apply_layout'):
        assert full_specs_annotations(tools)[name] is False, name
    full_specs = {t['name']: t for t in tools}
    assert 'editor_search_tools' in expected_core
    assert_search_isolation(full_specs, expected_core, 'full')
    outer = p
    for selection in ([], ['--toolset', 'core']):
        p = subprocess.Popen(['dotnet', str(app), '--pid', '2147483647'] + selection,
                             stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                             text=True, cwd=root, creationflags=subprocess.CREATE_NO_WINDOW)
        try:
            initialized = request('initialize', {'protocolVersion': '2025-11-25', 'capabilities': {},
                                                'clientInfo': {'name': 'core-smoke', 'version': '1'}})
            assert 'Initial tool set: core' in initialized['instructions'], initialized
            assert 'editor_search_tools' in initialized['instructions'], initialized
            assert initialized['capabilities']['tools']['listChanged'] is True
            notify('notifications/initialized')
            page = request('tools/list')
            assert page == request('tools/list', {})
            core_tools = page['tools']
            assert 'nextCursor' not in page and {t['name'] for t in core_tools} == expected_core, page
            assert all(t == full_specs[t['name']] for t in core_tools)
            assert_search_isolation(full_specs, expected_core, 'core')
            catalog = tool('editor_catalog')['structuredContent']
            assert catalog['toolset'] == 'core' and catalog['exposedToolCount'] == len(expected_core)
            assert {'editor_' + c['name'] for c in catalog['commands']} == core_native
            assert catalog['menuActionCount'] == 0
            assert_removed_loader()
            assert tool('editor_catalog', {'filter': 'uiPlaceAtPointer'})['structuredContent']['commands'] == []
            preview = tool('editor_place_formation', formation)
            assert not preview['isError'] and preview['structuredContent']['attempted'] == 0, preview
            for hidden in (*sorted(workflow_names), 'editor_uiPlaceAtPointer', next(n for n in names if n.startswith('action_'))):
                for refused in (tool(hidden), tool('editor_batch', {'steps': [
                        {'name': 'editor_search_tools', 'arguments': {'query': hidden}},
                        {'name': 'editor_status'}, {'name': hidden}]})):
                    assert refused['isError'] and 'unexposed tool' in str(refused), refused
                    assert '--toolset full' in str(refused) and 'Process with an Id' not in str(refused), refused
            refused = tool('editor_saveScenario', {'fname': 'do-not-save'})
            assert refused['isError'] and 'confirmDestructive' in str(refused), refused
            before = len(notifications)
            status = tool('editor_toolset')['structuredContent']
            assert status == {'toolset': 'core', 'exposedToolCount': len(expected_core), 'changed': False}
            for invalid in ({'mode': 'unknown'}, {'mode': False}, {'mode': 'FULL'}, {'mode': 'full', 'extra': True}):
                assert tool('editor_toolset', invalid)['isError'], invalid
            assert not tool('editor_toolset', {'mode': 'core'})['structuredContent']['changed']
            assert len(notifications) == before  # Invalid/no-op changes must not announce new list.
            for mode in ('full', 'core'):
                changed = tool('editor_toolset', {'mode': mode})['structuredContent']
                assert changed['changed'] and changed['toolset'] == mode
                before += 1
                assert len(notifications) == before  # One notification, before change response.
                refreshed = []
                params = {}
                while True:
                    page = request('tools/list', params)
                    refreshed.extend(page['tools'])
                    if 'nextCursor' not in page:
                        break
                    params = {'cursor': page['nextCursor']}
                assert refreshed == (tools if mode == 'full' else core_tools)
                assert changed['exposedToolCount'] == len(refreshed)
                assert tool('editor_catalog')['structuredContent']['toolset'] == mode
                assert 'editor_loadScenario' not in {t['name'] for t in refreshed}
                assert_search_isolation(full_specs, expected_core, mode)
                assert_removed_loader()
                standalone = tool('editor_batch', {'steps': [
                    {'name': 'editor_status'}, {'name': 'editor_toolset', 'arguments': {'mode': mode}}]})
                assert standalone['isError'] and 'standalone' in str(standalone) and 'Process with an Id' not in str(standalone), standalone
                assert not tool('editor_toolset', {'mode': mode})['structuredContent']['changed']
                assert len(notifications) == before
            assert tool('editor_uiPlaceAtPointer')['isError']
            request('tools/list', {'cursor': '100'}, error_code=-32602)  # Old full-mode cursor exceeds core snapshot.
        finally:
            assert p.stdin is not None and p.stderr is not None
            p.stdin.close()
            p.wait(timeout=10)
            assert p.returncode == 0, p.stderr.read()
            p = outer
    for invalid in ('unknown', 'FULL', ''):
        rejected = subprocess.run(['dotnet', str(app), '--toolset', invalid, '--self-test-local'],
                                  cwd=root, capture_output=True, text=True)
        assert rejected.returncode != 0 and '--toolset must be core or full.' in rejected.stderr, rejected
    if not options.live:
        # Separate deployment proves packaged metadata works; corrupt only temporary copies.
        outer = p
        with tempfile.TemporaryDirectory(prefix='aom-catalog-') as temporary:
            deploy = pathlib.Path(temporary) / 'deploy'
            shutil.copytree(app.parent, deploy)
            metadata = deploy / 'game_catalog.json'
            original = json.loads(metadata.read_text())
            for condition in ['fresh', 'touched_exe', 'wrong_hash', 'changed_exe', 'changed_archive', 'missing']:
                altered = dict(original)
                if condition == 'touched_exe':
                    # Same hash/length, only timestamp differs (Steam validation touch): still fresh.
                    altered['exeWriteTimeUtc'] = '1900-01-01T00:00:00Z'
                if condition == 'wrong_hash':
                    altered['exeSha256'] = '0' * 64  # Deliberately wrong SHA-256 fixture.
                if condition == 'changed_exe':
                    altered['exeLength'] = -1  # Impossible size fixture, no executable writes.
                if condition == 'changed_archive':
                    altered['archiveLength'] = -1  # Impossible size; do not touch real Data.bar.
                if condition == 'missing':
                    metadata.unlink()
                else:
                    metadata.write_text(json.dumps(altered))
                p = subprocess.Popen(['dotnet', str(deploy / app.name), '--pid', '2147483647'],
                                     stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                     text=True, cwd=root, creationflags=subprocess.CREATE_NO_WINDOW)
                try:
                    request('initialize', {'protocolVersion': '2025-11-25', 'capabilities': {},
                                          'clientInfo': {'name': 'metadata-smoke', 'version': '1'}})
                    notify('notifications/initialized')
                    lookup = tool('editor_pantheon', {'pantheon': 'Greeks'})
                    if condition in ('fresh', 'touched_exe'):
                        assert not lookup['isError'] and lookup['structuredContent'] == roster, lookup
                    else:
                        assert lookup['isError'] and '--generate' in lookup['content'][0]['text'], lookup
                        assert lookup['structuredContent']['code'] in ('METADATA_STALE', 'FILE_NOT_FOUND'), lookup
                        assert lookup['structuredContent']['retrySafe'] and lookup['structuredContent']['nativeDispatched'] is False, lookup
                    for catalog_name in game_catalogs:
                        lookup = tool(catalog_name, {'limit': 1})
                        if condition in ('fresh', 'touched_exe'):
                            assert not lookup['isError'] and lookup['structuredContent']['entries'], lookup
                        else:
                            assert lookup['isError'] and '--generate' in lookup['content'][0]['text'], lookup
                finally:
                    assert p.stdin is not None and p.stderr is not None
                    p.stdin.close()
                    p.wait(timeout=10)
                    assert p.returncode == 0, p.stderr.read()
                    p = outer
    if options.live:
        state = tool('editor_status')
        assert state['structuredContent']['connected'] and state['structuredContent']['editor'], state
        assert not tool('editor_uiClearSelection')['isError']
        assert tool('editor_uiSetProtoCursor', {'proto': 'invalid', 'setPlacement': 'wrong-type'})['isError']
        assert_removed_loader()
        assert tool('editor_key', {'key': 'F4', 'modifiers': ['ALT']})['isError']
        refused = tool('editor_place_unit', {'proto': 'AOM_MCP_NONEXISTENT_PROTO', 'player': 1, 'x': 1280, 'y': 720})
        assert refused['isError'] and 'no placement requested' in refused['content'][0]['text'], refused
        assert tool('editor_status')['structuredContent']['placementProtoId'] == -1
        stopped = tool('editor_batch', {'steps': [
            {'name': 'editor_uiClearCursor'},
            {'name': 'editor_mouse_move', 'arguments': {'x': -1, 'y': 0}},
            {'name': 'editor_catalog'}]})
        assert stopped['isError'] and stopped['structuredContent']['stoppedOnError'], stopped
        assert stopped['structuredContent']['attempted'] == 2 and stopped['structuredContent']['completed'] == 1
        image = tool('editor_batch', {'steps': [
            {'name': 'editor_status'},
            {'name': 'editor_screenshot', 'arguments': {'maxWidth': 640}, 'delayMs': 50}]})
        assert not image['isError'] and image['structuredContent']['completed'] == 2, image
        assert image['structuredContent']['steps'][0]['structuredContent']['editor']
        png = base64.b64decode(next(c['data'] for c in image['content'] if c['type'] == 'image'))
        assert png[:8] == b'\x89PNG\r\n\x1a\n'
        at = 8
        compressed = b''
        width = height = 0
        while at < len(png):
            size = struct.unpack_from('>I', png, at)[0]
            kind = png[at+4:at+8]
            data = png[at+8:at+8+size]
            crc = struct.unpack_from('>I', png, at+8+size)[0]
            assert zlib.crc32(kind + data) == crc
            if kind == b'IHDR':
                width, height = struct.unpack_from('>II', data)
            if kind == b'IDAT':
                compressed += data
            at += size + 12
        assert width == 640 and height > 0 and len(zlib.decompress(compressed)) == height * (width * 3 + 1)
        (root / 'research/mcp-screenshot.png').write_bytes(png)
    print(f'PASS: full={len(names)}, core={len(expected_core)} (default/explicit), paging/schema/guards/tool-search isolation/hidden-call refusals/live-switch notifications; live={options.live}')
finally:
    assert p.stdin is not None and p.stderr is not None
    p.stdin.close()
    p.wait(timeout=10)
    error = p.stderr.read()
    assert p.returncode == 0, error
