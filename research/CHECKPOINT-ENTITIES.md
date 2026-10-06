# Reviewed checkpoint entity framing

Read-only review on executable build `dd15d1d838e78faa1bc9854becc3994f4f3a4548ef30efd24108abedc1b84fff`; no debugger, input, process writes, or scenario mutation.

## Sources and scope

- `research/BaseDefense.mythscn`: 25 entities.
- Existing local player-matrix checkpoint: 12,547 entities, 45 nonempty notes.
- Existing farming-session `p6-rebuild-before-20261006.mythscn`: 12,644 entities, 55 nonempty notes. Ten Player 6 CinematicBlockSpawnPoint notes decoded as two Tower, four Smoke Trap, four Spike Trap; IDs/owners/positions corroborate session results.
- Proprietary records are not copied into new tracked fixtures. `tests/fixtures/checkpoint-entities.json` describes synthetic records, including Unicode and unsupported variants.

## Framing

BG decoded header length 10; ordered tag + U32 byte-length sections, optional retained trailing byte. J1 header U32 observed value 444, followed by ordered sections. Duplicate tags are valid (e.g. TM, UA, SS), so preserve occurrences.

PT: U32 header 0, U32 count, count length-prefixed Latin-1 prototype names (trailing NUL retained in bytes, trimmed in display); no remaining bytes in reviewed records. Saved table order is not runtime prototype order.

Z1: U32 entity count, byte 1, repeated U32 registry ID + `H1`/U32 size/body. Full consumption required. Entity IDs must be unique.

H1 begins with framed EN then framed P1 then framed P2:
- EN reviewed length 80: first U32 repeats registry ID; owner U32 at byte 8; XYZ floats at byte 32. Other fields remain opaque; they are not claimed to be live actions or health.
- First P1 lengths reviewed: 86, 128, 178, 220. First two U32 values index PT. Resolve a single prototype only when both resolve to the same exact name; otherwise report the ambiguity. Do not infer unreviewed current/base semantics.
- P2 reviewed length 15. Following it are six opaque U32 fields, a U32 count (observed 10), that many U32 fields, then byte 0/1 declaring note presence. If 1, read bounded U32 character count + strict UTF-16LE note. This derives note position from section lengths and list framing, not a fixed absolute offset or byte search.
- Remaining H1 bytes are retained as opaque data. Notes/positions/prototype/owner are partial entity semantics; unknown tail fields prevent universal semantic equality claims.

Reject unsupported J1/PT/Z1 headers, EN/P1/P2 sizes, field-count variants, malformed lengths/UTF-16, invalid owner/coordinates, duplicate or mismatched IDs. No production global searches for EN, ID, or text byte sequences. Saved IDs do not prove current live identity after reload.
