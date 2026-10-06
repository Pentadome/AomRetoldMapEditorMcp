"""Independent synthetic reviewed-format fixtures. No proprietary scenario records."""
import struct
import zlib


def u32(value):
    return struct.pack('<I', value)


def wide(value):
    data = value.encode('utf-16-le')
    return u32(len(data) // 2) + data


def narrow(value):
    data = value.encode('latin1')
    return u32(len(data)) + data


def block(tag, data):
    return tag.encode('ascii') + u32(len(data)) + data


def trigger_fixture(selection=1, trailer=127):
    def objects(key, unit_id, proto):
        return (u32(1) + narrow(key) + narrow(key) + u32(4)
                + u32(1) + wide(str(unit_id)) + u32(1)
                + u32(unit_id) + u32(1) + wide(proto) + bytes([selection, trailer]))

    always = u32(6) + narrow('Always') * 2 + u32(0) + narrow('true') + u32(0) + bytes(2)
    extras = (u32(3) + narrow('trUnitSelectClear();') + bytes(1) + u32(0)
              + narrow('trUnitSelectByID(%SrcObject%);') + bytes([1]) + u32(1) + narrow('SrcObject')
              + narrow('trUnitDoWorkOnUnit(%DstObject%, %EventID%);') + bytes(1) + u32(0))
    task = (u32(6) + narrow('Unit: Task') * 2 + u32(3)
            + objects('SrcObject', 100, 'VillagerAztec') + objects('DstObject', 200, 'Farm')
            + u32(1) + narrow('EventID') * 2 + u32(8) + u32(1) + wide('-1') + bytes(2)
            + narrow('true') + extras + bytes(2))
    record = (u32(9) + u32(704) + u32(0) + u32(3) + wide('Startup') + u32(0)
              + bytes([0, 1, 0, 0, 0]) + wide('') + u32(1) + always + u32(1) + task)
    group = u32(1) + u32(1) + u32(0) + narrow('Ungrouped') + u32(1) + u32(704)
    body = u32(12) + bytes(12) + u32(1) + record + group
    return block('TR', body) + struct.pack('<i', -1)


def checkpoint_fixture(note='塔 — Δέντρο 🌴', world_header=444, p1_size=86, units=None):
    units = units or [(100, 6, 'CinematicBlockSpawnPoint', 10.0, 4.0, 20.0, note)]
    protos = list(dict.fromkeys(unit[2] for unit in units))
    entities = u32(len(units)) + bytes([1])
    for unit_id, owner, proto, x, y, z, unit_note in units:
        identity = u32(unit_id) + u32(0) + u32(owner) + u32(16) + bytes(16)
        identity += struct.pack('<fff', x, y, z) + bytes(36)
        p1 = u32(protos.index(proto)) * 2 + bytes(p1_size - 8)
        entity = block('EN', identity) + block('P1', p1) + block('P2', bytes(15))
        entity += bytes(24) + u32(10) + bytes(40) + bytes([unit_note is not None])
        if unit_note is not None:
            entity += wide(unit_note)
        entity += bytes(28)
        entities += u32(unit_id) + block('H1', entity)
    world = (u32(world_header) + block('PT', u32(0) + u32(len(protos)) + b''.join(narrow(proto + '\0') for proto in protos))
             + block('Z1', entities) + block('UA', bytes(4)) + block('UA', bytes(4)))
    decoded = b'BG' + bytes(8) + block('J1', world) + bytes(1)
    return b'l33t' + u32(len(decoded)) + zlib.compress(decoded)
