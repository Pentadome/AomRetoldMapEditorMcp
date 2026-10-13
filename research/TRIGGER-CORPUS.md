# Trigger corpus census (B1a, 2026-10-13)

Offline, read-only scan of game-written scenarios with the project's own TR parser
(`CampaignTriggers.ParseScenarioSection`, reached through `CheckpointDocument` so player blocks are not parsed).
Scanner: `TriggerExemplars.Scan`. Files were read in place; nothing was copied into the repository.
Only counts and names are recorded here.

## Corpora

| Corpus | Scenarios | TR v12 parsed | TR v11 | Other failures |
|---|---|---|---|---|
| Install `game\campaign\**\*.mythscn` | 207 | 37 | 170 | 0 |
| Profile `mods\{local,subscribed,subscribed2}` (3 mods) | 38 | 30 | 8 | 0 |

- Two install files (`om\om09.mythscn`, `om\cinematics\om12B_a.mythscn`) first failed on argument value type 99
  (`Protounit: Modify Spawn Data` → `Event`, vartype `spawneventtype`). It is a plain single value
  (U32 prefix, UTF-16 string, trailer byte), so the reader's cap was raised from 82 to 99.
- Going through `ScenarioReader.Read` instead failed 31 install files on player-block checks
  (`PL age outside observed bounds`, `PL P8 layout unreviewed`). The census does not need player data.

## TR v11 is not a byte source

TR v11 element records differ from v12 at the argument level (observed in `fott\fott02.mythscn`):

- no per-argument trailer byte;
- unit-list arguments (value type 4) carry the value list and one byte, with no object-reference list;
- the argument magic is 10, where v12 files use 1 or 12.

The trigger and element headers otherwise match. A v11 element therefore cannot be byte-copied into a v12 export.
No v11→v12 converter was written, because it would be unproven. Possible user-side route (untested): open a stock
scenario in the editor and save it under a new name in the profile. The game writes the current version, and that
file is then a valid `sourcePath`.

## Definition coverage (v12 only)

- Install: 239 definitions seen, of 538 catalog names; 157 occur in at least 3 files.
- Mods: 218 definitions; 195 occur in at least 3 files.
- `Chat: Send Chat`: install `om\om09.mythscn` only (2 occurrences). In mods, 25 files.
- `Chat: Character Dialogue`: 35 install files.
- `Prompt: Two Options` and `XS: Code Snippet`: not present in any v12 file of either corpus.
  The plan's earlier string-search census counted them in v11 files.

## Value-type re-encode proofs

Rule: a numeric value type is **proven** when it has at least 3 samples and every decoded value re-encodes to
identical bytes (the undecoded U32 prefix and the flag byte are kept verbatim). A catalog vartype is proven
when every numeric type it appeared with is proven.

- Install corpus: 49 vartypes proven, including `string` (1), `stringid` (22, 3032 samples), `long` (0), `float` (2),
  `bool` (3), `player` (6), `event` (8), `protounit` (13), `unittype` (26), `operator` (15), `group` (17),
  `tech` (10), `godpower` (12), `sound` (7, wide list), `animname`/`animvariation`/`protoaction` (42/43/50).
- Unproven in the install corpus: `kbstat` (23, 1 sample), `culture` (27, 2), `spawneventtype` (99, 2).
  Every sample matched; there were just too few.
- `unit` (4) is never re-encoded by this path; object lists keep their own reviewed encoder in `TriggerObjects`.
- The mods corpus adds samples (`civilization` 28, `damagetype` 58, `modifytype` 63, `spawneventtype` 99, …);
  all of its non-object types matched as well. Proofs that ship in the index use only the install corpus (decision D8).
- Undecoded U32 prefix: 1 for every single-value type; the U32 after the `stringid` count is 0.

## Cross-file copy (B1c)

`editor_trigger_edit` `copyEffects` entries may name `sourcePath` + `sourceSha256` (another `.trg` or a TR v12 `.mythscn`;
library locations carry both). `copyValues` edits single-value parameters of a copied effect only when the value type is
proven above; the old value region must also re-encode byte-exact first. Verified offline (no game): `Chat: Send Chat`
from `om\om09.mythscn` trigger 647 element 1 copied into the om10 roundtrip export with `PlayerID=1` and a plain
`Message`; written to a temp file and read back. In-game behaviour of the copied effect is untested.
`Prompt: Two Options` and `XS: Code Snippet` still have no v12 install exemplar; re-saving a stock scenario in the editor
(untested) is the suggested way to obtain one.
