"""Passive unit-layout research. Query/read process access only; never calls game functions.
Requires existing temporary capstone installation for disassembly; no production dependency.
"""
import argparse
import ctypes as C
import hashlib
import importlib
import json
import pathlib
import struct
import sys
from ctypes import wintypes as W

ROOT = pathlib.Path(__file__).resolve().parents[1]
EXE = pathlib.Path(r'C:\Program Files (x86)\Steam\steamapps\common\Age of Mythology Retold\AoMRT_s.exe')
# Existing research-only dependency location from earlier passive probes.
sys.path.insert(0, str(pathlib.Path.home() / 'AppData/Local/Temp/aom-analysis-python'))
capstone = importlib.import_module('capstone')

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--pid', type=int, required=True)
parser.add_argument('--rva', type=lambda s: int(s, 0), action='append', default=[])
parser.add_argument('--name', action='append', default=[])
parser.add_argument('--registry', action='store_true', help='Read independently recovered context/object table candidates')
parser.add_argument('--core-registry', action='store_true', help='Read editor world table from uiLookAtAndSelectUnit')
parser.add_argument('--field', type=lambda s: int(s, 0), action='append', default=[])
parser.add_argument('--string', action='append', default=[])
parser.add_argument('--capture-layout', action='store_true')
parser.add_argument('--map-registry', action='store_true')
parser.add_argument('--capture-view-layout', action='store_true')
args = parser.parse_args()
assert C.sizeof(C.c_void_p) == 8, 'Use x64 Python'
k = C.WinDLL('kernel32', use_last_error=True)
p = C.WinDLL('psapi', use_last_error=True)
k.OpenProcess.argtypes = [W.DWORD, W.BOOL, W.DWORD]
k.OpenProcess.restype = W.HANDLE
k.CloseHandle.argtypes = [W.HANDLE]
k.QueryFullProcessImageNameW.argtypes = [W.HANDLE, W.DWORD, W.LPWSTR, C.POINTER(W.DWORD)]
k.ReadProcessMemory.argtypes = [W.HANDLE, C.c_void_p, C.c_void_p, C.c_size_t, C.POINTER(C.c_size_t)]
p.EnumProcessModules.argtypes = [W.HANDLE, C.POINTER(C.c_void_p), W.DWORD, C.POINTER(W.DWORD)]
handle = k.OpenProcess(0x410, False, args.pid)  # SDK QUERY_INFORMATION | VM_READ; no write/debug rights.
if not handle:
    raise OSError(C.get_last_error(), 'OpenProcess')
try:
    path = C.create_unicode_buffer(32768)  # Windows extended path ceiling, host buffer.
    length = W.DWORD(len(path))
    assert k.QueryFullProcessImageNameW(handle, 0, path, C.byref(length))
    assert pathlib.Path(path.value).resolve() == EXE.resolve(), 'Wrong executable'
    disk = EXE.read_bytes()
    digest = hashlib.sha256(disk).hexdigest()
    # This research script's candidate offsets were recovered only for this accepted build.
    assert digest == 'dd15d1d838e78faa1bc9854becc3994f4f3a4548ef30efd24108abedc1b84fff', 'Unreviewed probe build'
    layout = json.loads((ROOT / 'layouts' / (digest + '.json')).read_text())
    modules = (C.c_void_p * 1024)()  # Host module-list capacity, not number of game DLLs.
    needed = W.DWORD()
    assert p.EnumProcessModules(handle, modules, C.sizeof(modules), C.byref(needed))
    base = modules[0]
    def read(address, size):
        assert 0 <= size <= 100_000_000  # Same host read ceiling as Game.Read.
        buffer = C.create_string_buffer(size)
        actual = C.c_size_t()
        if not k.ReadProcessMemory(handle, address, buffer, size, C.byref(actual)) or actual.value != size:
            raise OSError(C.get_last_error(), f'ReadProcessMemory {address:#x}+{size:#x}')
        return buffer.raw
    def pointer(address):
        return struct.unpack('<Q', read(address, 8))[0]  # AMD64 pointer is eight little-endian bytes.
    prefix = bytes.fromhex(layout['prefixHex'])
    assert read(base + layout['dispatcherRva'], len(prefix)) == prefix, 'Wrong runtime signature'
    game = pointer(base + layout['editorGlobalRva'])
    assert game and read(game + layout['editorFlagOffset'], 1) == b'\x01', 'Open scenario editor; no input sent'
    print(f'PASSIVE pid={args.pid} base={base:#x} hash={digest}; editor=true')
    # Reviewed prototype fields/counts in LIVE-UNITS.md: context+4e0, count1448, array1458; DWORD is four bytes.
    proto_root = pointer(pointer(base + layout['contextRva']) + 0x4e0)
    proto_count = struct.unpack('<i', read(proto_root + 0x1448, 4))[0]
    proto_array = pointer(proto_root + 0x1458)
    # PE/COFF specification: DOS e_lfanew+3c; signature/COFF header24 bytes; section-count+6,
    # optional-header-size+20. Section headers40 bytes, name8, size/RVA/raw fields begin+8.
    pe = struct.unpack_from('<I', disk, 0x3c)[0]
    optional = pe + 24
    count = struct.unpack_from('<H', disk, pe + 6)[0]
    table = optional + struct.unpack_from('<H', disk, pe + 20)[0]
    sections = []
    for i in range(count):
        at = table + 40 * i  # IMAGE_SECTION_HEADER, PE/COFF.
        name = disk[at:at+8].rstrip(b'\0').decode('ascii')
        size, rva, rawsize, offset = struct.unpack_from('<4I', disk, at+8)
        sections.append((name, size, rva, rawsize, offset))
    _, size, text_rva, _, _ = next(s for s in sections if s[0] == '.text')
    text = read(base + text_rva, size)
    def code(rva, size=512):  # Host default disassembly byte window, not native function size.
        assert text_rva <= rva <= text_rva + len(text) - size
        return text[rva-text_rva:rva-text_rva+size]
    def targets(name):
        locations = set()
        needle = name.encode('ascii') + b'\0'
        for section, _, rva, rawsize, offset in sections:
            if section == '.text':
                continue
            data = disk[offset:offset+rawsize]
            pos = data.find(needle)
            while pos >= 0:
                if pos == 0 or data[pos-1] == 0:
                    locations.add(rva + pos)
                pos = data.find(needle, pos + len(needle))
        found = set()
        # AMD64 RIP-relative LEA: REX+opcode+ModRM (three bytes), disp32 at+3, seven-byte instruction.
        # Observed registration RDX=name, R8=target. REX48/4c; LEA8d; MOV8b/89; ModRM84/44
        # distinguishes disp32/disp8 stack operands, displacement at+4. Host search windows64/4096 bytes.
        pos = text.find(b'\x48\x8d\x15')  # Observed registration RDX=name, R8=target.
        while pos >= 0:
            dest = text_rva + pos + 7 + struct.unpack_from('<i', text, pos+3)[0]
            if dest in locations:
                before = [j for j in range(max(0, pos-64), pos)
                          if text[j:j+3] == b'\x4c\x8d\x05' or text[j:j+4] in [b'\x4c\x8b\x84\x24', b'\x4c\x8b\x44\x24']]
                if before:
                    j = before[-1]
                    if text[j+1] == 0x8d:
                        found.add(text_rva + j + 7 + struct.unpack_from('<i', text, j+3)[0])
                    else:
                        slot = struct.unpack_from('<i', text, j+4)[0] if text[j+2] == 0x84 else text[j+4]
                        for s in range(max(7, pos-4096), j-7):
                            if text[s:s+4] not in [b'\x48\x89\x84\x24', b'\x48\x89\x44\x24']:
                                continue
                            stored = struct.unpack_from('<i', text, s+4)[0] if text[s+2] == 0x84 else text[s+4]
                            if slot == stored and text[s-7:s-4] == b'\x48\x8d\x05':
                                found.add(text_rva + s + struct.unpack_from('<i', text, s-4)[0])
            pos = text.find(b'\x48\x8d\x15', pos+3)
        return found
    disassembler = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    if args.capture_layout:
        # Every field/RVA and signature window length has provenance in LIVE-UNITS.md.
        # Lengths pin specific reviewed instructions, not complete native function boundaries.
        candidate = {
            'worldGlobalRva': 0x53e5048, 'worldPointerOffset': 8, 'capacityOffset': 0x188,
            'arrayOffset': 0x198, 'indexMask': 0x3ffff, 'idOffset': 0x1d8, 'protoOffset': 0x1dc,
            'playerOffset': 0x208, 'positionOffsets': [0x5c,0x6c,0x7c], 'healthOffset': 0x23c,
            'maxHealthOffset': 0x248, 'protoRootOffset': 0x4e0, 'protoCapacityOffset': 0x1448,
            'protoArrayOffset': 0x1458, 'protoNameOffset': 0x10,
            'signatures': [{'rva': rva, 'hex': code(rva,size).hex()} for rva,size in
                           [(0x621580,229),(0x448750,61),(0x265135,89),(0x1c64740,48),(0x2576e0,60)]],
        }
        (ROOT/'research/units-layout-candidate.json').write_text(json.dumps(candidate,indent=2))
        print('Captured reviewed-build candidate signatures; no automatic layout activation.')
    if args.capture_view_layout:
        # Recovered native offsets for this hash only; numeric source tables in LIVE-VIEW.md.
        selection = {
            'player': 1, 'slotsOffset': 0x6c0, 'slotStride': 16, 'groupOffset': 0x10,
            'countOffset': 8, 'arrayOffset': 0x18, 'recordStride': 16, 'kindOffset': 8, 'idOffset': 12,
            'signatures': [{'rva': rva, 'hex': code(rva,size).hex()} for rva,size in
                           [(0x6214f0,137),(0x1b1080,16),(0x1b10f0,16),(0x4b5932,126)]],
        }
        map_layout = {
            'worldOffset': 8, 'terrainOffset': 0x260, 'tileOffsets': [0xc,0x10],
            'vertexOffsets': [0x14,0x18], 'scaleOffset': 0x1c, 'inverseScaleOffset': 0x20,
            'gridOffset': 0x78, 'gridCountOffset': 0, 'gridDataOffset': 0x10, 'gridStrideOffset': 0x2c,
            'cameraOffset': 0xd8, 'poseOffsets': [0,0x10,0x20,0x30], 'fovOffset': 0xb0,
            'rendererGlobalRva': 0x5429b30, 'viewportOffsets': [0x2418,0x241c,0x2420,0x2424],
            'projectionOffset': 0x1340,
            'signatures': [{'rva': rva, 'hex': code(rva,size).hex()} for rva,size in
                           [(0x1bfbca0,88),(0x1bfbd00,88),(0x207a162,64),(0x3d5d70,33),
                            (0x5f8213,38),(0x163d650,126),(0x163c694,51),(0x19ec80,82)]],
        }
        (ROOT/'research/view-layout-candidate.json').write_text(json.dumps({'selection':selection,'map':map_layout},indent=2))
        print('Captured reviewed-build view fields; manual activation only.')
    if args.core_registry:
        # uiLookAtAndSelectUnit (0x621580): world global RIP load, +8, slot table +0x198/count +0x188.
        root = pointer(base + 0x53e5048)
        world = pointer(root + 8)
        capacity = struct.unpack('<I', read(world + 0x188, 4))[0]
        objects = pointer(world + 0x198)
        print('CORE REGISTRY', hex(world), hex(objects), capacity)
        assert 0 <= capacity <= 0x40000 and (objects or capacity == 0)  # Recovered 18-bit mask permits 262144 slots.
        slots = struct.unpack('<' + 'Q' * capacity, read(objects, capacity * 8))
        print('CORE occupied', sum(bool(v) for v in slots))
        # Host preview cap20, raw field snapshots240/700 bytes and ASCII preview128 bytes; not object sizes.
        # Full ID/proto/owner/XYZ/health offsets below are reviewed in LIVE-UNITS.md; mask3ffff is 18-bit slot index.
        for slot, unit in [(i, v) for i, v in enumerate(slots) if v][:20]:
            body = read(unit, 0x240)
            unit_id = struct.unpack_from('<i', body, 0x1d8)[0]
            assert unit_id >= 0 and unit_id & 0x3ffff == slot, 'Core ID/slot mismatch'
            proto_id = struct.unpack_from('<i', body, 0x1dc)[0]
            player = struct.unpack_from('<i', body, 0x208)[0]  # Core owner via 0x448750, not prototype override +1e0.
            pos = [struct.unpack_from('<f', body, offset)[0] for offset in [0x5c, 0x6c, 0x7c]]
            assert 0 <= proto_id < proto_count
            proto = pointer(proto_array + proto_id*8)
            proto_name = read(pointer(proto+0x10),128).split(b'\0',1)[0].decode('ascii')
            bigger = read(unit,0x700)
            # Candidate-only scalar scan: start1d0/stride4 (binary32); 55/10/100/1 are observed/sample values,
            # NOT proof of a health field. Independently recovered health23c/max248 printed separately.
            likely_health = [(hex(j),struct.unpack_from('<f',bigger,j)[0]) for j in range(0x1d0,len(bigger),4) if struct.unpack_from('<f',bigger,j)[0] in [55.0,10.0,100.0,1.0]]
            health = struct.unpack_from('<f',body,0x23c)[0]
            max_health = struct.unpack_from('<f',read(unit+0x248,4))[0]
            print('CORE OBJECT', unit_id, 'proto',proto_id,proto_name,'player',player,'position',pos,'health',health,'maxHealth',max_health,'address',hex(unit),'candidate scalars',likely_health)
            (ROOT/'research/extracted'/f'core-unit-{unit_id}.bin').write_bytes(bigger)
        print('PROTO ROOT',hex(proto_root),'count',struct.unpack('<i',read(proto_root+0x1448,4))[0], 'array',hex(pointer(proto_root+0x1458)))
    if args.map_registry:
        # Native editor current-player helper 0x1b1080 returns 1 at editor branch 0x1b10f0.
        # uiLookAtSelection selects game+(player+0x6c)*16, holder+0x10 -> group.
        holder = pointer(game + (1 + 0x6c)*16)
        group = pointer(holder+0x10) if holder else 0
        selection_count = struct.unpack('<i',read(group+8,4))[0] if group else 0
        selection_array = pointer(group+0x18) if group else 0
        assert 0 <= selection_count <= 4096  # Host passive output safety limit, not game cap.
        print('SELECTION holder/group/count/array',hex(holder),hex(group),selection_count,hex(selection_array))
        if selection_count:
            print('SELECTION entries',read(selection_array,selection_count*16).hex())
        # kbGetMapX/ZSize: context+8 world mirror, +0x260 terrain, +c/+10 tile dims, +1c scale.
        context = pointer(base+layout['contextRva'])
        mirror = pointer(context+8)
        terrain = pointer(mirror+0x260)
        print('TERRAIN pointer',hex(terrain),'header',read(terrain,0x90).hex())
        vtable = pointer(terrain)
        getter = pointer(vtable+0x328)  # trGetTerrainHeight tail-dispatch virtual slot.
        print('TERRAIN height getter RVA',hex(getter-base))
        args.rva.append(getter-base)
        # uiCameraControl reads game+0xe8; header is research-only, no inferred camera properties.
        camera = pointer(game+0xe8)
        print('CAMERA controller',hex(camera),'header',read(camera,0x500).hex())
        (ROOT/'research/extracted/camera-controller.bin').write_bytes(read(camera,0x800))
        # Pose helpers show default camera+d0; native fov uses ACTIVE+d8. Fields/RVAs in LIVE-VIEW.md.
        # Raw headers/snapshots90/500/800/2600 and renderer window23f0+80 are host inspection windows,
        # not asserted object sizes. XYZ basis vector rows0/10/20/30; 48f/192 bytes previews first48 binary32 values.
        pose = pointer(game+0xd8)  # Active camera field used by native fov command, not default +d0.
        print('CAMERA pose',hex(pose),'first floats',struct.unpack('<48f',read(pose,192)))
        (ROOT/'research/extracted/camera-pose.bin').write_bytes(read(pose,0x800))
        (ROOT/'research/extracted/terrain-header.bin').write_bytes(read(terrain,0x800))
        # 0x163d650's RIP load identifies render state global; following +0x2418..24 feed viewport.
        renderer = pointer(base+0x5429b30)
        print('RENDERER settings',hex(renderer),read(renderer+0x23f0,0x80).hex())
        (ROOT/'research/extracted/renderer-state.bin').write_bytes(read(renderer,0x2600))
        # Height getter 0x3d5d70 proves terrain+78 grid, data+10, row stride+2c, count+0.
        grid = terrain+0x78
        # Grid header30-byte preview; eight binary32 samples=32 bytes, host preview choices.
        print('HEIGHT grid',read(grid,0x30).hex(),'sample',struct.unpack('<8f',read(pointer(grid+0x10),32)))
    if args.registry:
        # Observed KB mirror getters for accepted hash ONLY, not production simulation-object layout.
        # Table+138/count+140, ID+8, encoded position/player/proto slots10/18/a0 and pointee4c/8
        # come from getter disassembly; transform constants below are exact recovered arithmetic/XOR literals.
        # Mask3ffff/64-bit truncation reproduce inspected getter math. This mirror stayed EMPTY in editor.
        # Header16/bodya8 and sample cap20 are host read/preview windows, not object-size proofs.
        context = pointer(base + layout['contextRva'])
        world = pointer(context + 8)
        header = read(world + 0x138, 16)
        objects, capacity = struct.unpack_from('<QI', header)
        print('REGISTRY header', hex(context), hex(world), hex(objects), capacity, header.hex())
        assert 0 <= capacity <= 0x40000 and (objects or capacity == 0), 'Invalid object registry candidate'
        slots = struct.unpack('<' + 'Q' * capacity, read(objects, capacity * 8))
        print('REGISTRY capacity', capacity, 'occupied', sum(bool(v) for v in slots))
        for slot, unit in [(i, v) for i, v in enumerate(slots) if v][:20]:
            body = read(unit, 0xa8)
            unit_id = struct.unpack_from('<i', body, 8)[0]
            assert unit_id >= 0 and unit_id & 0x3ffff == slot, 'ID/slot mismatch'
            position_ptr = struct.unpack_from('<Q', body, 0x10)[0] ^ 0xaf0fbcdbc4cd5591 ^ 0xf5e4f83601e363cd
            player_ptr = (struct.unpack_from('<Q', body, 0x18)[0] + 0x258c9dbcfe05e761 - 0x1f36401ebf40d0aa) & ((1 << 64)-1)
            proto_ptr = (struct.unpack_from('<Q', body, 0xa0)[0] + 0x543b59facceef254 + 0x2e3561dc14d7d33f) & ((1 << 64)-1)
            position = struct.unpack('<4f', read(position_ptr, 16))
            player = struct.unpack('<i', read(player_ptr+0x4c, 4))[0] if player_ptr else -1
            proto = struct.unpack('<i', read(proto_ptr+8, 4))[0]
            print('OBJECT', unit_id, 'player', player, 'proto', proto, 'position', position, 'objectRva', hex(unit-base), 'protoRva',hex(proto_ptr-base))
    disassembler.detail = True
    # Candidate displacement search: DWORD pattern4 bytes; eight-byte backwards decoding window is a host heuristic,
    # not instruction-boundary proof. Capstone confirms decoded memory displacement; output sample cap35 is host policy.
    for field in args.field:
        pos = text.find(struct.pack('<I', field))
        results = []
        while pos >= 0:
            for start in range(max(0,pos-8),pos):
                ins = next(disassembler.disasm(text[start:pos+8],text_rva+start,count=1),None)
                if ins and ins.address + ins.size == text_rva + pos + 4 and any(op.type == capstone.x86.X86_OP_MEM and op.mem.disp == field for op in ins.operands):
                    results.append(f'{ins.address:08x}: {ins.mnemonic} {ins.op_str}')
                    break
            pos = text.find(struct.pack('<I',field),pos+4)
        (ROOT/'research/extracted'/f'field-{field:x}-refs.txt').write_text('\n'.join(results))
        # Camera class helpers cluster in this recovered build's 0x167xxxx RVA region (research only).
        camera_refs = [line for line in results if 0x1670000 <= int(line.split(':')[0],16) < 0x1690000]
        print('FIELD',hex(field),'refs',len(results),'samples',results[:35], 'camera',camera_refs)
    for name in args.string:
        needle = name.encode('ascii')+b'\0'
        locations = set()
        for section,_,rva,rawsize,offset in sections:
            if section == '.text':
                continue
            data = disk[offset:offset+rawsize]
            pos = data.find(needle)
            while pos>=0:
                if pos==0 or data[pos-1]==0:
                    locations.add(rva+pos)
                pos=data.find(needle,pos+len(needle))
        refs=[]
        # AMD64 RIP LEA opcode8d; REX48/4c, ModRM05/0d/.../3d encode destination GPRs.
        # Three-byte prefix, signed disp32+3, seven-byte total instruction (Intel x86-64 encoding).
        for prefix in [bytes([a,0x8d,b]) for a in [0x48,0x4c] for b in [0x05,0x0d,0x15,0x1d,0x25,0x2d,0x35,0x3d]]:
            pos=text.find(prefix)
            while pos>=0:
                if text_rva+pos+7+struct.unpack_from('<i',text,pos+3)[0] in locations:
                    refs.append(text_rva+pos)
                pos=text.find(prefix,pos+3)
        print('STRING',name,'refs',[hex(r) for r in refs])
    for name in args.name:
        found = targets(name)
        print(name, 'candidate targets', [hex(v) for v in sorted(found)])
        if len(found) == 1:
            args.rva.append(next(iter(found)))
    for rva in args.rva:
        print(f'FUNCTION RVA {rva:#x}')
        for i, ins in enumerate(disassembler.disasm(code(rva), rva)):
            print(f'{ins.address:08x}: {ins.mnemonic} {ins.op_str}')
            if ins.mnemonic == 'ret' or i >= 150:  # Host disassembly output cap, not function size.
                break
finally:
    k.CloseHandle(handle)
