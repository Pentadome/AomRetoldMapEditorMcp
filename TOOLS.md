# Complete Age of Mythology Retold editor MCP tool reference

**Current full: 923 tools = 71 helpers + 434 native commands + 418 shipped editor actions. Default core: 73 = 64 core helpers + nine essential native commands.** Eleven world-space scene helpers (core): see [research/SCENE-TOOLS.md](research/SCENE-TOOLS.md). Twelve world editing helpers (core): see [research/LIVE-WORLD.md](research/LIVE-WORLD.md).

Authoritative source: production `aom-retold-editor` `0.1.0` `tools/list` with `--toolset full`, protocol `2025-11-25`. Executable SHA-256: `dd15d1d838e78faa1bc9854becc3994f4f3a4548ef30efd24108abedc1b84fff`.

The catalog below is a historical generated reference from an earlier 28-helper snapshot; it does **not** list newer helpers or their latest schemas. Query live `tools/list` or narrow `editor_catalog` for authoritative signatures. Current additions: `editor_search_tools`, `editor_trigger_player_parity`, `editor_ui_read`, `editor_player_settings`; existing `editor_trigger_edit` adds batched edits/labels/values/condition removal; `editor_set_diplomacy` adds matrix changes, `editor_players` richer checkpoint fields, `editor_screenshot` region/scale. See [workflow limits](research/WORKFLOW-TOOLS.md). `editor_toolset` switches core/full mid-session; actual changes emit `notifications/tools/list_changed`. Clients must refresh tools/list without old cursor. The 40 core helpers are in both sets; seven new [session workflow helpers](research/SESSION-WORKFLOWS.md), most native commands and every shipped action require full mode. Playtest has one reviewed English alternative-UI 2560×1440 Player1/Standard profile; authorized public start/inspect/token-bound Quit passed. Other UI states and runtime memory telemetry remain unsupported. XS probes/supplied transcripts do not establish compiler/runtime proof. Mode operations are standalone-only. Hidden direct calls/batch steps refuse before game connection; core is not a permissions sandbox. Native/action exposure does **not** establish semantic/live validation of every command. Native acknowledgements are not getter values or proof of effects. See [README.md](README.md), [workflow limits](research/WORKFLOW-TOOLS.md) and [live evidence](research/LIVE-MUTATIONS.md).

## Current helper: `editor_search_tools`

Read-only, available in core/full; searches cached full-catalog tool names and descriptions without connecting to the game, switching mode or executing matches. No list-change notification. Removed native `loadScenario` and removed action aliases are excluded.

- Required `query`: nonblank string, 1..256 characters; trimmed and split on whitespace. All terms must match a name or description, case-insensitively; no regex/fuzzy search.
- Optional `offset`: integer >=0, default0. Optional `limit`: integer1..50, default10. No matches or offset past end returns an empty page.
- Ranking: exact name, whole-query name substring, all terms in name, then description/mixed matches; ordinal name tie-break.
- Output: `toolset`, `query`, `total`, `offset`, `limit`, nullable `nextOffset`, `guidance`, `tools` entries `{name, description, available, requiredToolset}`. No input schemas or script bodies. `requiredToolset` is minimum core/full surface; `available` means exposed now, not runtime readiness.
- Hidden matches require standalone `editor_toolset mode=full`, then fresh `tools/list` without an old cursor. Search itself leaves hidden direct/batch calls blocked. Metadata-only search batches need no game connection.

```json
{"name":"editor_search_tools","arguments":{"query":"camera","limit":5}}
```

## Safety and conventions

- All coordinates explicitly described as client pixels use full-resolution client coordinates, not scaled screenshots.
- Schema-required confirmation fields must be supplied explicitly; flags do not replace operator approval.
- Unknown/stale builds fail closed; optional reviewed read layouts are required for related object/map/selection tools.
- Batch preflight is not atomicity. Stop on first error; inspect actual state before further operations, never blindly retry mutations.
- Helper file outputs require approved new destinations; raw native file commands can overwrite their profile-relative targets.
- Native `uiLoadTriggers`, `uiSaveTriggers` and `saveScenario` expect filename stems; game appends extensions.
- Native `editor_loadScenario` was removed after a game crash. The same saved file loaded through normal Load Scenario UI after restart; use that UI instead. Direct/batch calls and actions referencing the removed command refuse even with confirmation/full mode. See [evidence](research/ALTERNATIVE-UI.md).
- Annotations are MCP hints, not additional permission or guarantees. Required fields and bounds are recorded verbatim below.

## Sections

- [Historical helpers (28)](#helpers)
- [Native commands (434)](#native-commands)
- [Shipped editor actions (418)](#shipped-editor-actions)

## Alphabetical index

- [`action_BrushFunctionsCopyPasteHeight`](#action_brushfunctionscopypasteheight)
- [`action_BrushFunctionsCopyPasteTexture`](#action_brushfunctionscopypastetexture)
- [`action_BrushFunctionsCopyPasteWater`](#action_brushfunctionscopypastewater)
- [`action_BrushFunctionsPlaceGateBtn`](#action_brushfunctionsplacegatebtn)
- [`action_BrushFunctionsPlacePlaceWallBtn`](#action_brushfunctionsplaceplacewallbtn)
- [`action_BrushFunctionsTerrainBrushMask`](#action_brushfunctionsterrainbrushmask)
- [`action_BrushFunctionsTerrainEdit`](#action_brushfunctionsterrainedit)
- [`action_BrushFunctionsWaterEdit`](#action_brushfunctionswateredit)
- [`action_BrushSettingsButton`](#action_brushsettingsbutton)
- [`action_BrushSettingsCloseButton`](#action_brushsettingsclosebutton)
- [`action_CameraStartButton`](#action_camerastartbutton)
- [`action_CinematicsButton`](#action_cinematicsbutton)
- [`action_CliffPaintButton`](#action_cliffpaintbutton)
- [`action_CombatCommands_AggressiveBtn`](#action_combatcommands_aggressivebtn)
- [`action_CombatCommands_AttackMoveBtn`](#action_combatcommands_attackmovebtn)
- [`action_CombatCommands_BellBtn`](#action_combatcommands_bellbtn)
- [`action_CombatCommands_BoxFormationBtn`](#action_combatcommands_boxformationbtn)
- [`action_CombatCommands_DefensiveBtn`](#action_combatcommands_defensivebtn)
- [`action_CombatCommands_LineFormationBtn`](#action_combatcommands_lineformationbtn)
- [`action_CombatCommands_NoAttackBtn`](#action_combatcommands_noattackbtn)
- [`action_CombatCommands_PatrolBtn`](#action_combatcommands_patrolbtn)
- [`action_CombatCommands_SpreadFormationBtn`](#action_combatcommands_spreadformationbtn)
- [`action_CombatCommands_StandGroundBtn`](#action_combatcommands_standgroundbtn)
- [`action_CombatCommands_StopBtn`](#action_combatcommands_stopbtn)
- [`action_CombatCommands_WorkBtn`](#action_combatcommands_workbtn)
- [`action_DeleteUnitButton`](#action_deleteunitbutton)
- [`action_DiplomacySettingsCloseButton`](#action_diplomacysettingsclosebutton)
- [`action_EditWaterButton`](#action_editwaterbutton)
- [`action_ForestButton`](#action_forestbutton)
- [`action_GroupingSettingsCancelButton`](#action_groupingsettingscancelbutton)
- [`action_GroupingSettingsDialog_OKButton`](#action_groupingsettingsdialog_okbutton)
- [`action_LandButton`](#action_landbutton)
- [`action_LetterBoxBars`](#action_letterboxbars)
- [`action_LightingButton`](#action_lightingbutton)
- [`action_LoadButton`](#action_loadbutton)
- [`action_MapElevationCloseButton`](#action_mapelevationclosebutton)
- [`action_MixButton`](#action_mixbutton)
- [`action_MoveUnitButton`](#action_moveunitbutton)
- [`action_NewButton`](#action_newbutton)
- [`action_NewScenarioCloseButton`](#action_newscenarioclosebutton)
- [`action_OceanButton`](#action_oceanbutton)
- [`action_PaintTerrainButton`](#action_paintterrainbutton)
- [`action_PlaceUnitButton`](#action_placeunitbutton)
- [`action_PlaytestScenarioCancelButton`](#action_playtestscenariocancelbutton)
- [`action_PlaytestScenarioNOWButton`](#action_playtestscenarionowbutton)
- [`action_PlaytestScenarioOKButton`](#action_playtestscenariookbutton)
- [`action_RaiseLowerButton`](#action_raiselowerbutton)
- [`action_RedoButton`](#action_redobutton)
- [`action_RoughenButton`](#action_roughenbutton)
- [`action_SampleElevationButton`](#action_sampleelevationbutton)
- [`action_SaveButton`](#action_savebutton)
- [`action_SaveButton_2`](#action_savebutton_2)
- [`action_ScenarioSummaryCloseButton`](#action_scenariosummaryclosebutton)
- [`action_SmoothButton`](#action_smoothbutton)
- [`action_TerrainCopyButton`](#action_terraincopybutton)
- [`action_TerrainPasteButton`](#action_terrainpastebutton)
- [`action_TriggersButton`](#action_triggersbutton)
- [`action_UndoButton`](#action_undobutton)
- [`action_UnitCopyButton`](#action_unitcopybutton)
- [`action_UnitPasteButton`](#action_unitpastebutton)
- [`action_WorldLightingCloseButton`](#action_worldlightingclosebutton)
- [`action_camEdit_Track_copybtn`](#action_camedit_track_copybtn)
- [`action_camEdit_Track_delbtn`](#action_camedit_track_delbtn)
- [`action_camEdit_Track_downbtn`](#action_camedit_track_downbtn)
- [`action_camEdit_Track_downbtn_2`](#action_camedit_track_downbtn_2)
- [`action_camEdit_Track_insbtn`](#action_camedit_track_insbtn)
- [`action_camEdit_Track_loadbtn`](#action_camedit_track_loadbtn)
- [`action_camEdit_Track_preview`](#action_camedit_track_preview)
- [`action_camEdit_Track_savebtn`](#action_camedit_track_savebtn)
- [`action_camEdit_Track_upbtn`](#action_camedit_track_upbtn)
- [`action_camEdit_Track_upbtn_2`](#action_camedit_track_upbtn_2)
- [`action_camEdit_addbtn`](#action_camedit_addbtn)
- [`action_camEdit_applybtn`](#action_camedit_applybtn)
- [`action_camEdit_backbtn`](#action_camedit_backbtn)
- [`action_camEdit_camRotation`](#action_camedit_camrotation)
- [`action_camEdit_camStates`](#action_camedit_camstates)
- [`action_camEdit_delbtn`](#action_camedit_delbtn)
- [`action_camEdit_freeCam`](#action_camedit_freecam)
- [`action_camEdit_fwdbtn`](#action_camedit_fwdbtn)
- [`action_camEdit_newbtn`](#action_camedit_newbtn)
- [`action_camEdit_pausebtn`](#action_camedit_pausebtn)
- [`action_camEdit_playbtn`](#action_camedit_playbtn)
- [`action_camEdit_precise_waypoint`](#action_camedit_precise_waypoint)
- [`action_camEdit_reset`](#action_camedit_reset)
- [`action_camEdit_resetFov`](#action_camedit_resetfov)
- [`action_camEdit_resetPitch`](#action_camedit_resetpitch)
- [`action_camEdit_resetRot`](#action_camedit_resetrot)
- [`action_camEdit_resetZoom`](#action_camedit_resetzoom)
- [`action_camEdit_stopbtn`](#action_camedit_stopbtn)
- [`action_cameraStates_FreeCam`](#action_camerastates_freecam)
- [`action_cameraStates_ResetView`](#action_camerastates_resetview)
- [`action_cameraStates_Rotate`](#action_camerastates_rotate)
- [`action_key_LocSelect_esc`](#action_key_locselect_esc)
- [`action_key_LocSelect_mouse1down`](#action_key_locselect_mouse1down)
- [`action_key_LocSelect_mouse2up`](#action_key_locselect_mouse2up)
- [`action_key_TerrainPaste_esc`](#action_key_terrainpaste_esc)
- [`action_key_TerrainPaste_mouse1up`](#action_key_terrainpaste_mouse1up)
- [`action_key_TerrainPaste_mouse2up`](#action_key_terrainpaste_mouse2up)
- [`action_key_UnitPaste_esc`](#action_key_unitpaste_esc)
- [`action_key_UnitPaste_mouse1up`](#action_key_unitpaste_mouse1up)
- [`action_key_UnitPaste_mousez`](#action_key_unitpaste_mousez)
- [`action_key_camtrack_esc`](#action_key_camtrack_esc)
- [`action_key_convertunits_esc`](#action_key_convertunits_esc)
- [`action_key_convertunits_mouse1down`](#action_key_convertunits_mouse1down)
- [`action_key_convertunits_mouse1up`](#action_key_convertunits_mouse1up)
- [`action_key_copy_control_v`](#action_key_copy_control_v)
- [`action_key_copy_esc`](#action_key_copy_esc)
- [`action_key_copy_mouse1down`](#action_key_copy_mouse1down)
- [`action_key_copy_mouse1up`](#action_key_copy_mouse1up)
- [`action_key_definegrouping_control_v`](#action_key_definegrouping_control_v)
- [`action_key_definegrouping_esc`](#action_key_definegrouping_esc)
- [`action_key_definegrouping_mouse1down`](#action_key_definegrouping_mouse1down)
- [`action_key_definegrouping_mouse1up`](#action_key_definegrouping_mouse1up)
- [`action_key_deleteunits_esc`](#action_key_deleteunits_esc)
- [`action_key_deleteunits_mouse1down`](#action_key_deleteunits_mouse1down)
- [`action_key_deleteunits_mouse1up`](#action_key_deleteunits_mouse1up)
- [`action_key_detailHelp__`](#action_key_detailhelp__)
- [`action_key_editor_0`](#action_key_editor_0)
- [`action_key_editor_1`](#action_key_editor_1)
- [`action_key_editor_2`](#action_key_editor_2)
- [`action_key_editor_3`](#action_key_editor_3)
- [`action_key_editor_4`](#action_key_editor_4)
- [`action_key_editor_5`](#action_key_editor_5)
- [`action_key_editor_6`](#action_key_editor_6)
- [`action_key_editor_7`](#action_key_editor_7)
- [`action_key_editor_8`](#action_key_editor_8)
- [`action_key_editor_9`](#action_key_editor_9)
- [`action_key_editor__`](#action_key_editor__)
- [`action_key_editor___2`](#action_key_editor___2)
- [`action_key_editor__alt_shift_y`](#action_key_editor__alt_shift_y)
- [`action_key_editor__alt_y`](#action_key_editor__alt_y)
- [`action_key_editor__k`](#action_key_editor__k)
- [`action_key_editor__shift_k`](#action_key_editor__shift_k)
- [`action_key_editor__shift_space`](#action_key_editor__shift_space)
- [`action_key_editor__shift_y`](#action_key_editor__shift_y)
- [`action_key_editor__space`](#action_key_editor__space)
- [`action_key_editor__y`](#action_key_editor__y)
- [`action_key_editor_alt_1`](#action_key_editor_alt_1)
- [`action_key_editor_alt_2`](#action_key_editor_alt_2)
- [`action_key_editor_alt_3`](#action_key_editor_alt_3)
- [`action_key_editor_alt_4`](#action_key_editor_alt_4)
- [`action_key_editor_alt_a`](#action_key_editor_alt_a)
- [`action_key_editor_alt_b`](#action_key_editor_alt_b)
- [`action_key_editor_alt_c`](#action_key_editor_alt_c)
- [`action_key_editor_alt_mousez`](#action_key_editor_alt_mousez)
- [`action_key_editor_alt_r`](#action_key_editor_alt_r)
- [`action_key_editor_alt_u`](#action_key_editor_alt_u)
- [`action_key_editor_alt_v`](#action_key_editor_alt_v)
- [`action_key_editor_b`](#action_key_editor_b)
- [`action_key_editor_c`](#action_key_editor_c)
- [`action_key_editor_control_0`](#action_key_editor_control_0)
- [`action_key_editor_control_1`](#action_key_editor_control_1)
- [`action_key_editor_control_2`](#action_key_editor_control_2)
- [`action_key_editor_control_3`](#action_key_editor_control_3)
- [`action_key_editor_control_4`](#action_key_editor_control_4)
- [`action_key_editor_control_5`](#action_key_editor_control_5)
- [`action_key_editor_control_6`](#action_key_editor_control_6)
- [`action_key_editor_control_7`](#action_key_editor_control_7)
- [`action_key_editor_control_8`](#action_key_editor_control_8)
- [`action_key_editor_control_9`](#action_key_editor_control_9)
- [`action_key_editor_control__`](#action_key_editor_control__)
- [`action_key_editor_control___2`](#action_key_editor_control___2)
- [`action_key_editor_control_c`](#action_key_editor_control_c)
- [`action_key_editor_control_f`](#action_key_editor_control_f)
- [`action_key_editor_control_f1`](#action_key_editor_control_f1)
- [`action_key_editor_control_f10`](#action_key_editor_control_f10)
- [`action_key_editor_control_f11`](#action_key_editor_control_f11)
- [`action_key_editor_control_f12`](#action_key_editor_control_f12)
- [`action_key_editor_control_f2`](#action_key_editor_control_f2)
- [`action_key_editor_control_f3`](#action_key_editor_control_f3)
- [`action_key_editor_control_f4`](#action_key_editor_control_f4)
- [`action_key_editor_control_f5`](#action_key_editor_control_f5)
- [`action_key_editor_control_f6`](#action_key_editor_control_f6)
- [`action_key_editor_control_f7`](#action_key_editor_control_f7)
- [`action_key_editor_control_f8`](#action_key_editor_control_f8)
- [`action_key_editor_control_f9`](#action_key_editor_control_f9)
- [`action_key_editor_control_h`](#action_key_editor_control_h)
- [`action_key_editor_control_k`](#action_key_editor_control_k)
- [`action_key_editor_control_l`](#action_key_editor_control_l)
- [`action_key_editor_control_mouse1down`](#action_key_editor_control_mouse1down)
- [`action_key_editor_control_mouse1up`](#action_key_editor_control_mouse1up)
- [`action_key_editor_control_mousez`](#action_key_editor_control_mousez)
- [`action_key_editor_control_n`](#action_key_editor_control_n)
- [`action_key_editor_control_r`](#action_key_editor_control_r)
- [`action_key_editor_control_s`](#action_key_editor_control_s)
- [`action_key_editor_control_shift_s`](#action_key_editor_control_shift_s)
- [`action_key_editor_control_v`](#action_key_editor_control_v)
- [`action_key_editor_control_y`](#action_key_editor_control_y)
- [`action_key_editor_control_z`](#action_key_editor_control_z)
- [`action_key_editor_delete`](#action_key_editor_delete)
- [`action_key_editor_e`](#action_key_editor_e)
- [`action_key_editor_f`](#action_key_editor_f)
- [`action_key_editor_g`](#action_key_editor_g)
- [`action_key_editor_i`](#action_key_editor_i)
- [`action_key_editor_l`](#action_key_editor_l)
- [`action_key_editor_mouse1down`](#action_key_editor_mouse1down)
- [`action_key_editor_mouse1up`](#action_key_editor_mouse1up)
- [`action_key_editor_mouse3down`](#action_key_editor_mouse3down)
- [`action_key_editor_mouse3up`](#action_key_editor_mouse3up)
- [`action_key_editor_o`](#action_key_editor_o)
- [`action_key_editor_s`](#action_key_editor_s)
- [`action_key_editor_shift_0`](#action_key_editor_shift_0)
- [`action_key_editor_shift_c`](#action_key_editor_shift_c)
- [`action_key_editor_shift_e`](#action_key_editor_shift_e)
- [`action_key_editor_shift_f1`](#action_key_editor_shift_f1)
- [`action_key_editor_shift_f10`](#action_key_editor_shift_f10)
- [`action_key_editor_shift_f11`](#action_key_editor_shift_f11)
- [`action_key_editor_shift_f12`](#action_key_editor_shift_f12)
- [`action_key_editor_shift_f2`](#action_key_editor_shift_f2)
- [`action_key_editor_shift_f3`](#action_key_editor_shift_f3)
- [`action_key_editor_shift_f4`](#action_key_editor_shift_f4)
- [`action_key_editor_shift_f5`](#action_key_editor_shift_f5)
- [`action_key_editor_shift_f6`](#action_key_editor_shift_f6)
- [`action_key_editor_shift_f7`](#action_key_editor_shift_f7)
- [`action_key_editor_shift_f8`](#action_key_editor_shift_f8)
- [`action_key_editor_shift_f9`](#action_key_editor_shift_f9)
- [`action_key_editor_shift_l`](#action_key_editor_shift_l)
- [`action_key_editor_shift_mouse3down`](#action_key_editor_shift_mouse3down)
- [`action_key_editor_shift_mouse3up`](#action_key_editor_shift_mouse3up)
- [`action_key_editor_shift_mousez`](#action_key_editor_shift_mousez)
- [`action_key_editor_shift_r`](#action_key_editor_shift_r)
- [`action_key_editor_shift_s`](#action_key_editor_shift_s)
- [`action_key_editor_shift_w`](#action_key_editor_shift_w)
- [`action_key_editor_t`](#action_key_editor_t)
- [`action_key_editor_u`](#action_key_editor_u)
- [`action_key_editor_v`](#action_key_editor_v)
- [`action_key_editor_w`](#action_key_editor_w)
- [`action_key_editwater_esc`](#action_key_editwater_esc)
- [`action_key_editwater_mouse1down`](#action_key_editwater_mouse1down)
- [`action_key_elevation_esc`](#action_key_elevation_esc)
- [`action_key_elevation_mouse1down`](#action_key_elevation_mouse1down)
- [`action_key_elevation_mouse1up`](#action_key_elevation_mouse1up)
- [`action_key_elevation_mouse2down`](#action_key_elevation_mouse2down)
- [`action_key_elevation_mouse2up`](#action_key_elevation_mouse2up)
- [`action_key_elevationsample_esc`](#action_key_elevationsample_esc)
- [`action_key_elevationsample_mouse1down`](#action_key_elevationsample_mouse1down)
- [`action_key_elevationsample_mouse1up`](#action_key_elevationsample_mouse1up)
- [`action_key_elevationsample_mouse2down`](#action_key_elevationsample_mouse2down)
- [`action_key_list_esc`](#action_key_list_esc)
- [`action_key_modifyterrain___`](#action_key_modifyterrain___)
- [`action_key_modifyterrain____2`](#action_key_modifyterrain____2)
- [`action_key_modifyterrain__alt__`](#action_key_modifyterrain__alt__)
- [`action_key_modifyterrain__alt___2`](#action_key_modifyterrain__alt___2)
- [`action_key_modifyterrain_control_space`](#action_key_modifyterrain_control_space)
- [`action_key_modifyterrain_esc`](#action_key_modifyterrain_esc)
- [`action_key_modifyterrain_mouse1down`](#action_key_modifyterrain_mouse1down)
- [`action_key_modifyterrain_mouse1up`](#action_key_modifyterrain_mouse1up)
- [`action_key_modifyterrain_space`](#action_key_modifyterrain_space)
- [`action_key_moveunit__a`](#action_key_moveunit__a)
- [`action_key_moveunit__alt_1`](#action_key_moveunit__alt_1)
- [`action_key_moveunit__alt_2`](#action_key_moveunit__alt_2)
- [`action_key_moveunit__alt_3`](#action_key_moveunit__alt_3)
- [`action_key_moveunit__alt_4`](#action_key_moveunit__alt_4)
- [`action_key_moveunit__alt_a`](#action_key_moveunit__alt_a)
- [`action_key_moveunit__alt_arrowdown`](#action_key_moveunit__alt_arrowdown)
- [`action_key_moveunit__alt_arrowleft`](#action_key_moveunit__alt_arrowleft)
- [`action_key_moveunit__alt_arrowright`](#action_key_moveunit__alt_arrowright)
- [`action_key_moveunit__alt_arrowup`](#action_key_moveunit__alt_arrowup)
- [`action_key_moveunit__alt_control_arrowdown`](#action_key_moveunit__alt_control_arrowdown)
- [`action_key_moveunit__alt_control_arrowdown_2`](#action_key_moveunit__alt_control_arrowdown_2)
- [`action_key_moveunit__alt_control_arrowleft`](#action_key_moveunit__alt_control_arrowleft)
- [`action_key_moveunit__alt_control_arrowleft_2`](#action_key_moveunit__alt_control_arrowleft_2)
- [`action_key_moveunit__alt_control_arrowright`](#action_key_moveunit__alt_control_arrowright)
- [`action_key_moveunit__alt_control_arrowright_2`](#action_key_moveunit__alt_control_arrowright_2)
- [`action_key_moveunit__alt_control_arrowup`](#action_key_moveunit__alt_control_arrowup)
- [`action_key_moveunit__alt_control_arrowup_2`](#action_key_moveunit__alt_control_arrowup_2)
- [`action_key_moveunit__alt_d`](#action_key_moveunit__alt_d)
- [`action_key_moveunit__alt_d_2`](#action_key_moveunit__alt_d_2)
- [`action_key_moveunit__alt_s`](#action_key_moveunit__alt_s)
- [`action_key_moveunit__alt_s_2`](#action_key_moveunit__alt_s_2)
- [`action_key_moveunit__alt_w`](#action_key_moveunit__alt_w)
- [`action_key_moveunit__arrowdown`](#action_key_moveunit__arrowdown)
- [`action_key_moveunit__arrowleft`](#action_key_moveunit__arrowleft)
- [`action_key_moveunit__arrowright`](#action_key_moveunit__arrowright)
- [`action_key_moveunit__arrowup`](#action_key_moveunit__arrowup)
- [`action_key_moveunit__control_arrowdown`](#action_key_moveunit__control_arrowdown)
- [`action_key_moveunit__control_arrowdown_2`](#action_key_moveunit__control_arrowdown_2)
- [`action_key_moveunit__control_arrowleft`](#action_key_moveunit__control_arrowleft)
- [`action_key_moveunit__control_arrowleft_2`](#action_key_moveunit__control_arrowleft_2)
- [`action_key_moveunit__control_arrowright`](#action_key_moveunit__control_arrowright)
- [`action_key_moveunit__control_arrowright_2`](#action_key_moveunit__control_arrowright_2)
- [`action_key_moveunit__control_arrowup`](#action_key_moveunit__control_arrowup)
- [`action_key_moveunit__control_arrowup_2`](#action_key_moveunit__control_arrowup_2)
- [`action_key_moveunit__d`](#action_key_moveunit__d)
- [`action_key_moveunit__d_2`](#action_key_moveunit__d_2)
- [`action_key_moveunit__s`](#action_key_moveunit__s)
- [`action_key_moveunit__s_2`](#action_key_moveunit__s_2)
- [`action_key_moveunit__w`](#action_key_moveunit__w)
- [`action_key_moveunit_esc`](#action_key_moveunit_esc)
- [`action_key_moveunit_mouse1down`](#action_key_moveunit_mouse1down)
- [`action_key_moveunit_mouse1up`](#action_key_moveunit_mouse1up)
- [`action_key_moveunit_mousez`](#action_key_moveunit_mousez)
- [`action_key_paint_alt_mouse1down`](#action_key_paint_alt_mouse1down)
- [`action_key_paint_alt_mouse1down_2`](#action_key_paint_alt_mouse1down_2)
- [`action_key_paint_esc`](#action_key_paint_esc)
- [`action_key_paint_mouse1down`](#action_key_paint_mouse1down)
- [`action_key_paint_mouse1up`](#action_key_paint_mouse1up)
- [`action_key_paint_mouse2down`](#action_key_paint_mouse2down)
- [`action_key_paint_mouse2up`](#action_key_paint_mouse2up)
- [`action_key_paintcliff_esc`](#action_key_paintcliff_esc)
- [`action_key_paintcliff_mouse1down`](#action_key_paintcliff_mouse1down)
- [`action_key_paintcliff_mouse2down`](#action_key_paintcliff_mouse2down)
- [`action_key_paintcliff_mouse2up`](#action_key_paintcliff_mouse2up)
- [`action_key_paintforest_esc`](#action_key_paintforest_esc)
- [`action_key_paintforest_mouse1down`](#action_key_paintforest_mouse1down)
- [`action_key_paintforest_mouse1up`](#action_key_paintforest_mouse1up)
- [`action_key_paintwater_c`](#action_key_paintwater_c)
- [`action_key_paintwater_d`](#action_key_paintwater_d)
- [`action_key_paintwater_esc`](#action_key_paintwater_esc)
- [`action_key_paintwater_mouse1doubleup`](#action_key_paintwater_mouse1doubleup)
- [`action_key_paintwater_mouse1down`](#action_key_paintwater_mouse1down)
- [`action_key_paintwater_mouse2down`](#action_key_paintwater_mouse2down)
- [`action_key_paintwater_mouse2up`](#action_key_paintwater_mouse2up)
- [`action_key_placeTradeRoute_esc`](#action_key_placetraderoute_esc)
- [`action_key_placeTradeRoute_mouse1up`](#action_key_placetraderoute_mouse1up)
- [`action_key_placeTradeRoute_mouse2up`](#action_key_placetraderoute_mouse2up)
- [`action_key_placeWall_esc`](#action_key_placewall_esc)
- [`action_key_placeWall_mouse1doubledown`](#action_key_placewall_mouse1doubledown)
- [`action_key_placeWall_mouse1doubleup`](#action_key_placewall_mouse1doubleup)
- [`action_key_placeWall_mouse1down`](#action_key_placewall_mouse1down)
- [`action_key_placeWall_shift_mouse1down`](#action_key_placewall_shift_mouse1down)
- [`action_key_placeunit_esc`](#action_key_placeunit_esc)
- [`action_key_placeunit_mouse1down`](#action_key_placeunit_mouse1down)
- [`action_key_placeunit_mouse2down`](#action_key_placeunit_mouse2down)
- [`action_key_placeunit_mousez`](#action_key_placeunit_mousez)
- [`action_key_placeunit_shift_mouse1down`](#action_key_placeunit_shift_mouse1down)
- [`action_key_placeunit_shift_mouse2down`](#action_key_placeunit_shift_mouse2down)
- [`action_key_placeunitselect_alt_w`](#action_key_placeunitselect_alt_w)
- [`action_key_placeunitselect_esc`](#action_key_placeunitselect_esc)
- [`action_key_recalcvariation_esc`](#action_key_recalcvariation_esc)
- [`action_key_recalcvariation_mouse1down`](#action_key_recalcvariation_mouse1down)
- [`action_key_recalcvariation_mouse1up`](#action_key_recalcvariation_mouse1up)
- [`action_key_roughen_esc`](#action_key_roughen_esc)
- [`action_key_roughen_mouse1down`](#action_key_roughen_mouse1down)
- [`action_key_roughen_mouse1up`](#action_key_roughen_mouse1up)
- [`action_key_selectTransportUnit_esc`](#action_key_selecttransportunit_esc)
- [`action_key_selectTransportUnit_mouse1up`](#action_key_selecttransportunit_mouse1up)
- [`action_key_smooth_esc`](#action_key_smooth_esc)
- [`action_key_smooth_mouse1down`](#action_key_smooth_mouse1down)
- [`action_key_smooth_mouse1up`](#action_key_smooth_mouse1up)
- [`action_key_smooth_mouse2down`](#action_key_smooth_mouse2down)
- [`action_key_smooth_mouse2up`](#action_key_smooth_mouse2up)
- [`action_key_smooth_shift_space`](#action_key_smooth_shift_space)
- [`action_key_trigger_esc`](#action_key_trigger_esc)
- [`action_key_triggroups_esc`](#action_key_triggroups_esc)
- [`action_key_trigrectselect_esc`](#action_key_trigrectselect_esc)
- [`action_key_trigrectselect_mouse1down`](#action_key_trigrectselect_mouse1down)
- [`action_key_trigrectselect_mouse1up`](#action_key_trigrectselect_mouse1up)
- [`action_key_trigrectselect_mouse2up`](#action_key_trigrectselect_mouse2up)
- [`action_key_trigselect_esc`](#action_key_trigselect_esc)
- [`action_key_trigselect_mouse1down`](#action_key_trigselect_mouse1down)
- [`action_key_trigselect_mouse2up`](#action_key_trigselect_mouse2up)
- [`action_key_world_control_shift_f10`](#action_key_world_control_shift_f10)
- [`action_mapsize_CancelBtn`](#action_mapsize_cancelbtn)
- [`action_pitchEdit_CinematicsButton`](#action_pitchedit_cinematicsbutton)
- [`action_pitchEdit_bias_10`](#action_pitchedit_bias_10)
- [`action_pitchEdit_bias_110`](#action_pitchedit_bias_110)
- [`action_pitchEdit_bias_135`](#action_pitchedit_bias_135)
- [`action_pitchEdit_bias_15`](#action_pitchedit_bias_15)
- [`action_pitchEdit_bias_25`](#action_pitchedit_bias_25)
- [`action_pitchEdit_bias_35`](#action_pitchedit_bias_35)
- [`action_pitchEdit_bias_5`](#action_pitchedit_bias_5)
- [`action_pitchEdit_bias_55`](#action_pitchedit_bias_55)
- [`action_pitchEdit_bias_70`](#action_pitchedit_bias_70)
- [`action_pitchEdit_bias_90`](#action_pitchedit_bias_90)
- [`action_pitchEdit_close`](#action_pitchedit_close)
- [`action_pitchEdit_defaultOrientation`](#action_pitchedit_defaultorientation)
- [`action_pitchEdit_freeCam`](#action_pitchedit_freecam)
- [`action_pitchEdit_normalpitch`](#action_pitchedit_normalpitch)
- [`action_pitchEdit_normalzoom`](#action_pitchedit_normalzoom)
- [`action_pitchEdit_resetPitch`](#action_pitchedit_resetpitch)
- [`action_pitchEdit_rotate`](#action_pitchedit_rotate)
- [`action_pitchEdit_zoom20`](#action_pitchedit_zoom20)
- [`action_pitchEdit_zoom5`](#action_pitchedit_zoom5)
- [`action_pitchEdit_zoomdefault`](#action_pitchedit_zoomdefault)
- [`action_trEdit_LoadAllTriggers`](#action_tredit_loadalltriggers)
- [`action_trEdit_SaveAllTriggers`](#action_tredit_savealltriggers)
- [`action_ui_editor_menu_item_61`](#action_ui_editor_menu_item_61)
- [`action_ui_editor_menu_item_62`](#action_ui_editor_menu_item_62)
- [`action_ui_editor_menu_item_63`](#action_ui_editor_menu_item_63)
- [`action_ui_editor_menu_item_64`](#action_ui_editor_menu_item_64)
- [`action_ui_editor_menu_item_65`](#action_ui_editor_menu_item_65)
- [`action_ui_editor_menu_item_66`](#action_ui_editor_menu_item_66)
- [`action_ui_editor_menu_item_67`](#action_ui_editor_menu_item_67)
- [`action_ui_editor_menu_item_68`](#action_ui_editor_menu_item_68)
- [`action_ui_editor_menu_item_69`](#action_ui_editor_menu_item_69)
- [`action_ui_editor_menu_item_70`](#action_ui_editor_menu_item_70)
- [`action_ui_editor_menu_item_71`](#action_ui_editor_menu_item_71)
- [`action_ui_editor_menu_item_72`](#action_ui_editor_menu_item_72)
- [`action_ui_editor_menu_item_73`](#action_ui_editor_menu_item_73)
- [`action_ui_editor_menu_item_74`](#action_ui_editor_menu_item_74)
- [`action_ui_editor_menu_item_75`](#action_ui_editor_menu_item_75)
- [`action_ui_editor_menu_item_76`](#action_ui_editor_menu_item_76)
- [`action_ui_editor_menu_item_77`](#action_ui_editor_menu_item_77)
- [`action_ui_editor_menu_item_78`](#action_ui_editor_menu_item_78)
- [`action_ui_editor_menu_item_79`](#action_ui_editor_menu_item_79)
- [`action_ui_editor_menu_item_80`](#action_ui_editor_menu_item_80)
- [`action_ui_editor_menu_item_81`](#action_ui_editor_menu_item_81)
- [`action_ui_editor_menu_item_82`](#action_ui_editor_menu_item_82)
- [`action_ui_editor_menu_item_83`](#action_ui_editor_menu_item_83)
- [`action_ui_editor_menu_item_84`](#action_ui_editor_menu_item_84)
- [`action_ui_editor_menu_item_85`](#action_ui_editor_menu_item_85)
- [`action_ui_editor_menu_item_86`](#action_ui_editor_menu_item_86)
- [`action_ui_editor_menu_item_87`](#action_ui_editor_menu_item_87)
- [`action_ui_editor_menu_item_88`](#action_ui_editor_menu_item_88)
- [`action_ui_editor_menu_item_89`](#action_ui_editor_menu_item_89)
- [`action_ui_editor_menu_item_90`](#action_ui_editor_menu_item_90)
- [`action_ui_pitch_editor_item_21`](#action_ui_pitch_editor_item_21)
- [`action_ui_pitch_editor_item_22`](#action_ui_pitch_editor_item_22)
- [`action_ui_pitch_editor_item_23`](#action_ui_pitch_editor_item_23)
- [`action_ui_pitch_editor_item_24`](#action_ui_pitch_editor_item_24)
- [`action_ui_pitch_editor_item_25`](#action_ui_pitch_editor_item_25)
- [`action_ui_pitch_editor_item_26`](#action_ui_pitch_editor_item_26)
- [`action_ui_pitch_editor_item_27`](#action_ui_pitch_editor_item_27)
- [`action_ui_pitch_editor_item_28`](#action_ui_pitch_editor_item_28)
- [`action_ui_pitch_editor_item_29`](#action_ui_pitch_editor_item_29)
- [`action_ui_pitch_editor_item_30`](#action_ui_pitch_editor_item_30)
- [`action_ui_pitch_editor_item_31`](#action_ui_pitch_editor_item_31)
- [`editor_batch`](#editor_batch)
- [`editor_cameraLimit`](#editor_cameralimit)
- [`editor_cameraMaxZoomSet`](#editor_cameramaxzoomset)
- [`editor_cameraMinZoomSet`](#editor_cameraminzoomset)
- [`editor_cameraNice`](#editor_cameranice)
- [`editor_cameraPitchAngle`](#editor_camerapitchangle)
- [`editor_cameraPitchReset`](#editor_camerapitchreset)
- [`editor_cameraRotate`](#editor_camerarotate)
- [`editor_cameraRotationReset`](#editor_camerarotationreset)
- [`editor_cameraStart`](#editor_camerastart)
- [`editor_cameraZoomReset`](#editor_camerazoomreset)
- [`editor_catalog`](#editor_catalog)
- [`editor_configSetInt`](#editor_configsetint)
- [`editor_configToggle`](#editor_configtoggle)
- [`editor_cycleFogAndBlackMap`](#editor_cyclefogandblackmap)
- [`editor_dependencies`](#editor_dependencies)
- [`editor_editMode`](#editor_editmode)
- [`editor_focus`](#editor_focus)
- [`editor_fov`](#editor_fov)
- [`editor_gadgetFlash`](#editor_gadgetflash)
- [`editor_gadgetReal`](#editor_gadgetreal)
- [`editor_gadgetRealIfNotMP`](#editor_gadgetrealifnotmp)
- [`editor_gadgetRefresh`](#editor_gadgetrefresh)
- [`editor_gadgetScrollDown`](#editor_gadgetscrolldown)
- [`editor_gadgetScrollLeft`](#editor_gadgetscrollleft)
- [`editor_gadgetScrollRight`](#editor_gadgetscrollright)
- [`editor_gadgetScrollUp`](#editor_gadgetscrollup)
- [`editor_gadgetToggle`](#editor_gadgettoggle)
- [`editor_gadgetToggleIfNotMP`](#editor_gadgettoggleifnotmp)
- [`editor_gadgetUnreal`](#editor_gadgetunreal)
- [`editor_god_powers`](#editor_god_powers)
- [`editor_gods`](#editor_gods)
- [`editor_inspect_selection`](#editor_inspect_selection)
- [`editor_key`](#editor_key)
- [`editor_map_info`](#editor_map_info)
- [`editor_mouse_click`](#editor_mouse_click)
- [`editor_mouse_drag`](#editor_mouse_drag)
- [`editor_mouse_move`](#editor_mouse_move)
- [`editor_mouse_wheel`](#editor_mouse_wheel)
- [`editor_openTerrainTextureBrowserGui`](#editor_openterraintexturebrowsergui)
- [`editor_openTerrainTextureEditor`](#editor_openterraintextureeditor)
- [`editor_openWaterBrowserGui`](#editor_openwaterbrowsergui)
- [`editor_openWaterEditorGui`](#editor_openwatereditorgui)
- [`editor_pantheon`](#editor_pantheon)
- [`editor_generate_catalog`](#editor_generate_catalog)
- [`editor_pause`](#editor_pause)
- [`editor_place_formation`](#editor_place_formation)
- [`editor_place_unit`](#editor_place_unit)
- [`editor_player`](#editor_player)
- [`editor_prototypes`](#editor_prototypes)
- [`editor_redo`](#editor_redo)
- [`editor_refreshObjectInfoPanel`](#editor_refreshobjectinfopanel)
- [`editor_saveScenario`](#editor_savescenario)
- [`editor_save_checkpoint`](#editor_save_checkpoint)
- [`editor_screenshot`](#editor_screenshot)
- [`editor_setFreeCam`](#editor_setfreecam)
- [`editor_setSquadMode`](#editor_setsquadmode)
- [`editor_status`](#editor_status)
- [`editor_sunDecreaseInclination`](#editor_sundecreaseinclination)
- [`editor_sunDecreaseRotation`](#editor_sundecreaserotation)
- [`editor_sunIncreaseInclination`](#editor_sunincreaseinclination)
- [`editor_sunIncreaseRotation`](#editor_sunincreaserotation)
- [`editor_technologies`](#editor_technologies)
- [`editor_terrain_types`](#editor_terrain_types)
- [`editor_text`](#editor_text)
- [`editor_toolset`](#editor_toolset)
- [`editor_trackAddWaypoint`](#editor_trackaddwaypoint)
- [`editor_trackClear`](#editor_trackclear)
- [`editor_trackCopy`](#editor_trackcopy)
- [`editor_trackEditWaypoint`](#editor_trackeditwaypoint)
- [`editor_trackInsert`](#editor_trackinsert)
- [`editor_trackMove`](#editor_trackmove)
- [`editor_trackPause`](#editor_trackpause)
- [`editor_trackPlay`](#editor_trackplay)
- [`editor_trackRemove`](#editor_trackremove)
- [`editor_trackRemoveWaypoint`](#editor_trackremovewaypoint)
- [`editor_trackStepBackward`](#editor_trackstepbackward)
- [`editor_trackStepForward`](#editor_trackstepforward)
- [`editor_trackStop`](#editor_trackstop)
- [`editor_trackToggleShow`](#editor_tracktoggleshow)
- [`editor_trackWaypointMove`](#editor_trackwaypointmove)
- [`editor_triggers`](#editor_triggers)
- [`editor_uiAddChatNotification`](#editor_uiaddchatnotification)
- [`editor_uiAddSelectNumberGroup`](#editor_uiaddselectnumbergroup)
- [`editor_uiAddSelectionButtonDown`](#editor_uiaddselectionbuttondown)
- [`editor_uiAddSelectionButtonUp`](#editor_uiaddselectionbuttonup)
- [`editor_uiAgeUpFromAnywhere`](#editor_uiageupfromanywhere)
- [`editor_uiApplyLightingSet`](#editor_uiapplylightingset)
- [`editor_uiAutoScoutSelectedUnit`](#editor_uiautoscoutselectedunit)
- [`editor_uiBeginClickDrag`](#editor_uibeginclickdrag)
- [`editor_uiBuildAtPointer`](#editor_uibuildatpointer)
- [`editor_uiBuildAtSite`](#editor_uibuildatsite)
- [`editor_uiBuildMode`](#editor_uibuildmode)
- [`editor_uiBuildWallAtPointer`](#editor_uibuildwallatpointer)
- [`editor_uiCameraControl`](#editor_uicameracontrol)
- [`editor_uiCancelRadialMenu`](#editor_uicancelradialmenu)
- [`editor_uiCancelRadialMenuLT`](#editor_uicancelradialmenult)
- [`editor_uiCancelRadialMenuReleased`](#editor_uicancelradialmenureleased)
- [`editor_uiCancelSelectedBuilding`](#editor_uicancelselectedbuilding)
- [`editor_uiCashInFavorBonus`](#editor_uicashinfavorbonus)
- [`editor_uiCenterPointer`](#editor_uicenterpointer)
- [`editor_uiChangeBrushSize`](#editor_uichangebrushsize)
- [`editor_uiChangeBrushSizePercent`](#editor_uichangebrushsizepercent)
- [`editor_uiChangeBrushType`](#editor_uichangebrushtype)
- [`editor_uiChangeElevationToSample`](#editor_uichangeelevationtosample)
- [`editor_uiChangeGizmoType`](#editor_uichangegizmotype)
- [`editor_uiChatDisplayModeToHistory`](#editor_uichatdisplaymodetohistory)
- [`editor_uiChatDisplayModeToRecent`](#editor_uichatdisplaymodetorecent)
- [`editor_uiChatDisplayModeToggle`](#editor_uichatdisplaymodetoggle)
- [`editor_uiChatScrollBack`](#editor_uichatscrollback)
- [`editor_uiChatScrollForward`](#editor_uichatscrollforward)
- [`editor_uiClearChat`](#editor_uiclearchat)
- [`editor_uiClearCursor`](#editor_uiclearcursor)
- [`editor_uiClearGatherPoint`](#editor_uicleargatherpoint)
- [`editor_uiClearMenu`](#editor_uiclearmenu)
- [`editor_uiClearNumberGroup`](#editor_uiclearnumbergroup)
- [`editor_uiClearOverrideCameras`](#editor_uiclearoverridecameras)
- [`editor_uiClearSelection`](#editor_uiclearselection)
- [`editor_uiClearTribute`](#editor_uicleartribute)
- [`editor_uiCloseControlsPopup`](#editor_uiclosecontrolspopup)
- [`editor_uiCloseDialog`](#editor_uiclosedialog)
- [`editor_uiCloseFieldSet`](#editor_uiclosefieldset)
- [`editor_uiCloseRadialMenu`](#editor_uicloseradialmenu)
- [`editor_uiCloseRadialMenuAndPause`](#editor_uicloseradialmenuandpause)
- [`editor_uiConfirmAndLoadQuickSave`](#editor_uiconfirmandloadquicksave)
- [`editor_uiControlGroupsPress`](#editor_uicontrolgroupspress)
- [`editor_uiControlGroupsRelease`](#editor_uicontrolgroupsrelease)
- [`editor_uiControlSelectionButtonDown`](#editor_uicontrolselectionbuttondown)
- [`editor_uiControlSelectionButtonUp`](#editor_uicontrolselectionbuttonup)
- [`editor_uiConvertUnits`](#editor_uiconvertunits)
- [`editor_uiCopyToClipboard`](#editor_uicopytoclipboard)
- [`editor_uiCoverTerrainWithWater`](#editor_uicoverterrainwithwater)
- [`editor_uiCreateNumberGroup`](#editor_uicreatenumbergroup)
- [`editor_uiCycleCurrentActivate`](#editor_uicyclecurrentactivate)
- [`editor_uiCycleGadget`](#editor_uicyclegadget)
- [`editor_uiDPadPrequeueDown`](#editor_uidpadprequeuedown)
- [`editor_uiDPadPrequeueLeft`](#editor_uidpadprequeueleft)
- [`editor_uiDPadPrequeueRight`](#editor_uidpadprequeueright)
- [`editor_uiDPadPrequeueUp`](#editor_uidpadprequeueup)
- [`editor_uiDecPlaceVariation`](#editor_uidecplacevariation)
- [`editor_uiDeleteAllSelectedUnits`](#editor_uideleteallselectedunits)
- [`editor_uiDeleteCameraStartLoc`](#editor_uideletecamerastartloc)
- [`editor_uiDeleteSelectedUnit`](#editor_uideleteselectedunit)
- [`editor_uiDeleteUnits`](#editor_uideleteunits)
- [`editor_uiDoubleClickDeselect`](#editor_uidoubleclickdeselect)
- [`editor_uiDoubleClickSelect`](#editor_uidoubleclickselect)
- [`editor_uiDumpAllUnitHotKeyMappings`](#editor_uidumpallunithotkeymappings)
- [`editor_uiDumpKeyMappings`](#editor_uidumpkeymappings)
- [`editor_uiDumpUnmappedKeys`](#editor_uidumpunmappedkeys)
- [`editor_uiEjectAtPointer`](#editor_uiejectatpointer)
- [`editor_uiEjectBackToWork`](#editor_uiejectbacktowork)
- [`editor_uiEjectFromAllMapControlBuildings`](#editor_uiejectfromallmapcontrolbuildings)
- [`editor_uiEjectGarrisonedUnits`](#editor_uiejectgarrisonedunits)
- [`editor_uiEmpowerAtPointer`](#editor_uiempoweratpointer)
- [`editor_uiEnterContext`](#editor_uientercontext)
- [`editor_uiEnterGameMenuModeIfNotResigned`](#editor_uientergamemenumodeifnotresigned)
- [`editor_uiErase`](#editor_uierase)
- [`editor_uiExportGrouping`](#editor_uiexportgrouping)
- [`editor_uiExportTriggers`](#editor_uiexporttriggers)
- [`editor_uiFavorStashBuySelectedItem`](#editor_uifavorstashbuyselecteditem)
- [`editor_uiFavorStashNextItem`](#editor_uifavorstashnextitem)
- [`editor_uiFavorStashPreviousItem`](#editor_uifavorstashpreviousitem)
- [`editor_uiFilterTerrainSelection`](#editor_uifilterterrainselection)
- [`editor_uiFindAllOfSelectedType`](#editor_uifindallofselectedtype)
- [`editor_uiFindAllOfTwoTypes`](#editor_uifindalloftwotypes)
- [`editor_uiFindAllOfTwoTypesAnd`](#editor_uifindalloftwotypesand)
- [`editor_uiFindAllOfTwoTypesOnScreen`](#editor_uifindalloftwotypesonscreen)
- [`editor_uiFindAllOfTwoTypesOnScreenAnd`](#editor_uifindalloftwotypesonscreenand)
- [`editor_uiFindAllOfType`](#editor_uifindalloftype)
- [`editor_uiFindAllOfTypeIdle`](#editor_uifindalloftypeidle)
- [`editor_uiFindAllOfTypeIdle2`](#editor_uifindalloftypeidle2)
- [`editor_uiFindAllOfTypeOnScreen`](#editor_uifindalloftypeonscreen)
- [`editor_uiFindAllOfTypeOnScreenIdle`](#editor_uifindalloftypeonscreenidle)
- [`editor_uiFindAllOfTypes`](#editor_uifindalloftypes)
- [`editor_uiFindAllStealthUnits`](#editor_uifindallstealthunits)
- [`editor_uiFindAnyOfTypes`](#editor_uifindanyoftypes)
- [`editor_uiFindGatherersNotGathering`](#editor_uifindgatherersnotgathering)
- [`editor_uiFindIdleOrAnyType`](#editor_uifindidleoranytype)
- [`editor_uiFindIdleType`](#editor_uifindidletype)
- [`editor_uiFindIdleType2`](#editor_uifindidletype2)
- [`editor_uiFindKeyMapping`](#editor_uifindkeymapping)
- [`editor_uiFindMenuPress`](#editor_uifindmenupress)
- [`editor_uiFindMenuRelease`](#editor_uifindmenurelease)
- [`editor_uiFindNextIdleGroupOfType`](#editor_uifindnextidlegroupoftype)
- [`editor_uiFindNextIdleGroupOfType2`](#editor_uifindnextidlegroupoftype2)
- [`editor_uiFindResourceGatherers`](#editor_uifindresourcegatherers)
- [`editor_uiFindTownBellTC`](#editor_uifindtownbelltc)
- [`editor_uiFindTwoTypes`](#editor_uifindtwotypes)
- [`editor_uiFindTwoTypesAnd`](#editor_uifindtwotypesand)
- [`editor_uiFindType`](#editor_uifindtype)
- [`editor_uiFlareAtPointer`](#editor_uiflareatpointer)
- [`editor_uiFlareAtUnit`](#editor_uiflareatunit)
- [`editor_uiFlattenTerrainSelection`](#editor_uiflattenterrainselection)
- [`editor_uiFrontendMPRankedSetupCancel`](#editor_uifrontendmprankedsetupcancel)
- [`editor_uiGameSpeedToggle`](#editor_uigamespeedtoggle)
- [`editor_uiGamepadCycleScoreboardView`](#editor_uigamepadcyclescoreboardview)
- [`editor_uiGamepadReplayCycleMapVis`](#editor_uigamepadreplaycyclemapvis)
- [`editor_uiGamepadReplayDPadDown`](#editor_uigamepadreplaydpaddown)
- [`editor_uiGamepadReplayDPadLeft`](#editor_uigamepadreplaydpadleft)
- [`editor_uiGamepadReplayDPadRight`](#editor_uigamepadreplaydpadright)
- [`editor_uiGamepadReplayDPadUp`](#editor_uigamepadreplaydpadup)
- [`editor_uiGamepadReplayPlayerViewMenuPress`](#editor_uigamepadreplayplayerviewmenupress)
- [`editor_uiGamepadReplayPlayerViewMenuRelease`](#editor_uigamepadreplayplayerviewmenurelease)
- [`editor_uiGamepadReplayRestart`](#editor_uigamepadreplayrestart)
- [`editor_uiGamepadReplaySwitchResourceView`](#editor_uigamepadreplayswitchresourceview)
- [`editor_uiGarrisonToPointer`](#editor_uigarrisontopointer)
- [`editor_uiGodPowersPress`](#editor_uigodpowerspress)
- [`editor_uiGuardAtPointer`](#editor_uiguardatpointer)
- [`editor_uiHandleIdleBanner`](#editor_uihandleidlebanner)
- [`editor_uiHandleTrainingPanelAction`](#editor_uihandletrainingpanelaction)
- [`editor_uiHandleUserTab`](#editor_uihandleusertab)
- [`editor_uiHideCursor`](#editor_uihidecursor)
- [`editor_uiHotkeyToggleObjectivesDialog`](#editor_uihotkeytoggleobjectivesdialog)
- [`editor_uiIgnoreNextKey`](#editor_uiignorenextkey)
- [`editor_uiImportTriggers`](#editor_uiimporttriggers)
- [`editor_uiIncPlaceVariation`](#editor_uiincplacevariation)
- [`editor_uiLTModifierBeginHold`](#editor_uiltmodifierbeginhold)
- [`editor_uiLTModifierEndHold`](#editor_uiltmodifierendhold)
- [`editor_uiLTSelectRadialMenuEntry`](#editor_uiltselectradialmenuentry)
- [`editor_uiLeaveContext`](#editor_uileavecontext)
- [`editor_uiLeaveModeOnUnshift`](#editor_uileavemodeonunshift)
- [`editor_uiLoadTriggers`](#editor_uiloadtriggers)
- [`editor_uiLookAtAndSelectUnit`](#editor_uilookatandselectunit)
- [`editor_uiLookAtBattle`](#editor_uilookatbattle)
- [`editor_uiLookAtNumberGroup`](#editor_uilookatnumbergroup)
- [`editor_uiLookAtProto`](#editor_uilookatproto)
- [`editor_uiLookAtSelection`](#editor_uilookatselection)
- [`editor_uiLookAtUnit`](#editor_uilookatunit)
- [`editor_uiLowerElevation`](#editor_uilowerelevation)
- [`editor_uiLowerTerrainSelection`](#editor_uilowerterrainselection)
- [`editor_uiMessageBox`](#editor_uimessagebox)
- [`editor_uiMinimapBack`](#editor_uiminimapback)
- [`editor_uiMinimapDPadDown`](#editor_uiminimapdpaddown)
- [`editor_uiMinimapDPadDownReleased`](#editor_uiminimapdpaddownreleased)
- [`editor_uiMinimapDPadLeft`](#editor_uiminimapdpadleft)
- [`editor_uiMinimapDPadRight`](#editor_uiminimapdpadright)
- [`editor_uiMinimapDPadUp`](#editor_uiminimapdpadup)
- [`editor_uiMinimapSnapCamera`](#editor_uiminimapsnapcamera)
- [`editor_uiMinimapSnapCameraReleased`](#editor_uiminimapsnapcamerareleased)
- [`editor_uiMinorGodUI`](#editor_uiminorgodui)
- [`editor_uiMinorGodUIInSelected`](#editor_uiminorgoduiinselected)
- [`editor_uiMoveAllMilitaryAtPointer`](#editor_uimoveallmilitaryatpointer)
- [`editor_uiMoveAllUnitOfTypeToUnitLocation`](#editor_uimoveallunitoftypetounitlocation)
- [`editor_uiMoveIdleUnitsToPointer`](#editor_uimoveidleunitstopointer)
- [`editor_uiMoveSelectionAddButtonDown`](#editor_uimoveselectionaddbuttondown)
- [`editor_uiMoveSelectionAddButtonUp`](#editor_uimoveselectionaddbuttonup)
- [`editor_uiMoveSelectionButtonDown`](#editor_uimoveselectionbuttondown)
- [`editor_uiMoveSelectionButtonUp`](#editor_uimoveselectionbuttonup)
- [`editor_uiMoveUnitBackward`](#editor_uimoveunitbackward)
- [`editor_uiMoveUnitDown`](#editor_uimoveunitdown)
- [`editor_uiMoveUnitForward`](#editor_uimoveunitforward)
- [`editor_uiMoveUnitLeft`](#editor_uimoveunitleft)
- [`editor_uiMoveUnitOfNameToUnitLocation`](#editor_uimoveunitofnametounitlocation)
- [`editor_uiMoveUnitOfTypeToUnitLocation`](#editor_uimoveunitoftypetounitlocation)
- [`editor_uiMoveUnitRight`](#editor_uimoveunitright)
- [`editor_uiMoveUnitUp`](#editor_uimoveunitup)
- [`editor_uiMoveUnitsToPointer`](#editor_uimoveunitstopointer)
- [`editor_uiNewScenario`](#editor_uinewscenario)
- [`editor_uiNorseBuildAtSite`](#editor_uinorsebuildatsite)
- [`editor_uiOpenChatMenu`](#editor_uiopenchatmenu)
- [`editor_uiOpenDiplomacyMenu`](#editor_uiopendiplomacymenu)
- [`editor_uiOpenGameSettings`](#editor_uiopengamesettings)
- [`editor_uiOpenRecordGameBrowser`](#editor_uiopenrecordgamebrowser)
- [`editor_uiOpenScenarioBrowser`](#editor_uiopenscenariobrowser)
- [`editor_uiPaint`](#editor_uipaint)
- [`editor_uiPaintCliff`](#editor_uipaintcliff)
- [`editor_uiPaintForest`](#editor_uipaintforest)
- [`editor_uiPaintTerrainOverlay`](#editor_uipaintterrainoverlay)
- [`editor_uiPaintTerrainToSample`](#editor_uipaintterraintosample)
- [`editor_uiPaintWater`](#editor_uipaintwater)
- [`editor_uiPaintWaterArea`](#editor_uipaintwaterarea)
- [`editor_uiPaintWaterObjects`](#editor_uipaintwaterobjects)
- [`editor_uiPasteFromClipboard`](#editor_uipastefromclipboard)
- [`editor_uiPatrolAtPointer`](#editor_uipatrolatpointer)
- [`editor_uiPayFoundation`](#editor_uipayfoundation)
- [`editor_uiPeekBuildingChainRadius`](#editor_uipeekbuildingchainradius)
- [`editor_uiPhotoModeDialogFocusEnter`](#editor_uiphotomodedialogfocusenter)
- [`editor_uiPhotoModeDialogFocusLeave`](#editor_uiphotomodedialogfocusleave)
- [`editor_uiPhotoModeMouseZoom`](#editor_uiphotomodemousezoom)
- [`editor_uiPhotoModeQuit`](#editor_uiphotomodequit)
- [`editor_uiPhotoModeResetCamera`](#editor_uiphotomoderesetcamera)
- [`editor_uiPhotoModeTakeScreenshot`](#editor_uiphotomodetakescreenshot)
- [`editor_uiPhotoModeTogglePhotoUI`](#editor_uiphotomodetogglephotoui)
- [`editor_uiPitchUnitDown`](#editor_uipitchunitdown)
- [`editor_uiPitchUnitUp`](#editor_uipitchunitup)
- [`editor_uiPlaceAtPointer`](#editor_uiplaceatpointer)
- [`editor_uiQuickSelectDown`](#editor_uiquickselectdown)
- [`editor_uiQuickSelectLeft`](#editor_uiquickselectleft)
- [`editor_uiQuickSelectRight`](#editor_uiquickselectright)
- [`editor_uiQuickSelectUp`](#editor_uiquickselectup)
- [`editor_uiRadialMenuAltAction`](#editor_uiradialmenualtaction)
- [`editor_uiRadialMenuChangePage`](#editor_uiradialmenuchangepage)
- [`editor_uiRadialMenuCloseCommandMenu`](#editor_uiradialmenuclosecommandmenu)
- [`editor_uiRadialMenuCloseControlGroupMenu`](#editor_uiradialmenuclosecontrolgroupmenu)
- [`editor_uiRadialMenuCloseFindMenu`](#editor_uiradialmenuclosefindmenu)
- [`editor_uiRadialMenuCloseGodPowerMenu`](#editor_uiradialmenuclosegodpowermenu)
- [`editor_uiRadialMenuCloseVPSMenu`](#editor_uiradialmenuclosevpsmenu)
- [`editor_uiRadialMenuThumbAction`](#editor_uiradialmenuthumbaction)
- [`editor_uiRadialMenuViewAction`](#editor_uiradialmenuviewaction)
- [`editor_uiRadialMenuXAxis`](#editor_uiradialmenuxaxis)
- [`editor_uiRadialMenuYAxis`](#editor_uiradialmenuyaxis)
- [`editor_uiRaiseElevation`](#editor_uiraiseelevation)
- [`editor_uiRaiseTerrainSelection`](#editor_uiraiseterrainselection)
- [`editor_uiRecalcVariation`](#editor_uirecalcvariation)
- [`editor_uiRefreshCommandPanel`](#editor_uirefreshcommandpanel)
- [`editor_uiRefreshEditorMenu`](#editor_uirefresheditormenu)
- [`editor_uiReleaseDPadPrequeueDown`](#editor_uireleasedpadprequeuedown)
- [`editor_uiReleaseDPadPrequeueLeft`](#editor_uireleasedpadprequeueleft)
- [`editor_uiReleaseDPadPrequeueRight`](#editor_uireleasedpadprequeueright)
- [`editor_uiReleaseDPadPrequeueUp`](#editor_uireleasedpadprequeueup)
- [`editor_uiReleaseDownKeys`](#editor_uireleasedownkeys)
- [`editor_uiReleaseQuickSelectDown`](#editor_uireleasequickselectdown)
- [`editor_uiReleaseQuickSelectLeft`](#editor_uireleasequickselectleft)
- [`editor_uiReleaseQuickSelectRight`](#editor_uireleasequickselectright)
- [`editor_uiReleaseQuickSelectUp`](#editor_uireleasequickselectup)
- [`editor_uiReleaseRadialMenuAltAction`](#editor_uireleaseradialmenualtaction)
- [`editor_uiReleaseRadialMenuEntry`](#editor_uireleaseradialmenuentry)
- [`editor_uiRemoveFromAnyNumberGroup`](#editor_uiremovefromanynumbergroup)
- [`editor_uiRemoveSelectedUnit`](#editor_uiremoveselectedunit)
- [`editor_uiRemoveSelectionButtonDown`](#editor_uiremoveselectionbuttondown)
- [`editor_uiRemoveSelectionButtonUp`](#editor_uiremoveselectionbuttonup)
- [`editor_uiRepairAtPointer`](#editor_uirepairatpointer)
- [`editor_uiResetCameraOrientation`](#editor_uiresetcameraorientation)
- [`editor_uiResetInGameCameraRotation`](#editor_uiresetingamecamerarotation)
- [`editor_uiResetInGameCameraZoom`](#editor_uiresetingamecamerazoom)
- [`editor_uiRollUnitLeft`](#editor_uirollunitleft)
- [`editor_uiRollUnitRight`](#editor_uirollunitright)
- [`editor_uiRotateClipboard`](#editor_uirotateclipboard)
- [`editor_uiRotateSelection`](#editor_uirotateselection)
- [`editor_uiRotateWaterLeft`](#editor_uirotatewaterleft)
- [`editor_uiRotateWaterRight`](#editor_uirotatewaterright)
- [`editor_uiRoughen`](#editor_uiroughen)
- [`editor_uiSampleCliffElevationAtPointer`](#editor_uisamplecliffelevationatpointer)
- [`editor_uiSampleElevationAtPointer`](#editor_uisampleelevationatpointer)
- [`editor_uiSampleTerrainAtPointer`](#editor_uisampleterrainatpointer)
- [`editor_uiSampleWaterAtPointer`](#editor_uisamplewateratpointer)
- [`editor_uiSaveAsScenarioBrowser`](#editor_uisaveasscenariobrowser)
- [`editor_uiSaveBuiltInPrefab`](#editor_uisavebuiltinprefab)
- [`editor_uiSaveScenarioBrowser`](#editor_uisavescenariobrowser)
- [`editor_uiSaveScenarioPrompt`](#editor_uisavescenarioprompt)
- [`editor_uiSaveTriggers`](#editor_uisavetriggers)
- [`editor_uiSaveUserPrefab`](#editor_uisaveuserprefab)
- [`editor_uiScenarioEndReplay`](#editor_uiscenarioendreplay)
- [`editor_uiScenarioLoad`](#editor_uiscenarioload)
- [`editor_uiScenarioTestMainMenu`](#editor_uiscenariotestmainmenu)
- [`editor_uiScrollBrushSize`](#editor_uiscrollbrushsize)
- [`editor_uiScrollCliffHeight`](#editor_uiscrollcliffheight)
- [`editor_uiSeekShelter`](#editor_uiseekshelter)
- [`editor_uiSelectAgeUpGod`](#editor_uiselectageupgod)
- [`editor_uiSelectCameraFocusedUnit`](#editor_uiselectcamerafocusedunit)
- [`editor_uiSelectLocation`](#editor_uiselectlocation)
- [`editor_uiSelectNextUnitStack`](#editor_uiselectnextunitstack)
- [`editor_uiSelectNumberGroup`](#editor_uiselectnumbergroup)
- [`editor_uiSelectRadialMenuEntry`](#editor_uiselectradialmenuentry)
- [`editor_uiSelectTransportUnit`](#editor_uiselecttransportunit)
- [`editor_uiSelectType`](#editor_uiselecttype)
- [`editor_uiSelectWaterAtPointer`](#editor_uiselectwateratpointer)
- [`editor_uiSelectionButtonDown`](#editor_uiselectionbuttondown)
- [`editor_uiSelectionButtonUp`](#editor_uiselectionbuttonup)
- [`editor_uiSendTribute`](#editor_uisendtribute)
- [`editor_uiSetBrushType`](#editor_uisetbrushtype)
- [`editor_uiSetBuildingPlacementRender`](#editor_uisetbuildingplacementrender)
- [`editor_uiSetCameraStartLoc`](#editor_uisetcamerastartloc)
- [`editor_uiSetClickDragParams`](#editor_uisetclickdragparams)
- [`editor_uiSetCliffType`](#editor_uisetclifftype)
- [`editor_uiSetCliffTypeNum`](#editor_uisetclifftypenum)
- [`editor_uiSetClipboardRotation`](#editor_uisetclipboardrotation)
- [`editor_uiSetForestType`](#editor_uisetforesttype)
- [`editor_uiSetForestTypeNum`](#editor_uisetforesttypenum)
- [`editor_uiSetGatherPointAtGamepadPointer`](#editor_uisetgatherpointatgamepadpointer)
- [`editor_uiSetGatherPointAtGamepadPointerOfNearbyUnitTypes`](#editor_uisetgatherpointatgamepadpointerofnearbyunittypes)
- [`editor_uiSetGatherPointAtPointer`](#editor_uisetgatherpointatpointer)
- [`editor_uiSetGatherPointAtPointerOfNearbyUnitTypes`](#editor_uisetgatherpointatpointerofnearbyunittypes)
- [`editor_uiSetGatherPointAtPointerOfNearbyUnitTypesToUnit`](#editor_uisetgatherpointatpointerofnearbyunittypestounit)
- [`editor_uiSetKBArmyRender`](#editor_uisetkbarmyrender)
- [`editor_uiSetKBAttackRouteRender`](#editor_uisetkbattackrouterender)
- [`editor_uiSetKBResourceRender`](#editor_uisetkbresourcerender)
- [`editor_uiSetNearestUnitNamedToBuild`](#editor_uisetnearestunitnamedtobuild)
- [`editor_uiSetPlacementPlayer`](#editor_uisetplacementplayer)
- [`editor_uiSetProtoCursor`](#editor_uisetprotocursor)
- [`editor_uiSetProtoCursorID`](#editor_uisetprotocursorid)
- [`editor_uiSetProtoID`](#editor_uisetprotoid)
- [`editor_uiSetTerrainDetailPaintMode`](#editor_uisetterraindetailpaintmode)
- [`editor_uiSetUnitBar`](#editor_uisetunitbar)
- [`editor_uiSetWaterType`](#editor_uisetwatertype)
- [`editor_uiSetWaterTypeNum`](#editor_uisetwatertypenum)
- [`editor_uiShowAIDebugInfoArea`](#editor_uishowaidebuginfoarea)
- [`editor_uiShowAIDebugInfoAreaGroup`](#editor_uishowaidebuginfoareagroup)
- [`editor_uiShowAIDebugInfoAttackRoute`](#editor_uishowaidebuginfoattackroute)
- [`editor_uiShowAIDebugInfoBase`](#editor_uishowaidebuginfobase)
- [`editor_uiShowAIDebugInfoEscrow`](#editor_uishowaidebuginfoescrow)
- [`editor_uiShowAIDebugInfoKBArmy`](#editor_uishowaidebuginfokbarmy)
- [`editor_uiShowAIDebugInfoKBResource`](#editor_uishowaidebuginfokbresource)
- [`editor_uiShowAIDebugInfoKBUnit`](#editor_uishowaidebuginfokbunit)
- [`editor_uiShowAIDebugInfoKBUnitPick`](#editor_uishowaidebuginfokbunitpick)
- [`editor_uiShowAIDebugInfoPlacement`](#editor_uishowaidebuginfoplacement)
- [`editor_uiShowAIDebugInfoPlan`](#editor_uishowaidebuginfoplan)
- [`editor_uiShowCameraStartLoc`](#editor_uishowcamerastartloc)
- [`editor_uiShowChatWindow`](#editor_uishowchatwindow)
- [`editor_uiShowEconomicUI`](#editor_uishoweconomicui)
- [`editor_uiShowFavorBonusUI`](#editor_uishowfavorbonusui)
- [`editor_uiShowHelpPopupHistoryTopic`](#editor_uishowhelppopuphistorytopic)
- [`editor_uiShowMilitaryUI`](#editor_uishowmilitaryui)
- [`editor_uiShowNextAIError`](#editor_uishownextaierror)
- [`editor_uiShowPreviousAIError`](#editor_uishowpreviousaierror)
- [`editor_uiSmooth`](#editor_uismooth)
- [`editor_uiSocketBuild`](#editor_uisocketbuild)
- [`editor_uiSpecialPowerAtPointer`](#editor_uispecialpoweratpointer)
- [`editor_uiSpectatorFlipPlayers`](#editor_uispectatorflipplayers)
- [`editor_uiSpewDownKeys`](#editor_uispewdownkeys)
- [`editor_uiStartScenarioTest`](#editor_uistartscenariotest)
- [`editor_uiStickRotatePlacedUnit`](#editor_uistickrotateplacedunit)
- [`editor_uiStopScenarioTest`](#editor_uistopscenariotest)
- [`editor_uiStopSelectedUnits`](#editor_uistopselectedunits)
- [`editor_uiSwitchControlsPopup`](#editor_uiswitchcontrolspopup)
- [`editor_uiSwitchFromChatToDiplomacy`](#editor_uiswitchfromchattodiplomacy)
- [`editor_uiSwitchFromDiplomacyToChat`](#editor_uiswitchfromdiplomacytochat)
- [`editor_uiSwitchToGamepadMode`](#editor_uiswitchtogamepadmode)
- [`editor_uiSwitchToMouseMode`](#editor_uiswitchtomousemode)
- [`editor_uiTaskAllUnitOfTypeToUnitLocation`](#editor_uitaskallunitoftypetounitlocation)
- [`editor_uiTerrainSelection`](#editor_uiterrainselection)
- [`editor_uiToggleBrushMask`](#editor_uitogglebrushmask)
- [`editor_uiToggleDiplomacyDialog`](#editor_uitogglediplomacydialog)
- [`editor_uiToggleGame`](#editor_uitogglegame)
- [`editor_uiToggleGamepadMinimap`](#editor_uitogglegamepadminimap)
- [`editor_uiToggleGarrisonMode`](#editor_uitogglegarrisonmode)
- [`editor_uiToggleGizmoSnapping`](#editor_uitogglegizmosnapping)
- [`editor_uiToggleMinimapEnlarged`](#editor_uitoggleminimapenlarged)
- [`editor_uiToggleSelectionButton`](#editor_uitoggleselectionbutton)
- [`editor_uiToggleTerrainPasteMode`](#editor_uitoggleterrainpastemode)
- [`editor_uiToggleUI`](#editor_uitoggleui)
- [`editor_uiToggleUnitFollowCamera`](#editor_uitoggleunitfollowcamera)
- [`editor_uiToggleUnitPerspectiveCamera`](#editor_uitoggleunitperspectivecamera)
- [`editor_uiTransformSelectedUnit`](#editor_uitransformselectedunit)
- [`editor_uiTriggerResetParameters`](#editor_uitriggerresetparameters)
- [`editor_uiTriggerResetSounds`](#editor_uitriggerresetsounds)
- [`editor_uiTriggerSelectLocation`](#editor_uitriggerselectlocation)
- [`editor_uiUnSelectWater`](#editor_uiunselectwater)
- [`editor_uiUnbuildSelectedUnit`](#editor_uiunbuildselectedunit)
- [`editor_uiUnbuildSelectedUnitAtPointer`](#editor_uiunbuildselectedunitatpointer)
- [`editor_uiUniformLowerElevation`](#editor_uiuniformlowerelevation)
- [`editor_uiUniformRaiseElevation`](#editor_uiuniformraiseelevation)
- [`editor_uiUnitCommandsMenuPress`](#editor_uiunitcommandsmenupress)
- [`editor_uiUnitCommandsMenuRelease`](#editor_uiunitcommandsmenurelease)
- [`editor_uiVillagerPrioritiesPress`](#editor_uivillagerprioritiespress)
- [`editor_uiVillagerPrioritiesRelease`](#editor_uivillagerprioritiesrelease)
- [`editor_uiVillagerReturnResources`](#editor_uivillagerreturnresources)
- [`editor_uiWheelRotate`](#editor_uiwheelrotate)
- [`editor_uiWheelRotateCamera`](#editor_uiwheelrotatecamera)
- [`editor_uiWheelRotatePlacedUnit`](#editor_uiwheelrotateplacedunit)
- [`editor_uiWorkAtPointer`](#editor_uiworkatpointer)
- [`editor_uiYawUnitLeft`](#editor_uiyawunitleft)
- [`editor_uiYawUnitRight`](#editor_uiyawunitright)
- [`editor_uiZoomToMinimapEvent`](#editor_uizoomtominimapevent)
- [`editor_uiZoomToMinimapEvent2`](#editor_uizoomtominimapevent2)
- [`editor_uiZoomToProto`](#editor_uizoomtoproto)
- [`editor_undo`](#editor_undo)
- [`editor_unitReturnToWork`](#editor_unitreturntowork)
- [`editor_unitSetStance`](#editor_unitsetstance)
- [`editor_unitTownBell`](#editor_unittownbell)
- [`editor_units`](#editor_units)
- [`editor_validate_scenario`](#editor_validate_scenario)
- [`editor_water_types`](#editor_water_types)

## Helpers

### `editor_batch`

Run 1..32 existing tool calls sequentially in one connection. Schema/native-confirmation preflight; every step retains guards. Optional delayMs before a step for queued UI effects. Stops on first error; earlier effects remain (NOT atomic), no retries. Put screenshot last; inspect native acknowledgements independently.

**Required arguments:** `steps`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "steps": {
      "type": "array",
      "minItems": 1,
      "maxItems": 32,
      "items": {
        "type": "object",
        "properties": {
          "name": {
            "type": "string"
          },
          "arguments": {
            "type": "object"
          },
          "delayMs": {
            "type": "integer",
            "minimum": 0,
            "maximum": 2000
          }
        },
        "required": [
          "name"
        ],
        "additionalProperties": false
      }
    }
  },
  "required": [
    "steps"
  ],
  "additionalProperties": false
}
```

### `editor_catalog`

List generated editor command signatures and coverage limits; optional name filter.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": true,
  "openWorldHint": false,
  "readOnlyHint": true
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "filter": {
      "type": "string"
    }
  },
  "required": [],
  "additionalProperties": false
}
```

### `editor_dependencies`

Explain exact proto's static train/build links, positive Enable/CreateUnit/replacement tech effects/raw prerequisites, god starting units and shortest active/obtainable god-to-tech paths. Reuses cached shipped catalogs, no game. Abstract unit-type targets included; paths are potential, NOT evaluated prerequisites/exclusions or current-player trainability. Bounded relations: offset>=0, limit1..200/default50.

**Required arguments:** `proto`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": true,
  "openWorldHint": false,
  "readOnlyHint": true
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "proto": {
      "type": "string"
    },
    "offset": {
      "type": "integer",
      "minimum": 0
    },
    "limit": {
      "type": "integer",
      "minimum": 1,
      "maximum": 200
    }
  },
  "required": [
    "proto"
  ],
  "additionalProperties": false
}
```

### `editor_focus`

Focus/restore game window. Editor only.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": true,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_god_powers`

List shipped god-power definitions, types, costs, placement and created units. Potential culture links from tech grants; source-file variants retained. XML detail has full settings. Read-only, no running game required. name=exact identifier; filter=name/label substring. offset>=0, limit=1..200 (default 50); includeDefinition=false by default. Missing/stale data: --generate generated, then restart MCP.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": true,
  "openWorldHint": false,
  "readOnlyHint": true
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "filter": {
      "type": "string",
      "description": "Case-insensitive substring of name or label/string ID."
    },
    "name": {
      "type": "string",
      "description": "Exact source identifier (case-insensitive)."
    },
    "offset": {
      "type": "integer",
      "minimum": 0
    },
    "limit": {
      "type": "integer",
      "minimum": 1,
      "maximum": 200
    },
    "includeDefinition": {
      "type": "boolean",
      "description": "Include original XML definition; default false."
    },
    "pantheon": {
      "type": "string",
      "description": "Optional culture association (not current-player availability)."
    }
  },
  "required": [],
  "additionalProperties": false
}
```

### `editor_gods`

List major/minor gods and culture associations, starting units, age techs and direct unlock effects. Minor names are canonical age-tech IDs (e.g. ClassicalAgeAthena); labels are source string IDs, not translations. Read-only, no running game required. name=exact identifier; filter=name/label substring. offset>=0, limit=1..200 (default 50); includeDefinition=false by default. Missing/stale data: --generate generated, then restart MCP.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": true,
  "openWorldHint": false,
  "readOnlyHint": true
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "filter": {
      "type": "string",
      "description": "Case-insensitive substring of name or label/string ID."
    },
    "name": {
      "type": "string",
      "description": "Exact source identifier (case-insensitive)."
    },
    "offset": {
      "type": "integer",
      "minimum": 0
    },
    "limit": {
      "type": "integer",
      "minimum": 1,
      "maximum": 200
    },
    "includeDefinition": {
      "type": "boolean",
      "description": "Include original XML definition; default false."
    },
    "pantheon": {
      "type": "string",
      "description": "Optional culture association (not current-player availability)."
    },
    "category": {
      "type": "string",
      "enum": [
        "all",
        "major",
        "minor"
      ]
    }
  },
  "required": [],
  "additionalProperties": false
}
```

### `editor_inspect_selection`

Read actual selection records and selected objects' live prototype/player/world position/current-max health. Not command acknowledgements; includes unresolved and non-unit selection kinds. Editor only; no focus/input/game calls. Requires reviewed unit+selection fields. Paging offset>=0, limit1..200/default100; changing selection refuses.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": true,
  "openWorldHint": false,
  "readOnlyHint": true
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "offset": {
      "type": "integer",
      "minimum": 0
    },
    "limit": {
      "type": "integer",
      "minimum": 1,
      "maximum": 200
    }
  },
  "required": [],
  "additionalProperties": false
}
```

### `editor_key`

Send editor key/hotkey. Letters, digits, F1..F12, ESC, ENTER, TAB, arrows, DELETE/BACKSPACE, HOME/END/PGUP/PGDN.

**Required arguments:** `key`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "key": {
      "type": "string"
    },
    "confirmDestructive": {
      "type": "boolean"
    },
    "modifiers": {
      "type": "array",
      "items": {
        "type": "string",
        "enum": [
          "CTRL",
          "SHIFT",
          "ALT"
        ]
      },
      "maxItems": 3
    }
  },
  "required": [
    "key"
  ],
  "additionalProperties": false
}
```

### `editor_map_info`

Read map tile/world dimensions, active camera and native render projection/full-resolution client viewport. Optional terrainAt [X,Z] returns native quantized node height; world [X,Y,Z] projects to client pixels/frustum visibility. screen [clientX,clientY] returns inverse ray; optional planeY gives explicit horizontal-plane intersection, not guessed terrain hit. Read-only editor access; changing/unreviewed layouts refuse.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": true,
  "openWorldHint": false,
  "readOnlyHint": true
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "terrainAt": {
      "type": "array",
      "items": {
        "type": "number",
        "minimum": -1000000,
        "maximum": 1000000
      },
      "minItems": 2,
      "maxItems": 2
    },
    "world": {
      "type": "array",
      "items": {
        "type": "number",
        "minimum": -1000000,
        "maximum": 1000000
      },
      "minItems": 3,
      "maxItems": 3
    },
    "screen": {
      "type": "array",
      "items": {
        "type": "number",
        "minimum": -1000000,
        "maximum": 1000000
      },
      "minItems": 2,
      "maxItems": 2
    },
    "planeY": {
      "type": "number",
      "minimum": -1000000,
      "maximum": 1000000
    }
  },
  "required": [],
  "additionalProperties": false
}
```

### `editor_mouse_click`

Click game-client pixel position. Covers editor controls/dialog buttons lacking a command API. button left/right/middle.

**Required arguments:** `x`, `y`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "x": {
      "type": "integer"
    },
    "y": {
      "type": "integer"
    },
    "button": {
      "type": "string"
    }
  },
  "required": [
    "x",
    "y"
  ],
  "additionalProperties": false
}
```

### `editor_mouse_drag`

Left-button stroke/selection/drag within editor client; releases button in finally. durationMs 100..5000.

**Required arguments:** `x1`, `y1`, `x2`, `y2`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "x1": {
      "type": "integer"
    },
    "y1": {
      "type": "integer"
    },
    "x2": {
      "type": "integer"
    },
    "y2": {
      "type": "integer"
    },
    "durationMs": {
      "type": "integer"
    }
  },
  "required": [
    "x1",
    "y1",
    "x2",
    "y2"
  ],
  "additionalProperties": false
}
```

### `editor_mouse_move`

Move pointer to game-client pixel position; needed for pointer-dependent native tools.

**Required arguments:** `x`, `y`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "x": {
      "type": "integer"
    },
    "y": {
      "type": "integer"
    }
  },
  "required": [
    "x",
    "y"
  ],
  "additionalProperties": false
}
```

### `editor_mouse_wheel`

Scroll/zoom at current game pointer; steps -20..20.

**Required arguments:** `steps`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "steps": {
      "type": "integer"
    }
  },
  "required": [
    "steps"
  ],
  "additionalProperties": false
}
```

### `editor_pantheon`

Get exact unit/building proto names by pantheon (e.g. greeks -> VillagerGreek, MilitaryAcademy). Case-insensitive singular/plural culture names. Generated from shipped culture/start/tech metadata, not guessed names or IDs. Potential union across gods/ages, not current-player trainability; unresolved techs reported. No game connection. Run --generate generated if missing/stale.

**Required arguments:** `pantheon`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": true,
  "openWorldHint": false,
  "readOnlyHint": true
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "pantheon": {
      "type": "string"
    }
  },
  "required": [
    "pantheon"
  ],
  "additionalProperties": false
}
```

### `editor_generate_catalog`

Generate game-data catalogs (prototypes, gods, techs, god powers, terrain/water, mixes, footprints, editor UI XML) from installed game archives with bundled CryBar. Run once per install/game update or when a tool reports metadata missing/stale; catalog tools work immediately afterwards (new action_* UI tools need MCP restart). No game connection. Skips when fresh unless force=true.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": true,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "force": {
      "type": "boolean"
    }
  },
  "required": [],
  "additionalProperties": false
}
```

### `editor_place_formation`

Bounded 1..32 objects in rows/ring, centered on x/y FULL-RESOLUTION CLIENT PIXELS; spacingPixels is NOT world distance. Rows columns default ceil(sqrt(count)); ring spacing is neighbor chord before rounding, no columns allowed. preview defaults true: pure local plan/no game. preview=false requires confirmPlacement=true, checks whole plan against actual client before mutation, reuses single placement guards/cleanup and independently observes actual new IDs. Stops on first error with partial progress/no retry/rollback/save. Camera/UI hover affect world layout.

**Required arguments:** `proto`, `shape`, `count`, `spacingPixels`, `x`, `y`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "proto": {
      "type": "string"
    },
    "player": {
      "type": "integer",
      "minimum": 0,
      "maximum": 12
    },
    "shape": {
      "type": "string",
      "enum": [
        "rows",
        "ring"
      ]
    },
    "count": {
      "type": "integer",
      "minimum": 1,
      "maximum": 32
    },
    "spacingPixels": {
      "type": "integer",
      "minimum": 1,
      "maximum": 10000
    },
    "x": {
      "type": "integer",
      "minimum": 0,
      "maximum": 65535
    },
    "y": {
      "type": "integer",
      "minimum": 0,
      "maximum": 65535
    },
    "columns": {
      "type": "integer",
      "minimum": 1,
      "maximum": 32
    },
    "preview": {
      "type": "boolean"
    },
    "confirmPlacement": {
      "type": "boolean"
    }
  },
  "required": [
    "proto",
    "shape",
    "count",
    "spacingPixels",
    "x",
    "y"
  ],
  "additionalProperties": false
}
```

### `editor_place_unit`

Native, one-shot unit placement using internal proto name and player. x/y game-client pixels default center. Cleans preview; does not save. Verify screenshot afterward.

**Required arguments:** `proto`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "proto": {
      "type": "string"
    },
    "player": {
      "type": "integer"
    },
    "x": {
      "type": "integer"
    },
    "y": {
      "type": "integer"
    }
  },
  "required": [
    "proto"
  ],
  "additionalProperties": false
}
```

### `editor_prototypes`

List exact prototypes: units, buildings, trees, resource nodes, decorations, wildlife, effects and other objects. Filter category/unitType/pantheon; base costs, stats, resources and train/build links. Flags are source data, NOT live editor placement proof. Read-only, no running game required. name=exact identifier; filter=name/label substring. offset>=0, limit=1..200 (default 50); includeDefinition=false by default. Missing/stale data: --generate generated, then restart MCP.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": true,
  "openWorldHint": false,
  "readOnlyHint": true
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "filter": {
      "type": "string",
      "description": "Case-insensitive substring of name or label/string ID."
    },
    "name": {
      "type": "string",
      "description": "Exact source identifier (case-insensitive)."
    },
    "offset": {
      "type": "integer",
      "minimum": 0
    },
    "limit": {
      "type": "integer",
      "minimum": 1,
      "maximum": 200
    },
    "includeDefinition": {
      "type": "boolean",
      "description": "Include original XML definition; default false."
    },
    "pantheon": {
      "type": "string",
      "description": "Optional culture association (not current-player availability)."
    },
    "category": {
      "type": "string",
      "enum": [
        "all",
        "units",
        "buildings",
        "trees",
        "resources",
        "objects"
      ]
    },
    "unitType": {
      "type": "string",
      "description": "Exact unittype tag, e.g. GoldResource, FishResource, Herdable, Projectile."
    }
  },
  "required": [],
  "additionalProperties": false
}
```

### `editor_save_checkpoint`

Save through native game writer to unique active-profile scenario staging file, verify stable l33t/zlib payload/decoded length, copy/hash-verify to caller-approved NEW absolute local .mythscn path. confirmWrite=true required. No overwrite option; original scenario files never silently overwritten. profileDirectory selects active scenario directory when ambiguous. Staging retained; native writer may change editor save-name/dirty state. Not semantic reload validation; never retry unknown outcomes.

**Required arguments:** `path`, `confirmWrite`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "path": {
      "type": "string"
    },
    "profileDirectory": {
      "type": "string"
    },
    "confirmWrite": {
      "type": "boolean"
    }
  },
  "required": [
    "path",
    "confirmWrite"
  ],
  "additionalProperties": false
}
```

### `editor_screenshot`

Capture foreground game client PNG. maxWidth default 1280; region [x,y,w,h] uses full-resolution client pixels, optional scale 1..4 nearest (output max 1600×1600). resolutionScale 0.1..1 (default 1) multiplies final output size (e.g. 0.6 = 60% width/height, ~36% pixels) after maxWidth/region/scale. Screenshots consume tokens: recommended to use resolutionScale <1 (e.g. 0.5-0.6) and/or small regions to save context/token cost; pixel coordinates you derive must be mapped back to full-resolution client pixels.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "maxWidth": {
      "type": "integer",
      "minimum": 320,
      "maximum": 2560
    },
    "region": {
      "type": "array",
      "minItems": 4,
      "maxItems": 4,
      "items": {
        "type": "integer"
      }
    },
    "scale": {
      "type": "integer",
      "minimum": 1,
      "maximum": 4
    },
    "resolutionScale": {
      "type": "number",
      "minimum": 0.1,
      "maximum": 1,
      "default": 1
    }
  },
  "required": [],
  "additionalProperties": false
}
```

### `editor_status`

Read current editor state, build, thread and placement selection. No input sent.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": true,
  "openWorldHint": false,
  "readOnlyHint": true
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_technologies`

List technologies/upgrades, base costs, prerequisites, effects and potential culture associations. Not current-player researchability. Read-only, no running game required. name=exact identifier; filter=name/label substring. offset>=0, limit=1..200 (default 50); includeDefinition=false by default. Missing/stale data: --generate generated, then restart MCP.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": true,
  "openWorldHint": false,
  "readOnlyHint": true
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "filter": {
      "type": "string",
      "description": "Case-insensitive substring of name or label/string ID."
    },
    "name": {
      "type": "string",
      "description": "Exact source identifier (case-insensitive)."
    },
    "offset": {
      "type": "integer",
      "minimum": 0
    },
    "limit": {
      "type": "integer",
      "minimum": 1,
      "maximum": 200
    },
    "includeDefinition": {
      "type": "boolean",
      "description": "Include original XML definition; default false."
    },
    "pantheon": {
      "type": "string",
      "description": "Optional culture association (not current-player availability)."
    }
  },
  "required": [],
  "additionalProperties": false
}
```

### `editor_terrain_types`

List terrain texture identifiers, UI labels/classes, parent terrain types (passability groups) and settings. Names retain shipped backslashes; no guessed numeric IDs. Read-only, no running game required. name=exact identifier; filter=name/label substring. offset>=0, limit=1..200 (default 50); includeDefinition=false by default. Missing/stale data: --generate generated, then restart MCP.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": true,
  "openWorldHint": false,
  "readOnlyHint": true
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "filter": {
      "type": "string",
      "description": "Case-insensitive substring of name or label/string ID."
    },
    "name": {
      "type": "string",
      "description": "Exact source identifier (case-insensitive)."
    },
    "offset": {
      "type": "integer",
      "minimum": 0
    },
    "limit": {
      "type": "integer",
      "minimum": 1,
      "maximum": 200
    },
    "includeDefinition": {
      "type": "boolean",
      "description": "Include original XML definition; default false."
    },
    "terrainType": {
      "type": "string",
      "description": "Exact parent type, e.g. PassableLand; not a runtime numeric ID."
    }
  },
  "required": [],
  "additionalProperties": false
}
```

### `editor_text`

Type Unicode text into focused editor field. UI fallback for property/trigger fields; no clipboard modification.

**Required arguments:** `text`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "text": {
      "type": "string"
    }
  },
  "required": [
    "text"
  ],
  "additionalProperties": false
}
```

### `editor_toolset`

Get or switch current core/full tool set mid-session; omit mode to inspect. Default core exposes all helpers plus essential history/selection/camera/file commands; full exposes all generated native/action tools. No game connection. Always available in both sets. Changes notify tools/list_changed; client must refresh tools/list. Standalone only, not allowed in batches. Surface selection is not a permissions sandbox; all editor guards/confirmations remain.

**Required arguments:** none

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": true,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "mode": {
      "type": "string",
      "enum": [
        "core",
        "full"
      ]
    }
  },
  "required": [],
  "additionalProperties": false
}
```

### `editor_triggers`

Inspect/validate verified TR v12 single Always/CodeSnippet controllers; patch name/active/loop/code losslessly to new outputPath. Local operations need no game. Unknown shapes refuse, never guessed. export uses unique active-profile staging and copies verified bytes to approved new destination; apply explicitly replaces whole trigger set, keeps game-written original backup and verifies serialized round-trip. Not XS compilation/effects. File writes require confirmWrite=true; apply requires confirmDestructive=true. No overwrite/retry/automatic rollback.

**Required arguments:** `operation`, `path`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "operation": {
      "type": "string",
      "enum": [
        "inspect",
        "validate",
        "patch",
        "export",
        "apply"
      ]
    },
    "path": {
      "type": "string"
    },
    "outputPath": {
      "type": "string"
    },
    "profileDirectory": {
      "type": "string"
    },
    "name": {
      "type": "string"
    },
    "active": {
      "type": "boolean"
    },
    "loop": {
      "type": "boolean"
    },
    "code": {
      "type": "string"
    },
    "confirmWrite": {
      "type": "boolean"
    },
    "confirmDestructive": {
      "type": "boolean"
    }
  },
  "required": [
    "operation",
    "path"
  ],
  "additionalProperties": false
}
```

### `editor_units`

Read live scenario objects: full unitId, runtime protoId/exact base proto name, player, world XYZ, current/max health. Editor only, read-only process access; no focus/input/game calls. Optional exact case-insensitive proto, player (0=Gaia..12), area {x,z,radius} in world units. offset>=0, limit 1..200 (default 100). IDs scoped to current object/scenario lifetime, not persistent save IDs. Registry/IDs rechecked, not atomic frame snapshot; races refuse. Player-local prototype names may be null, never guessed; proto filtering refuses if names unresolved. Requires independently reviewed build-specific unit layout/runtime signatures; generator does not guess these fields.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": true,
  "openWorldHint": false,
  "readOnlyHint": true
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "proto": {
      "type": "string"
    },
    "player": {
      "type": "integer",
      "minimum": 0,
      "maximum": 12
    },
    "offset": {
      "type": "integer",
      "minimum": 0
    },
    "limit": {
      "type": "integer",
      "minimum": 1,
      "maximum": 200
    },
    "area": {
      "type": "object",
      "properties": {
        "x": {
          "type": "number",
          "minimum": -1000000,
          "maximum": 1000000
        },
        "z": {
          "type": "number",
          "minimum": -1000000,
          "maximum": 1000000
        },
        "radius": {
          "type": "number",
          "minimum": 0,
          "maximum": 1000000
        }
      },
      "required": [
        "x",
        "z",
        "radius"
      ],
      "additionalProperties": false
    }
  },
  "required": [],
  "additionalProperties": false
}
```

### `editor_validate_scenario`

Read-only partial diagnostics: live objects/map bounds, missing caller-declared requiredUnitIds or requireTownCenterPlayers; optional exported triggerPath for verified controller flags/percent/outcome/rule-reference heuristics. File is NOT proven current scene state. Objective UI/diplomacy/modes/dynamic XS references not inspected; no universal validity verdict or automatic fixes. offset>=0, limit1..200/default100.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": true,
  "openWorldHint": false,
  "readOnlyHint": true
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "triggerPath": {
      "type": "string"
    },
    "requiredUnitIds": {
      "type": "array",
      "items": {
        "type": "integer",
        "minimum": 0
      },
      "maxItems": 200
    },
    "requireTownCenterPlayers": {
      "type": "array",
      "items": {
        "type": "integer",
        "minimum": 0,
        "maximum": 12
      },
      "maxItems": 12
    },
    "offset": {
      "type": "integer",
      "minimum": 0
    },
    "limit": {
      "type": "integer",
      "minimum": 1,
      "maximum": 200
    }
  },
  "required": [],
  "additionalProperties": false
}
```

### `editor_water_types`

List lake/river/ocean water presets, exact names and settings. Optional XML detail includes rendering, terrain placement and other nested settings. Read-only, no running game required. name=exact identifier; filter=name/label substring. offset>=0, limit=1..200 (default 50); includeDefinition=false by default. Missing/stale data: --generate generated, then restart MCP.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": true,
  "openWorldHint": false,
  "readOnlyHint": true
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "filter": {
      "type": "string",
      "description": "Case-insensitive substring of name or label/string ID."
    },
    "name": {
      "type": "string",
      "description": "Exact source identifier (case-insensitive)."
    },
    "offset": {
      "type": "integer",
      "minimum": 0
    },
    "limit": {
      "type": "integer",
      "minimum": 1,
      "maximum": 200
    },
    "includeDefinition": {
      "type": "boolean",
      "description": "Include original XML definition; default false."
    },
    "category": {
      "type": "string",
      "enum": [
        "all",
        "lake",
        "river",
        "ocean"
      ]
    }
  },
  "required": [],
  "additionalProperties": false
}
```


## Native commands

### `editor_cameraLimit`

sets whether camera limiting is on. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `arg`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "arg": {
      "type": "integer"
    }
  },
  "required": [
    "arg"
  ],
  "additionalProperties": false
}
```

### `editor_cameraMaxZoomSet`

sets the current world camera Zoom to the given value. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `zoom`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "zoom": {
      "type": "number"
    }
  },
  "required": [
    "zoom"
  ],
  "additionalProperties": false
}
```

### `editor_cameraMinZoomSet`

sets the current world camera minimum Zoom to the given value. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `zoom`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "zoom": {
      "type": "number"
    }
  },
  "required": [
    "zoom"
  ],
  "additionalProperties": false
}
```

### `editor_cameraNice`

puts the camera in a reasonable orientation. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_cameraPitchAngle`

resets world camera orientation and sets pitch to the given angle. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `angle`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "angle": {
      "type": "number"
    }
  },
  "required": [
    "angle"
  ],
  "additionalProperties": false
}
```

### `editor_cameraPitchReset`

resets current world camera pitch to defaults, without affecting zoom or rotation. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_cameraRotate`

sets whether camera limiting is on. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `arg`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "arg": {
      "type": "integer"
    }
  },
  "required": [
    "arg"
  ],
  "additionalProperties": false
}
```

### `editor_cameraRotationReset`

resets current world camera rotation to defaults, without affecting zoom or pitch. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_cameraStart`

if applicable, sets the camera to the starting  view of the given player, or the active player, in case no player ID is given. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `playerID`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "playerID": {
      "type": "integer"
    }
  },
  "required": [
    "playerID"
  ],
  "additionalProperties": false
}
```

### `editor_cameraZoomReset`

resets current world camera Zoom to defaults. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_configSetInt`

sets a config var to an integer value Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`, `value`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    },
    "value": {
      "type": "integer"
    }
  },
  "required": [
    "name",
    "value"
  ],
  "additionalProperties": false
}
```

### `editor_configToggle`

defined var becomes undefined, and vice versa Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    }
  },
  "required": [
    "name"
  ],
  "additionalProperties": false
}
```

### `editor_cycleFogAndBlackMap`

moves between none, fog of war and blackmap Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_editMode`

changes the edit mode Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `modename`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "modename": {
      "type": "string"
    }
  },
  "required": [
    "modename"
  ],
  "additionalProperties": false
}
```

### `editor_fov`

sets camera and renderer field of view. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `newFov`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "newFov": {
      "type": "number"
    }
  },
  "required": [
    "newFov"
  ],
  "additionalProperties": false
}
```

### `editor_gadgetFlash`

turns gadget flashing on/off. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`, `flash`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    },
    "flash": {
      "type": "boolean"
    }
  },
  "required": [
    "name",
    "flash"
  ],
  "additionalProperties": false
}
```

### `editor_gadgetReal`

makes real the named gadget. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    }
  },
  "required": [
    "name"
  ],
  "additionalProperties": false
}
```

### `editor_gadgetRealIfNotMP`

makes real the named gadget. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    }
  },
  "required": [
    "name"
  ],
  "additionalProperties": false
}
```

### `editor_gadgetRefresh`

refresh the contents of the named gadget. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    }
  },
  "required": [
    "name"
  ],
  "additionalProperties": false
}
```

### `editor_gadgetScrollDown`

scrolls the gadget up one unit Native bool; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `gadget`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "gadget": {
      "type": "string"
    }
  },
  "required": [
    "gadget"
  ],
  "additionalProperties": false
}
```

### `editor_gadgetScrollLeft`

scrolls the gadget to the left one unit Native bool; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `gadget`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "gadget": {
      "type": "string"
    }
  },
  "required": [
    "gadget"
  ],
  "additionalProperties": false
}
```

### `editor_gadgetScrollRight`

scrolls the gadget to the Right one unit Native bool; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `gadget`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "gadget": {
      "type": "string"
    }
  },
  "required": [
    "gadget"
  ],
  "additionalProperties": false
}
```

### `editor_gadgetScrollUp`

scrolls the gadget up one unit Native bool; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `gadget`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "gadget": {
      "type": "string"
    }
  },
  "required": [
    "gadget"
  ],
  "additionalProperties": false
}
```

### `editor_gadgetToggle`

toggles the reality of the named gadget. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    }
  },
  "required": [
    "name"
  ],
  "additionalProperties": false
}
```

### `editor_gadgetToggleIfNotMP`

toggles the reality of the named gadget. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    }
  },
  "required": [
    "name"
  ],
  "additionalProperties": false
}
```

### `editor_gadgetUnreal`

makes un-real the named gadget. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    }
  },
  "required": [
    "name"
  ],
  "additionalProperties": false
}
```

### `editor_openTerrainTextureBrowserGui`

Opens The terrain texture browser Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_openTerrainTextureEditor`

Opens The terrain texture Editor Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_openWaterBrowserGui`

Opens The Water Browser Gui Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_openWaterEditorGui`

Opens The Water Editor Gui Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_pause`

with no arg, toggles pause state on/off.  otherwise, sets pause state Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `val`, `checkForAllowPause`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "val": {
      "type": "integer"
    },
    "checkForAllowPause": {
      "type": "boolean"
    }
  },
  "required": [
    "val",
    "checkForAllowPause"
  ],
  "additionalProperties": false
}
```

### `editor_player`

with no arg, outputs current player.  otherwise, sets current player to given argument Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `val`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "val": {
      "type": "integer"
    }
  },
  "required": [
    "val"
  ],
  "additionalProperties": false
}
```

### `editor_redo`

Re-does the last undone operation. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_refreshObjectInfoPanel`

Refreshes and shows the object info panel or hides it if no object is selected Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_saveScenario`

saves out a scenario file. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `fname`, `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "fname": {
      "type": "string"
    },
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "fname",
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `editor_setFreeCam`

sets whether free cam is on. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `arg`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "arg": {
      "type": "integer"
    }
  },
  "required": [
    "arg"
  ],
  "additionalProperties": false
}
```

### `editor_setSquadMode`

Sets the mode for a squad. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `squadModeName`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "squadModeName": {
      "type": "string"
    }
  },
  "required": [
    "squadModeName"
  ],
  "additionalProperties": false
}
```

### `editor_sunDecreaseInclination`

intended for ui use only.  Indicates that the decrease sun inclination key has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_sunDecreaseRotation`

intended for ui use only.  Indicates that the decrease sun rotation key has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_sunIncreaseInclination`

intended for ui use only.  Indicates that the increase sun inclination key has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_sunIncreaseRotation`

intended for ui use only.  Indicates that the increase sun rotation key has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_trackAddWaypoint`

adds the camera's current position and orientation to the current camera track. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_trackClear`

clears all tracks. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_trackCopy`

copies selected track. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_trackEditWaypoint`

edits the currently selected camera track. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_trackInsert`

adds a new camera track. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_trackMove`

moves a camera track up or down in the list. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `change`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "change": {
      "type": "integer"
    }
  },
  "required": [
    "change"
  ],
  "additionalProperties": false
}
```

### `editor_trackPause`

pauses the current camera track. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_trackPlay`

plays a track file (otherwise if "none" than plays the current track.) with no arg uses current duration, otherwise sets duration Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `lDuration`, `eventID`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "lDuration": {
      "type": "number"
    },
    "eventID": {
      "type": "integer"
    }
  },
  "required": [
    "lDuration",
    "eventID"
  ],
  "additionalProperties": false
}
```

### `editor_trackRemove`

removes selected track. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_trackRemoveWaypoint`

removes the most recently added track waypoint from the current camera track. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_trackStepBackward`

steps the current camera track 1 step backward. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_trackStepForward`

steps the current camera track 1 step forward. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_trackStop`

stops the current camera track. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_trackToggleShow`

toggles rendering of the camera track on and off. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_trackWaypointMove`

reorder waypoint. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `change`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "change": {
      "type": "integer"
    }
  },
  "required": [
    "change"
  ],
  "additionalProperties": false
}
```

### `editor_uiAddChatNotification`

Adds a notification to the game's chat output, can play a sound too. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `fromPlayerID`, `toPlayerID`, `stringID`, `soundSetNameID`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "fromPlayerID": {
      "type": "integer"
    },
    "toPlayerID": {
      "type": "integer"
    },
    "stringID": {
      "type": "string"
    },
    "soundSetNameID": {
      "type": "integer"
    }
  },
  "required": [
    "fromPlayerID",
    "toPlayerID",
    "stringID",
    "soundSetNameID"
  ],
  "additionalProperties": false
}
```

### `editor_uiAddSelectNumberGroup`

adds the units in the given number group to current selection. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `group`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "group": {
      "type": "integer"
    }
  },
  "required": [
    "group"
  ],
  "additionalProperties": false
}
```

### `editor_uiAddSelectionButtonDown`

intended for ui use only.  Indicates that the add selection button has been pressed. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `allowToggle`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "allowToggle": {
      "type": "boolean"
    }
  },
  "required": [
    "allowToggle"
  ],
  "additionalProperties": false
}
```

### `editor_uiAddSelectionButtonUp`

intended for ui use only.  Indicates that the add selection button has been released. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiAgeUpFromAnywhere`

Open age up popup from anywhere Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiApplyLightingSet`

intended for UI use only.  Applies a Lighting Set Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `n`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "n": {
      "type": "integer"
    }
  },
  "required": [
    "n"
  ],
  "additionalProperties": false
}
```

### `editor_uiAutoScoutSelectedUnit`

Enables/disables auto scout on selected unit. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `unitID`, `enable`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "unitID": {
      "type": "integer"
    },
    "enable": {
      "type": "boolean"
    }
  },
  "required": [
    "unitID",
    "enable"
  ],
  "additionalProperties": false
}
```

### `editor_uiBeginClickDrag`

intended for ui use only.  Indicates that click-drag button has been pressed. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiBuildAtPointer`

instructs the currently selected unit(s) to build the current proto unit cursor building type at the pointer location. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiBuildAtSite`

intended for UI use only.  Selects nearest worker and opens the gamepad build menu. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiBuildMode`

does and editMode and setProtoID, after verifying sufficient resources for the current player. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `protoID`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "protoID": {
      "type": "integer"
    }
  },
  "required": [
    "protoID"
  ],
  "additionalProperties": false
}
```

### `editor_uiBuildWallAtPointer`

instructs the currently selected unit(s) to build the current proto unit cursor building type at the pointer location, with wall-like endpoint behavior. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `addWaypoint`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "addWaypoint": {
      "type": "boolean"
    }
  },
  "required": [
    "addWaypoint"
  ],
  "additionalProperties": false
}
```

### `editor_uiCameraControl`

Controls camera with gamepad stick. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `x`, `y`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "x": {
      "type": "number"
    },
    "y": {
      "type": "number"
    }
  },
  "required": [
    "x",
    "y"
  ],
  "additionalProperties": false
}
```

### `editor_uiCancelRadialMenu`

intended for ui use only.  Cancels radial menu selected entry. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiCancelRadialMenuLT`

intended for ui use only.  Cancels radial menu selected entry. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiCancelRadialMenuReleased`

intended for ui use only.  Cancels radial menu selected entry. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiCancelSelectedBuilding`

cancels currently built unit. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiCashInFavorBonus`

Try to claim Favor from your stash. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `showPopupOnError`, `favorStashItemID`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "showPopupOnError": {
      "type": "boolean"
    },
    "favorStashItemID": {
      "type": "integer"
    }
  },
  "required": [
    "showPopupOnError",
    "favorStashItemID"
  ],
  "additionalProperties": false
}
```

### `editor_uiCenterPointer`

Centers mouse pointer. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiChangeBrushSize`

changes the size of the current brush Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `p1`, `p2`, `p3`, `p4`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "p1": {
      "type": "number"
    },
    "p2": {
      "type": "number"
    },
    "p3": {
      "type": "number"
    },
    "p4": {
      "type": "number"
    }
  },
  "required": [
    "p1",
    "p2",
    "p3",
    "p4"
  ],
  "additionalProperties": false
}
```

### `editor_uiChangeBrushSizePercent`

changes the size of the current brush by percentage(0-100) between brush size min and max values Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `p1`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "p1": {
      "type": "number"
    }
  },
  "required": [
    "p1"
  ],
  "additionalProperties": false
}
```

### `editor_uiChangeBrushType`

changes the brush to the named type Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `brushType`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "brushType": {
      "type": "string"
    }
  },
  "required": [
    "brushType"
  ],
  "additionalProperties": false
}
```

### `editor_uiChangeElevationToSample`

intended for ui use only.  Indicates that the change elevation to sample button has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiChangeGizmoType`

changes the guizmo to the parameter type Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiChatDisplayModeToHistory`

Toggles the chat display mode to history mode. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiChatDisplayModeToRecent`

Toggles the chat display mode to recent mode. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiChatDisplayModeToggle`

Toggles the chat display mode. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiChatScrollBack`

Scrolls chat back one. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiChatScrollForward`

Scrolls chat forward one. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiClearChat`

Clears the chat and resets to recent mode.  clearOnly == true if you don't want it to populate the chat, but only clear it. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `clearOnly`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "clearOnly": {
      "type": "boolean"
    }
  },
  "required": [
    "clearOnly"
  ],
  "additionalProperties": false
}
```

### `editor_uiClearCursor`

resets the cursor to the basic pointer. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiClearGatherPoint`

Clears the gather point for the selected unit(s), returning it to a default state. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiClearMenu`

removes any dangling child menus off of the given gadget Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    }
  },
  "required": [
    "name"
  ],
  "additionalProperties": false
}
```

### `editor_uiClearNumberGroup`

erases the given number group. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `group`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "group": {
      "type": "integer"
    }
  },
  "required": [
    "group"
  ],
  "additionalProperties": false
}
```

### `editor_uiClearOverrideCameras`

intended for ui use only. Clears any active perspective or follow cameras. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiClearSelection`

deselects all selected units Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiClearTribute`

Clears tribute in the diplomacy menu. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiCloseControlsPopup`

Closes the controls popup. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiCloseDialog`

closes (as if clicked on the close button) any active dialog Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiCloseFieldSet`

closes (as if clicked on the close button) any active field set Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiCloseRadialMenu`

intended for ui use only.  Conducts the cancel command on the radial menu (whatever that does for the specific context). Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiCloseRadialMenuAndPause`

intended for ui use only. Close open radial menu and show pause menu. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiConfirmAndLoadQuickSave`

Opens a yes no popup to and loads save game if player confirms Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiControlGroupsPress`

intended for ui use only.  Opens the control group gamepad menu menu. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiControlGroupsRelease`

intended for ui use only.  Opens the control group gamepad menu menu. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiControlSelectionButtonDown`

intended for ui use only.  Handles control+selection button pressing. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiControlSelectionButtonUp`

intended for ui use only.  Indicates control+selection button has been released. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiConvertUnits`

intended for ui use only.  Indicates that the convert units button has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiCopyToClipboard`

copies the brush selection to the clipboard. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiCoverTerrainWithWater`

flattens terrain and paints water over the entire map. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `waterHeight`, `depth`, `name`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "waterHeight": {
      "type": "number"
    },
    "depth": {
      "type": "number"
    },
    "name": {
      "type": "string"
    }
  },
  "required": [
    "waterHeight",
    "depth",
    "name"
  ],
  "additionalProperties": false
}
```

### `editor_uiCreateNumberGroup`

creates a number group with the currently selected units. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `group`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "group": {
      "type": "integer"
    }
  },
  "required": [
    "group"
  ],
  "additionalProperties": false
}
```

### `editor_uiCycleCurrentActivate`

acts like the current cycle gadget has been pressed Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiCycleGadget`

cycles through the 'active' child gadget of a deluxe gadget. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `delta`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "delta": {
      "type": "integer"
    }
  },
  "required": [
    "delta"
  ],
  "additionalProperties": false
}
```

### `editor_uiDPadPrequeueDown`

intended for ui use only. Prequeues down for the radial menu quick find Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiDPadPrequeueLeft`

intended for ui use only. Prequeues left for the radial menu quick find Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiDPadPrequeueRight`

intended for ui use only. Prequeues right for the radial menu quick find Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiDPadPrequeueUp`

intended for ui use only. Prequeues up for the radial menu quick find Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiDecPlaceVariation`

decrements the variation to place. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiDeleteAllSelectedUnits`

deletes all selected unit. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `editor_uiDeleteCameraStartLoc`

TODO. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiDeleteSelectedUnit`

deletes selected unit. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `ignoreConfirmation`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "ignoreConfirmation": {
      "type": "boolean"
    }
  },
  "required": [
    "ignoreConfirmation"
  ],
  "additionalProperties": false
}
```

### `editor_uiDeleteUnits`

intended for ui use only.  Indicates that the delete units button has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiDoubleClickDeselect`

intended for ui use only.  Double click deselect at pointer location. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiDoubleClickSelect`

intended for ui use only.  Double click select at pointer location. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `add`, `checkAction`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "add": {
      "type": "integer"
    },
    "checkAction": {
      "type": "boolean"
    }
  },
  "required": [
    "add",
    "checkAction"
  ],
  "additionalProperties": false
}
```

### `editor_uiDumpAllUnitHotKeyMappings`

spews all hot key mappings that create units to the console Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiDumpKeyMappings`

spews all key mappings out to the console. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `context`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "context": {
      "type": "string"
    }
  },
  "required": [
    "context"
  ],
  "additionalProperties": false
}
```

### `editor_uiDumpUnmappedKeys`

spews all empty keys out to the console Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `context`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "context": {
      "type": "string"
    }
  },
  "required": [
    "context"
  ],
  "additionalProperties": false
}
```

### `editor_uiEjectAtPointer`

intended for UI use only.  Sends an ejection command with waypoint for the selected unit. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiEjectBackToWork`

intended for UI use only.  Sends an ejection command for the selected unit, tasking ungarrisoned units back to work. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiEjectFromAllMapControlBuildings`

intended for UI use only.  Ejects all units from all Map Control Buildings. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiEjectGarrisonedUnits`

intended for UI use only.  Sends an ejection command for the selected unit. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiEmpowerAtPointer`

Commands the selected unit(s) to Empower the target building at the pointer position. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiEnterContext`

enters the specified UI context. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `context`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "context": {
      "type": "string"
    }
  },
  "required": [
    "context"
  ],
  "additionalProperties": false
}
```

### `editor_uiEnterGameMenuModeIfNotResigned`

Wrapper that enters GameMenu mode if the player isn't resigned. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiErase`

intended for ui use only.  Indicates that the erase button has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "boolean"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiExportGrouping`

save a group. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiExportTriggers`

save some triggers. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiFavorStashBuySelectedItem`

intended for UI use only. Attempt to buy the selected item. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiFavorStashNextItem`

intended for UI use only. Cycle to the next favor item in the favor popup. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiFavorStashPreviousItem`

intended for UI use only. Cycle to the previous favor item in the favor popup. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiFilterTerrainSelection`

filter the current terrain selection. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiFindAllOfSelectedType`

finds all units of the same type as the selected unit Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiFindAllOfTwoTypes`

finds all units belonging to any of the two provided types Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name1`, `name2`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name1": {
      "type": "string"
    },
    "name2": {
      "type": "string"
    }
  },
  "required": [
    "name1",
    "name2"
  ],
  "additionalProperties": false
}
```

### `editor_uiFindAllOfTwoTypesAnd`

finds all units belonging to both of the two provided types Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name1`, `name2`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name1": {
      "type": "string"
    },
    "name2": {
      "type": "string"
    }
  },
  "required": [
    "name1",
    "name2"
  ],
  "additionalProperties": false
}
```

### `editor_uiFindAllOfTwoTypesOnScreen`

finds all units of either of the two types currently on screen Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`, `secondName`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    },
    "secondName": {
      "type": "string"
    }
  },
  "required": [
    "name",
    "secondName"
  ],
  "additionalProperties": false
}
```

### `editor_uiFindAllOfTwoTypesOnScreenAnd`

finds all units of both of the two types currently on screen Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`, `secondName`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    },
    "secondName": {
      "type": "string"
    }
  },
  "required": [
    "name",
    "secondName"
  ],
  "additionalProperties": false
}
```

### `editor_uiFindAllOfType`

finds all units of the same type Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    }
  },
  "required": [
    "name"
  ],
  "additionalProperties": false
}
```

### `editor_uiFindAllOfTypeIdle`

finds all idle units of the same type Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    }
  },
  "required": [
    "name"
  ],
  "additionalProperties": false
}
```

### `editor_uiFindAllOfTypeIdle2`

finds all idle units of the same type Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    }
  },
  "required": [
    "name"
  ],
  "additionalProperties": false
}
```

### `editor_uiFindAllOfTypeOnScreen`

finds all units of the same type currently on screen Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    }
  },
  "required": [
    "name"
  ],
  "additionalProperties": false
}
```

### `editor_uiFindAllOfTypeOnScreenIdle`

finds all units of the same type currently on screen Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    }
  },
  "required": [
    "name"
  ],
  "additionalProperties": false
}
```

### `editor_uiFindAllOfTypes`

finds all units of the same types Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    }
  },
  "required": [
    "name"
  ],
  "additionalProperties": false
}
```

### `editor_uiFindAllStealthUnits`

finds all Stealth units with desired state Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `includeUnstealthed`, `includeStealthed`, `onlyOnScreen`, `onlyAmbushing`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "includeUnstealthed": {
      "type": "boolean"
    },
    "includeStealthed": {
      "type": "boolean"
    },
    "onlyOnScreen": {
      "type": "boolean"
    },
    "onlyAmbushing": {
      "type": "boolean"
    }
  },
  "required": [
    "includeUnstealthed",
    "includeStealthed",
    "onlyOnScreen",
    "onlyAmbushing"
  ],
  "additionalProperties": false
}
```

### `editor_uiFindAnyOfTypes`

finds the next unit of any of the given types (separated by commas or spaces) in the arbitrary order of unit ID, so that it can be called repeatedly to cycle Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `nameListString`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "nameListString": {
      "type": "string"
    }
  },
  "required": [
    "nameListString"
  ],
  "additionalProperties": false
}
```

### `editor_uiFindGatherersNotGathering`

finds the gatherer unit that's not gathering in the arbitrary order of unit ID, so that it can be called repeatedly to cycle. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiFindIdleOrAnyType`

finds the next idle unit of the given type in the arbitrary order of unit ID, so that it can be called repeatedly to cycle. If none are found, finds the next unit even if it's not idle. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`, `shiftSelectsAll`, `controlModifier`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    },
    "shiftSelectsAll": {
      "type": "boolean"
    },
    "controlModifier": {
      "type": "boolean"
    }
  },
  "required": [
    "name",
    "shiftSelectsAll",
    "controlModifier"
  ],
  "additionalProperties": false
}
```

### `editor_uiFindIdleType`

finds the next idle unit of the given type in the arbitrary order of unit ID, so that it can be called repeatedly to cycle. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`, `shiftSelectsAll`, `controlModifier`, `groupByType`, `useExclusionSettings`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    },
    "shiftSelectsAll": {
      "type": "boolean"
    },
    "controlModifier": {
      "type": "boolean"
    },
    "groupByType": {
      "type": "boolean"
    },
    "useExclusionSettings": {
      "type": "boolean"
    }
  },
  "required": [
    "name",
    "shiftSelectsAll",
    "controlModifier",
    "groupByType",
    "useExclusionSettings"
  ],
  "additionalProperties": false
}
```

### `editor_uiFindIdleType2`

finds the next idle unit of the given type in the arbitrary order of unit ID, so that it can be called repeatedly to cycle. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`, `shiftSelectsAll`, `controlModifier`, `groupByType`, `useExclusionSettings`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    },
    "shiftSelectsAll": {
      "type": "boolean"
    },
    "controlModifier": {
      "type": "boolean"
    },
    "groupByType": {
      "type": "boolean"
    },
    "useExclusionSettings": {
      "type": "boolean"
    }
  },
  "required": [
    "name",
    "shiftSelectsAll",
    "controlModifier",
    "groupByType",
    "useExclusionSettings"
  ],
  "additionalProperties": false
}
```

### `editor_uiFindKeyMapping`

finds all key mappings for a given key Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyname`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyname": {
      "type": "string"
    }
  },
  "required": [
    "keyname"
  ],
  "additionalProperties": false
}
```

### `editor_uiFindMenuPress`

intended for ui use only.  Opens the find buildings/units menu. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiFindMenuRelease`

intended for ui use only.  Opens the find buildings/units menu. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiFindNextIdleGroupOfType`

finds the next contiguous group of idle units of the given type in the arbitrary order of unit ID, so that it can be called repeatedly to cycle. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`, `groupByPUID`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    },
    "groupByPUID": {
      "type": "boolean"
    }
  },
  "required": [
    "name",
    "groupByPUID"
  ],
  "additionalProperties": false
}
```

### `editor_uiFindNextIdleGroupOfType2`

finds the next contiguous group of idle units of the given type in the arbitrary order of unit ID, so that it can be called repeatedly to cycle. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`, `groupByPUID`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    },
    "groupByPUID": {
      "type": "boolean"
    }
  },
  "required": [
    "name",
    "groupByPUID"
  ],
  "additionalProperties": false
}
```

### `editor_uiFindResourceGatherers`

finds the next resource gatherer unit of the given resource type in the arbitrary order of unit ID, so that it can be called repeatedly to cycle. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `resourceName`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "resourceName": {
      "type": "string"
    }
  },
  "required": [
    "resourceName"
  ],
  "additionalProperties": false
}
```

### `editor_uiFindTownBellTC`

finds the next town center that has the town bell active, so that it can be called repeatedly to cycle. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiFindTwoTypes`

finds the next unit of the given two types in the arbitrary order of unit ID, so that it can be called repeatedly to cycle Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name1`, `name2`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name1": {
      "type": "string"
    },
    "name2": {
      "type": "string"
    }
  },
  "required": [
    "name1",
    "name2"
  ],
  "additionalProperties": false
}
```

### `editor_uiFindTwoTypesAnd`

finds the next unit of both of the given two types in the arbitrary order of unit ID, so that it can be called repeatedly to cycle Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name1`, `name2`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name1": {
      "type": "string"
    },
    "name2": {
      "type": "string"
    }
  },
  "required": [
    "name1",
    "name2"
  ],
  "additionalProperties": false
}
```

### `editor_uiFindType`

finds the next unit (idle or not) of the given type in the arbitrary order of unit ID, so that it can be called repeatedly to cycle. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    }
  },
  "required": [
    "name"
  ],
  "additionalProperties": false
}
```

### `editor_uiFlareAtPointer`

Sends out a flare at the pointer position. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiFlareAtUnit`

Sends out a flare at the unit's position. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `unitID`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "unitID": {
      "type": "integer"
    }
  },
  "required": [
    "unitID"
  ],
  "additionalProperties": false
}
```

### `editor_uiFlattenTerrainSelection`

flatten the current terrain selection. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiFrontendMPRankedSetupCancel`

intended for ui use only. Hotkey for cancelling ranked search. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiGameSpeedToggle`

toggles the game speed between Slow, Normal and Fast. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiGamepadCycleScoreboardView`

Cycle scoreboard view Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiGamepadReplayCycleMapVis`

Replay functions 'Cycle Map Visibility' Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiGamepadReplayDPadDown`

Replay functions 'Pause' Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiGamepadReplayDPadLeft`

Replay function 'Slowed Playback' Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiGamepadReplayDPadRight`

Replay function 'Fast Playback' Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiGamepadReplayDPadUp`

Replay functions 'Play' Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiGamepadReplayPlayerViewMenuPress`

Opens replay player view ring menu Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiGamepadReplayPlayerViewMenuRelease`

Closes replay player view ring menu Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiGamepadReplayRestart`

Replay function 'Restart' Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `editor_uiGamepadReplaySwitchResourceView`

Swap between power bar and resource view in replay mode Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiGarrisonToPointer`

Commands the selected unit(s) to garrison in a building at the pointer position. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiGodPowersPress`

intended for ui use only.  Opens the god powers menu. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiGuardAtPointer`

intended for UI use only.  Sends an guard order. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiHandleIdleBanner`

does the right thing. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`, `groupByType`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    },
    "groupByType": {
      "type": "boolean"
    }
  },
  "required": [
    "name",
    "groupByType"
  ],
  "additionalProperties": false
}
```

### `editor_uiHandleTrainingPanelAction`

handles clicking over an item in the global queue/training panel. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `unitID`, `actionType`, `actionID`, `actionIdx`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "unitID": {
      "type": "integer"
    },
    "actionType": {
      "type": "integer"
    },
    "actionID": {
      "type": "integer"
    },
    "actionIdx": {
      "type": "integer"
    }
  },
  "required": [
    "unitID",
    "actionType",
    "actionID",
    "actionIdx"
  ],
  "additionalProperties": false
}
```

### `editor_uiHandleUserTab`

handles toggle of user textures Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `parent`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "parent": {
      "type": "integer"
    }
  },
  "required": [
    "parent"
  ],
  "additionalProperties": false
}
```

### `editor_uiHideCursor`

testing only Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `hide`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "hide": {
      "type": "boolean"
    }
  },
  "required": [
    "hide"
  ],
  "additionalProperties": false
}
```

### `editor_uiHotkeyToggleObjectivesDialog`

TODO. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiIgnoreNextKey`

used when activating a text box with a key to avoid having that key go into the text box too. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiImportTriggers`

load some triggers. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiIncPlaceVariation`

increments the variation to place. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiLTModifierBeginHold`

used by interface elements that need to know if the LT is held Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiLTModifierEndHold`

used by interface elements that need to know if the LT is held Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiLTSelectRadialMenuEntry`

intended for ui use only.  Select the currently highlighted option in the radial menu. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiLeaveContext`

leaves the specified UI context. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `context`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "context": {
      "type": "string"
    }
  },
  "required": [
    "context"
  ],
  "additionalProperties": false
}
```

### `editor_uiLeaveModeOnUnshift`

causes game to return to editMode none when shift hotkey is released Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiLoadTriggers`

load some triggers. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `filename`, `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "filename": {
      "type": "string"
    },
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "filename",
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `editor_uiLookAtAndSelectUnit`

moves the camera to see the specified unit and selects it. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `lID`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "lID": {
      "type": "integer"
    }
  },
  "required": [
    "lID"
  ],
  "additionalProperties": false
}
```

### `editor_uiLookAtBattle`

moves the camera to see the specified battle. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `bID`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "bID": {
      "type": "integer"
    }
  },
  "required": [
    "bID"
  ],
  "additionalProperties": false
}
```

### `editor_uiLookAtNumberGroup`

moves the camera to see the given number group. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `number`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "number": {
      "type": "integer"
    }
  },
  "required": [
    "number"
  ],
  "additionalProperties": false
}
```

### `editor_uiLookAtProto`

moves the camera to see the first instance of proto unit X owned by the current player. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `protoname`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "protoname": {
      "type": "string"
    }
  },
  "required": [
    "protoname"
  ],
  "additionalProperties": false
}
```

### `editor_uiLookAtSelection`

moves the camera to see the first selected unit. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiLookAtUnit`

moves the camera to see the specified Unit. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `lID`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "lID": {
      "type": "integer"
    }
  },
  "required": [
    "lID"
  ],
  "additionalProperties": false
}
```

### `editor_uiLowerElevation`

intended for ui use only.  Indicates that the lower elevation button has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiLowerTerrainSelection`

intended for ui use only.  Indicates that the lower terrain selection button has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiMessageBox`

pops up a message box with text that activated the cmd when the ok button is hit Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `text`, `command`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "text": {
      "type": "string"
    },
    "command": {
      "type": "string"
    }
  },
  "required": [
    "text",
    "command"
  ],
  "additionalProperties": false
}
```

### `editor_uiMinimapBack`

intended for ui use only. Gamepad back function while minimap is enlarged Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiMinimapDPadDown`

intended for ui use only. DPad Down function while minimap is enlarged Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiMinimapDPadDownReleased`

intended for ui use only. DPad Down released function while minimap is enlarged Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiMinimapDPadLeft`

intended for ui use only. DPad Left function while minimap is enlarged Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiMinimapDPadRight`

intended for ui use only. DPad right function while minimap is enlarged Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiMinimapDPadUp`

intended for ui use only. DPad Up function while minimap is enlarged Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiMinimapSnapCamera`

Snaps the camera to the minimap reticle position Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiMinimapSnapCameraReleased`

End snapping the camera to the minimap reticle position Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiMinorGodUI`

Used to activate the minor god UI Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `techID`, `tcID`, `playerID`, `PreQueue`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "techID": {
      "type": "integer"
    },
    "tcID": {
      "type": "integer"
    },
    "playerID": {
      "type": "integer"
    },
    "PreQueue": {
      "type": "boolean"
    }
  },
  "required": [
    "techID",
    "tcID",
    "playerID",
    "PreQueue"
  ],
  "additionalProperties": false
}
```

### `editor_uiMinorGodUIInSelected`

Used to activate the minor god UI in current selection Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `PreQueue`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "PreQueue": {
      "type": "boolean"
    }
  },
  "required": [
    "PreQueue"
  ],
  "additionalProperties": false
}
```

### `editor_uiMoveAllMilitaryAtPointer`

Moves all military units to the pointer position. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiMoveAllUnitOfTypeToUnitLocation`

Find units of a certain type, and tell them to walk over. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `targetUnitID`, `typeNames`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "targetUnitID": {
      "type": "integer"
    },
    "typeNames": {
      "type": "string"
    }
  },
  "required": [
    "targetUnitID",
    "typeNames"
  ],
  "additionalProperties": false
}
```

### `editor_uiMoveIdleUnitsToPointer`

intended for UI use only.  Moves a number of units of the type given to the location of the pointer, preferencing idle units. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `number`, `type`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "number": {
      "type": "integer"
    },
    "type": {
      "type": "string"
    }
  },
  "required": [
    "number",
    "type"
  ],
  "additionalProperties": false
}
```

### `editor_uiMoveSelectionAddButtonDown`

intended for ui use only.  Indicates that selection button has been pressed. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiMoveSelectionAddButtonUp`

intended for ui use only.  Indicates that selection button has been released. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiMoveSelectionButtonDown`

intended for ui use only.  Indicates that selection button has been pressed. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiMoveSelectionButtonUp`

intended for ui use only.  Indicates that selection button has been released. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiMoveUnitBackward`

intended for ui use only.  Indicates that the move unit backward key has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiMoveUnitDown`

intended for ui use only.  Indicates that the move unit down key has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiMoveUnitForward`

intended for ui use only.  Indicates that the move unit forward key has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiMoveUnitLeft`

intended for ui use only.  Indicates that the move unit left key has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiMoveUnitOfNameToUnitLocation`

Find the unit of a certain protounit name nearest to the target unit, and tell them to walk over. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `targetUnitID`, `unitNames`, `minDist`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "targetUnitID": {
      "type": "integer"
    },
    "unitNames": {
      "type": "string"
    },
    "minDist": {
      "type": "number"
    }
  },
  "required": [
    "targetUnitID",
    "unitNames",
    "minDist"
  ],
  "additionalProperties": false
}
```

### `editor_uiMoveUnitOfTypeToUnitLocation`

Find the unit of a certain type nearest to the target unit, and tell them to walk over. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `targetUnitID`, `typeNames`, `minDist`, `doWorkOnUnit`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "targetUnitID": {
      "type": "integer"
    },
    "typeNames": {
      "type": "string"
    },
    "minDist": {
      "type": "number"
    },
    "doWorkOnUnit": {
      "type": "boolean"
    }
  },
  "required": [
    "targetUnitID",
    "typeNames",
    "minDist",
    "doWorkOnUnit"
  ],
  "additionalProperties": false
}
```

### `editor_uiMoveUnitRight`

intended for ui use only.  Indicates that the move unit right key has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiMoveUnitUp`

intended for ui use only.  Indicates that the move unit up key has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiMoveUnitsToPointer`

intended for UI use only.  Moves a number of units of the type given to the location of the pointer. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `number`, `type`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "number": {
      "type": "integer"
    },
    "type": {
      "type": "string"
    }
  },
  "required": [
    "number",
    "type"
  ],
  "additionalProperties": false
}
```

### `editor_uiNewScenario`

creates a new blank scenario Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `editor_uiNorseBuildAtSite`

intended for UI use only.  Selects nearest infantry and opens the gamepad build menu. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiOpenChatMenu`

Opens the chat menu. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiOpenDiplomacyMenu`

Opens the diplomacy menu. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiOpenGameSettings`

Opens the game settings dialog. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiOpenRecordGameBrowser`

open a record game from the replays directory Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiOpenScenarioBrowser`

open a scenario from the scenario directory Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `fileName`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "fileName": {
      "type": "string"
    }
  },
  "required": [
    "fileName"
  ],
  "additionalProperties": false
}
```

### `editor_uiPaint`

intended for ui use only.  Indicates that the paint button has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`, `offset`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "boolean"
    },
    "offset": {
      "type": "boolean"
    }
  },
  "required": [
    "keyState",
    "offset"
  ],
  "additionalProperties": false
}
```

### `editor_uiPaintCliff`

intended for ui use only.  Indicates that the paint cliff button has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiPaintForest`

intended for ui use only.  Indicates that the paint button has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`, `offset`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "boolean"
    },
    "offset": {
      "type": "boolean"
    }
  },
  "required": [
    "keyState",
    "offset"
  ],
  "additionalProperties": false
}
```

### `editor_uiPaintTerrainOverlay`

intended for ui use only.  Indicates that the paint terrain overlay button has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiPaintTerrainToSample`

intended for ui use only.  Indicates that the sample terrain button has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiPaintWater`

intended for ui use only.  Indicates that the paint water button has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiPaintWaterArea`

intended for ui use only. Painting water area. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiPaintWaterObjects`

Paint objects on the currently selected water. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiPasteFromClipboard`

pastes the contents of the clipboard to the brush selection. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiPatrolAtPointer`

intended for UI use only.  Sends an patrol order. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiPayFoundation`

pays for the given foundation. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `unitID`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "unitID": {
      "type": "integer"
    }
  },
  "required": [
    "unitID"
  ],
  "additionalProperties": false
}
```

### `editor_uiPeekBuildingChainRadius`

Shows the current players building chain radius for x seconds. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `seconds`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "seconds": {
      "type": "number"
    }
  },
  "required": [
    "seconds"
  ],
  "additionalProperties": false
}
```

### `editor_uiPhotoModeDialogFocusEnter`

intended for ui use only. Enter dialog navigation (gamepad) in photo mode Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiPhotoModeDialogFocusLeave`

intended for ui use only. Leave dialog navigation (gamepad) in photo mode Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiPhotoModeMouseZoom`

intended for ui use only. Zoom (dolly) camera in photo mode Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `mouseWheelValue`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "mouseWheelValue": {
      "type": "integer"
    }
  },
  "required": [
    "mouseWheelValue"
  ],
  "additionalProperties": false
}
```

### `editor_uiPhotoModeQuit`

intended for ui use only. Quit photo mode Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `editor_uiPhotoModeResetCamera`

intended for ui use only. Reset camera position in photo mode Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiPhotoModeTakeScreenshot`

intended for ui use only. Take screenshot in photo mode Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `unused`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "unused": {
      "type": "integer"
    }
  },
  "required": [
    "unused"
  ],
  "additionalProperties": false
}
```

### `editor_uiPhotoModeTogglePhotoUI`

intended for ui use only. Toggle photo mode UI in photo mode Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiPitchUnitDown`

intended for ui use only.  Indicates that the pitch unit down key has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiPitchUnitUp`

intended for ui use only.  Indicates that the pitch unit up key has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiPlaceAtPointer`

intended for ui use only.  Places unit at pointer location. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `changeVariation`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "changeVariation": {
      "type": "boolean"
    }
  },
  "required": [
    "changeVariation"
  ],
  "additionalProperties": false
}
```

### `editor_uiQuickSelectDown`

intended for ui use only. Quick selects certain units Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `leftTrigger`, `rightTrigger`, `leftBumper`, `rightBumper`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "leftTrigger": {
      "type": "boolean"
    },
    "rightTrigger": {
      "type": "boolean"
    },
    "leftBumper": {
      "type": "boolean"
    },
    "rightBumper": {
      "type": "boolean"
    }
  },
  "required": [
    "leftTrigger",
    "rightTrigger",
    "leftBumper",
    "rightBumper"
  ],
  "additionalProperties": false
}
```

### `editor_uiQuickSelectLeft`

intended for ui use only. quick selects certain units Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `leftTrigger`, `rightTrigger`, `leftBumper`, `rightBumper`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "leftTrigger": {
      "type": "boolean"
    },
    "rightTrigger": {
      "type": "boolean"
    },
    "leftBumper": {
      "type": "boolean"
    },
    "rightBumper": {
      "type": "boolean"
    }
  },
  "required": [
    "leftTrigger",
    "rightTrigger",
    "leftBumper",
    "rightBumper"
  ],
  "additionalProperties": false
}
```

### `editor_uiQuickSelectRight`

intended for ui use only. quick selects certain units Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `leftTrigger`, `rightTrigger`, `leftBumper`, `rightBumper`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "leftTrigger": {
      "type": "boolean"
    },
    "rightTrigger": {
      "type": "boolean"
    },
    "leftBumper": {
      "type": "boolean"
    },
    "rightBumper": {
      "type": "boolean"
    }
  },
  "required": [
    "leftTrigger",
    "rightTrigger",
    "leftBumper",
    "rightBumper"
  ],
  "additionalProperties": false
}
```

### `editor_uiQuickSelectUp`

intended for ui use only. Quick selects certain units Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `leftTrigger`, `rightTrigger`, `leftBumper`, `rightBumper`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "leftTrigger": {
      "type": "boolean"
    },
    "rightTrigger": {
      "type": "boolean"
    },
    "leftBumper": {
      "type": "boolean"
    },
    "rightBumper": {
      "type": "boolean"
    }
  },
  "required": [
    "leftTrigger",
    "rightTrigger",
    "leftBumper",
    "rightBumper"
  ],
  "additionalProperties": false
}
```

### `editor_uiRadialMenuAltAction`

intended for ui use only.  Activates the page specific action. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiRadialMenuChangePage`

intended for ui use only.  Changes radial menu page. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiRadialMenuCloseCommandMenu`

Closes the unit command radial menu if it is open. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiRadialMenuCloseControlGroupMenu`

Closes the control group radial menu if it is open. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiRadialMenuCloseFindMenu`

Closes the find radial menu if it is open. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiRadialMenuCloseGodPowerMenu`

Closes the god power radial menu if it is open. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiRadialMenuCloseVPSMenu`

Closes the VPS radial menu if it is open. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiRadialMenuThumbAction`

intended for ui use only.  Activates the page specific thumb action. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiRadialMenuViewAction`

intended for ui use only.  Activates the page specific view action. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiRadialMenuXAxis`

intended for ui use only.  Indicates the X position of the radial menu selector. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `axisValue`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "axisValue": {
      "type": "number"
    }
  },
  "required": [
    "axisValue"
  ],
  "additionalProperties": false
}
```

### `editor_uiRadialMenuYAxis`

intended for ui use only.  Indicates the Y position of the radial menu selector. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `axisValue`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "axisValue": {
      "type": "number"
    }
  },
  "required": [
    "axisValue"
  ],
  "additionalProperties": false
}
```

### `editor_uiRaiseElevation`

intended for ui use only.  Indicates that the raise elevation button has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiRaiseTerrainSelection`

intended for ui use only.  Indicates that the raise terrain selection button has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiRecalcVariation`

intended for ui use only.  Indicates that the recalc variation button has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiRefreshCommandPanel`

Forces a refresh on the command panel. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiRefreshEditorMenu`

reconstitutes the entire editor menu Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiReleaseDPadPrequeueDown`

intended for ui use only. Releases down for the radial menu quick find prequeue Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiReleaseDPadPrequeueLeft`

intended for ui use only. Releases left for the radial menu quick find prequeue Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiReleaseDPadPrequeueRight`

intended for ui use only. Releases right for the radial menu quick find prequeue Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiReleaseDPadPrequeueUp`

intended for ui use only. Releases up for the radial menu quick find prequeue Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiReleaseDownKeys`

pops up all downed keys. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiReleaseQuickSelectDown`

intended for ui use only. Releases down in the quick select manager Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiReleaseQuickSelectLeft`

intended for ui use only. Releases left in the quick select manager Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiReleaseQuickSelectRight`

intended for ui use only. Releases right in the quick select manager Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiReleaseQuickSelectUp`

intended for ui use only. Releases up in the quick select manager Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiReleaseRadialMenuAltAction`

intended for ui use only.  Releases the page specific action (mainly for hold triggers). Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiReleaseRadialMenuEntry`

intended for ui use only.  Release the currently highlighted option in the radial menu. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiRemoveFromAnyNumberGroup`

removes current selection from any army. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiRemoveSelectedUnit`

Removes specified unit, or selected unit if not ID is specified. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `id`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "id": {
      "type": "integer"
    }
  },
  "required": [
    "id"
  ],
  "additionalProperties": false
}
```

### `editor_uiRemoveSelectionButtonDown`

intended for ui use only.  Indicates that the remove selection button has been pressed. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `isModified`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "isModified": {
      "type": "boolean"
    }
  },
  "required": [
    "isModified"
  ],
  "additionalProperties": false
}
```

### `editor_uiRemoveSelectionButtonUp`

intended for ui use only.  Indicates that the remove selection button has been released. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiRepairAtPointer`

Commands the selected unit(s) to repair the target building at the pointer position. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiResetCameraOrientation`

intended for ui use only.  Reset the rotation and zoom of the camera to default Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiResetInGameCameraRotation`

intended for ui use only.  Reset the Rotation of the in-game (strategy) camera to default Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiResetInGameCameraZoom`

intended for ui use only.  Reset the Zoom of the in-game (strategy) camera to default Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiRollUnitLeft`

intended for ui use only.  Indicates that the roll unit left key has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiRollUnitRight`

intended for ui use only.  Indicates that the roll unit right key has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiRotateClipboard`

rotate the clipboard by amount. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `amount`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "amount": {
      "type": "integer"
    }
  },
  "required": [
    "amount"
  ],
  "additionalProperties": false
}
```

### `editor_uiRotateSelection`

intended for ui use only.  rotates the selected UNIT. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `amount`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "amount": {
      "type": "integer"
    }
  },
  "required": [
    "amount"
  ],
  "additionalProperties": false
}
```

### `editor_uiRotateWaterLeft`

intended for ui use only.  Indicates that the rotate water left button has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiRotateWaterRight`

intended for ui use only.  Indicates that the rotate water right button has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiRoughen`

intended for ui use only.  Indicates that the roughen button has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiSampleCliffElevationAtPointer`

intended for ui use only.  Samples Cliff elevation height at pointer. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSampleElevationAtPointer`

intended for ui use only.  Samples elevation height at pointer. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSampleTerrainAtPointer`

intended for ui use only.  Samples terrain type at pointer. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSampleWaterAtPointer`

intended for ui use only.  Samples water type at pointer. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSaveAsScenarioBrowser`

save a scenario to the scenario directory Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSaveBuiltInPrefab`

save some groups. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `filename`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "filename": {
      "type": "string"
    }
  },
  "required": [
    "filename"
  ],
  "additionalProperties": false
}
```

### `editor_uiSaveScenarioBrowser`

save a scenario to the scenario directory Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `editor_uiSaveScenarioPrompt`

checks for currently-set active player and triggers prompt to user, if it's not set to player 1, before opening save dialog Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `saveAs`, `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "saveAs": {
      "type": "boolean"
    },
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "saveAs",
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `editor_uiSaveTriggers`

save some triggers. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `filename`, `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "filename": {
      "type": "string"
    },
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "filename",
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `editor_uiSaveUserPrefab`

save some groups. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `filename`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "filename": {
      "type": "string"
    }
  },
  "required": [
    "filename"
  ],
  "additionalProperties": false
}
```

### `editor_uiScenarioEndReplay`

intended for ui use only. Hotkey shortcut for replaying current scenario in the scenario end popup. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiScenarioLoad`

load a scenario, checking dirty bit on world. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiScenarioTestMainMenu`

test a scenario into main menu screen. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiScrollBrushSize`

intended for ui use only. Increament the size of the current brush up/down Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `amount`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "amount": {
      "type": "integer"
    }
  },
  "required": [
    "amount"
  ],
  "additionalProperties": false
}
```

### `editor_uiScrollCliffHeight`

intended for ui use only. Increment the height of the cliff brush up/down Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `amount`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "amount": {
      "type": "integer"
    }
  },
  "required": [
    "amount"
  ],
  "additionalProperties": false
}
```

### `editor_uiSeekShelter`

intended for UI use only.  Tasks all valid Town Bell targets to garrison within the nearest building. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSelectAgeUpGod`

select the left god when input bool is true, the right god when it is false Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `left`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "left": {
      "type": "boolean"
    }
  },
  "required": [
    "left"
  ],
  "additionalProperties": false
}
```

### `editor_uiSelectCameraFocusedUnit`

intended for ui use only. Selects any unit that is currently followed by an attached camera. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSelectLocation`

Pans the camera to the location. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSelectNextUnitStack`

Selects the next unit stack. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `reverse`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "reverse": {
      "type": "boolean"
    }
  },
  "required": [
    "reverse"
  ],
  "additionalProperties": false
}
```

### `editor_uiSelectNumberGroup`

selects the units in the given number group. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `group`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "group": {
      "type": "integer"
    }
  },
  "required": [
    "group"
  ],
  "additionalProperties": false
}
```

### `editor_uiSelectRadialMenuEntry`

intended for ui use only.  Select the currently highlighted option in the radial menu. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSelectTransportUnit`

TODO. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSelectType`

selects units of the same type Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `nameListString`, `selectAll`, `snapTo`, `forbidSnap`, `useGamepadExclusionSettings`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "nameListString": {
      "type": "string"
    },
    "selectAll": {
      "type": "boolean"
    },
    "snapTo": {
      "type": "boolean"
    },
    "forbidSnap": {
      "type": "boolean"
    },
    "useGamepadExclusionSettings": {
      "type": "boolean"
    }
  },
  "required": [
    "nameListString",
    "selectAll",
    "snapTo",
    "forbidSnap",
    "useGamepadExclusionSettings"
  ],
  "additionalProperties": false
}
```

### `editor_uiSelectWaterAtPointer`

intended for ui use only.  Selects water at pointer location. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSelectionButtonDown`

intended for ui use only.  Indicates that selection button has been pressed. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSelectionButtonUp`

intended for ui use only.  Indicates that selection button has been released. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSendTribute`

Send tribute in the diplomacy menu. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSetBrushType`

makes the current brush based on the name and parameters. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `brushType`, `p1`, `p2`, `p3`, `p4`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "brushType": {
      "type": "string"
    },
    "p1": {
      "type": "number"
    },
    "p2": {
      "type": "number"
    },
    "p3": {
      "type": "number"
    },
    "p4": {
      "type": "number"
    }
  },
  "required": [
    "brushType",
    "p1",
    "p2",
    "p3",
    "p4"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetBuildingPlacementRender`

controls rendering of the building placement info for the given ID Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `placeID`, `set`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "placeID": {
      "type": "integer"
    },
    "set": {
      "type": "boolean"
    }
  },
  "required": [
    "placeID",
    "set"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetCameraStartLoc`

TODO. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSetClickDragParams`

Set the parameters for click-drag movement. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `deadZoneSize`, `maxZoneSize`, `exponent`, `maxSpeed`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "deadZoneSize": {
      "type": "number"
    },
    "maxZoneSize": {
      "type": "number"
    },
    "exponent": {
      "type": "number"
    },
    "maxSpeed": {
      "type": "number"
    }
  },
  "required": [
    "deadZoneSize",
    "maxZoneSize",
    "exponent",
    "maxSpeed"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetCliffType`

sets the cliff type by name. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    }
  },
  "required": [
    "name"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetCliffTypeNum`

sets the cliff type by index. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `num`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "num": {
      "type": "integer"
    }
  },
  "required": [
    "num"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetClipboardRotation`

sets the clipboard rotation amount. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `degrees`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "degrees": {
      "type": "number"
    }
  },
  "required": [
    "degrees"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetForestType`

sets the forest type by name. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    }
  },
  "required": [
    "name"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetForestTypeNum`

sets the forest type by index. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `num`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "num": {
      "type": "integer"
    }
  },
  "required": [
    "num"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetGatherPointAtGamepadPointer`

Sets the gather points for the selected unit(s) to the pointer position. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSetGatherPointAtGamepadPointerOfNearbyUnitTypes`

Updates the gather point of all nearby units of specified type(s) closest to the currently selected unit. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `targetTypes`, `distance`, `waterSpawnPoint`, `isEconomicGatherPoint`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "targetTypes": {
      "type": "string"
    },
    "distance": {
      "type": "number"
    },
    "waterSpawnPoint": {
      "type": "boolean"
    },
    "isEconomicGatherPoint": {
      "type": "boolean"
    }
  },
  "required": [
    "targetTypes",
    "distance",
    "waterSpawnPoint",
    "isEconomicGatherPoint"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetGatherPointAtPointer`

Sets the gather points for the selected unit(s) to the pointer position. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `waterSpawnPoint`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "waterSpawnPoint": {
      "type": "boolean"
    }
  },
  "required": [
    "waterSpawnPoint"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetGatherPointAtPointerOfNearbyUnitTypes`

Updates the gather point of all nearby units of specified type(s) closest to the currently selected unit. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `targetTypes`, `distance`, `waterSpawnPoint`, `isEconomicGatherPoint`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "targetTypes": {
      "type": "string"
    },
    "distance": {
      "type": "number"
    },
    "waterSpawnPoint": {
      "type": "boolean"
    },
    "isEconomicGatherPoint": {
      "type": "boolean"
    }
  },
  "required": [
    "targetTypes",
    "distance",
    "waterSpawnPoint",
    "isEconomicGatherPoint"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetGatherPointAtPointerOfNearbyUnitTypesToUnit`

Updates the gather point of all nearby units of specified type(s) closest to the currently selected unit, to the position of the selected unit Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `targetUnitID`, `targetTypes`, `distance`, `waterSpawnPoint`, `isEconomicGatherPoint`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "targetUnitID": {
      "type": "integer"
    },
    "targetTypes": {
      "type": "string"
    },
    "distance": {
      "type": "number"
    },
    "waterSpawnPoint": {
      "type": "boolean"
    },
    "isEconomicGatherPoint": {
      "type": "boolean"
    }
  },
  "required": [
    "targetUnitID",
    "targetTypes",
    "distance",
    "waterSpawnPoint",
    "isEconomicGatherPoint"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetKBArmyRender`

render the kbArmy info for the given ID. If not set given, will not render the army. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `id`, `set`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "id": {
      "type": "integer"
    },
    "set": {
      "type": "boolean"
    }
  },
  "required": [
    "id",
    "set"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetKBAttackRouteRender`

render the attackRoute info for the given ID Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `routeID`, `set`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "routeID": {
      "type": "integer"
    },
    "set": {
      "type": "boolean"
    }
  },
  "required": [
    "routeID",
    "set"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetKBResourceRender`

render the kbResource info for the given ID. If not set given, will not render the resource. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `resID`, `set`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "resID": {
      "type": "integer"
    },
    "set": {
      "type": "boolean"
    }
  },
  "required": [
    "resID",
    "set"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetNearestUnitNamedToBuild`

Find the nearest unit to the specified target unit with one of the specified names, and have it start building. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `unitID`, `unitNames`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "unitID": {
      "type": "integer"
    },
    "unitNames": {
      "type": "string"
    }
  },
  "required": [
    "unitID",
    "unitNames"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetPlacementPlayer`

sets current placement player to the given player ID Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `playerID`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "playerID": {
      "type": "integer"
    }
  },
  "required": [
    "playerID"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetProtoCursor`

sets the cursor to a proto-unit. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `proto`, `setPlacement`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "proto": {
      "type": "string"
    },
    "setPlacement": {
      "type": "boolean"
    }
  },
  "required": [
    "proto",
    "setPlacement"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetProtoCursorID`

sets the cursor to a proto-unit. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `protoID`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "protoID": {
      "type": "integer"
    }
  },
  "required": [
    "protoID"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetProtoID`

sets the proto ID to place. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `id`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "id": {
      "type": "integer"
    }
  },
  "required": [
    "id"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetTerrainDetailPaintMode`

Sets the terrain detail paint mode (0) paint overlay (1) paint wetness (2) smooth. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `mode`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "mode": {
      "type": "integer"
    }
  },
  "required": [
    "mode"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetUnitBar`

Set dimensions of unit health bars (Ratio Range: 0-20, Height Range: 0-10, Border, Depth: 0-500) Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `ratio`, `height`, `border`, `depth`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "ratio": {
      "type": "number"
    },
    "height": {
      "type": "number"
    },
    "border": {
      "type": "number"
    },
    "depth": {
      "type": "number"
    }
  },
  "required": [
    "ratio",
    "height",
    "border",
    "depth"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetWaterType`

sets the water type by name. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `name`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string"
    }
  },
  "required": [
    "name"
  ],
  "additionalProperties": false
}
```

### `editor_uiSetWaterTypeNum`

sets the water type by index. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `num`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "num": {
      "type": "integer"
    }
  },
  "required": [
    "num"
  ],
  "additionalProperties": false
}
```

### `editor_uiShowAIDebugInfoArea`

brings up the area info for the given ID Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `areaID`, `renderOn`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "areaID": {
      "type": "integer"
    },
    "renderOn": {
      "type": "integer"
    }
  },
  "required": [
    "areaID",
    "renderOn"
  ],
  "additionalProperties": false
}
```

### `editor_uiShowAIDebugInfoAreaGroup`

brings up the area info for the given ID Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `areaGroupID`, `renderOn`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "areaGroupID": {
      "type": "integer"
    },
    "renderOn": {
      "type": "integer"
    }
  },
  "required": [
    "areaGroupID",
    "renderOn"
  ],
  "additionalProperties": false
}
```

### `editor_uiShowAIDebugInfoAttackRoute`

brings up the attackRoute info for the given ID Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `routeID`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "routeID": {
      "type": "integer"
    }
  },
  "required": [
    "routeID"
  ],
  "additionalProperties": false
}
```

### `editor_uiShowAIDebugInfoBase`

brings up the base info for the given ID Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `playerID`, `baseID`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "playerID": {
      "type": "integer"
    },
    "baseID": {
      "type": "integer"
    }
  },
  "required": [
    "playerID",
    "baseID"
  ],
  "additionalProperties": false
}
```

### `editor_uiShowAIDebugInfoEscrow`

brings up the escrow info for the given ID Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `id`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "id": {
      "type": "integer"
    }
  },
  "required": [
    "id"
  ],
  "additionalProperties": false
}
```

### `editor_uiShowAIDebugInfoKBArmy`

brings up the kbArmy info for the given ID Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `id`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "id": {
      "type": "integer"
    }
  },
  "required": [
    "id"
  ],
  "additionalProperties": false
}
```

### `editor_uiShowAIDebugInfoKBResource`

brings up the kbResource info for the given ID Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `resID`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "resID": {
      "type": "integer"
    }
  },
  "required": [
    "resID"
  ],
  "additionalProperties": false
}
```

### `editor_uiShowAIDebugInfoKBUnit`

brings up the kbunit info for the given ID Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `state`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "state": {
      "type": "integer"
    }
  },
  "required": [
    "state"
  ],
  "additionalProperties": false
}
```

### `editor_uiShowAIDebugInfoKBUnitPick`

brings up the kbUnitPick info for the given ID Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `id`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "id": {
      "type": "integer"
    }
  },
  "required": [
    "id"
  ],
  "additionalProperties": false
}
```

### `editor_uiShowAIDebugInfoPlacement`

brings up the building placement info for the given ID Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `placeID`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "placeID": {
      "type": "integer"
    }
  },
  "required": [
    "placeID"
  ],
  "additionalProperties": false
}
```

### `editor_uiShowAIDebugInfoPlan`

brings up the plan debug text for the given plan ID Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `planID`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "planID": {
      "type": "integer"
    }
  },
  "required": [
    "planID"
  ],
  "additionalProperties": false
}
```

### `editor_uiShowCameraStartLoc`

TODO. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiShowChatWindow`

TODO. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiShowEconomicUI`

Displays Economic UI on command panel, if applicable. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiShowFavorBonusUI`

Shows the UI for spending Favor. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `showPopupOnError`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "showPopupOnError": {
      "type": "boolean"
    }
  },
  "required": [
    "showPopupOnError"
  ],
  "additionalProperties": false
}
```

### `editor_uiShowHelpPopupHistoryTopic`

Opens the help popup with selected history topic. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `topicName`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "topicName": {
      "type": "string"
    }
  },
  "required": [
    "topicName"
  ],
  "additionalProperties": false
}
```

### `editor_uiShowMilitaryUI`

Displays Military UI on command panel, if applicable. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiShowNextAIError`

tries to show the next AI error if there is one. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiShowPreviousAIError`

tries to show the previous AI error if there is one. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSmooth`

intended for ui use only.  Indicates that the smooth button has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiSocketBuild`

performs auto-building for the given socket, if it has valid socket building data. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `unitID`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "unitID": {
      "type": "integer"
    }
  },
  "required": [
    "unitID"
  ],
  "additionalProperties": false
}
```

### `editor_uiSpecialPowerAtPointer`

intended for ui use only.  Use a special power at targeted location. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSpectatorFlipPlayers`

Flip player sides in spectator UI. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSpewDownKeys`

spews all down keys. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiStartScenarioTest`

test a scenario. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiStickRotatePlacedUnit`

intended for ui use only.  Rotate placed unit with gamepad stick -- i.e. intended to be mapped to gamepad stick axis Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `amount`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "amount": {
      "type": "number"
    }
  },
  "required": [
    "amount"
  ],
  "additionalProperties": false
}
```

### `editor_uiStopScenarioTest`

test a scenario. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiStopSelectedUnits`

stop selected units. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSwitchControlsPopup`

Switches pages in the controls popup. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `moveNext`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "moveNext": {
      "type": "boolean"
    }
  },
  "required": [
    "moveNext"
  ],
  "additionalProperties": false
}
```

### `editor_uiSwitchFromChatToDiplomacy`

Closes the chat menu and opens the diplomacy menu. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSwitchFromDiplomacyToChat`

Closes the diplomacy menu and opens the chat menu. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSwitchToGamepadMode`

switch the user input into gamepad mode Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiSwitchToMouseMode`

switch the user input into mouse mode. Param sets last input type to either keyboard or mouse Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `useKeyboard`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "useKeyboard": {
      "type": "boolean"
    }
  },
  "required": [
    "useKeyboard"
  ],
  "additionalProperties": false
}
```

### `editor_uiTaskAllUnitOfTypeToUnitLocation`

Find units of a certain type, and tasks them to the provided target. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `targetUnitID`, `typeNames`, `ignoreTasked`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "targetUnitID": {
      "type": "integer"
    },
    "typeNames": {
      "type": "string"
    },
    "ignoreTasked": {
      "type": "boolean"
    }
  },
  "required": [
    "targetUnitID",
    "typeNames",
    "ignoreTasked"
  ],
  "additionalProperties": false
}
```

### `editor_uiTerrainSelection`

intended for ui use only.  Indicates that the terrain selection button has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiToggleBrushMask`

This is not what you are looking for. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `classID`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "classID": {
      "type": "integer"
    }
  },
  "required": [
    "classID"
  ],
  "additionalProperties": false
}
```

### `editor_uiToggleDiplomacyDialog`

Toggles the diplomacy dialog. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiToggleGame`

turns off and on the game UI. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiToggleGamepadMinimap`

intended for ui use only. Toggles the gamepad minimap open Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `open`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "open": {
      "type": "boolean"
    }
  },
  "required": [
    "open"
  ],
  "additionalProperties": false
}
```

### `editor_uiToggleGarrisonMode`

intended for UI use only.  Sends a toggle garrison mode command for the selected unit. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiToggleGizmoSnapping`

toggles guizmo snapping Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiToggleMinimapEnlarged`

intended for ui use only. Toggles the minimap enlarged state Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiToggleSelectionButton`

intended for ui use only. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiToggleTerrainPasteMode`

This is not what you are looking for. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `mode`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "mode": {
      "type": "integer"
    }
  },
  "required": [
    "mode"
  ],
  "additionalProperties": false
}
```

### `editor_uiToggleUI`

Toggles visibility of main game UI. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiToggleUnitFollowCamera`

intended for ui use only. Tries to set the camera into override panning mode if possible. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `specificUnitIDToToggle`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "specificUnitIDToToggle": {
      "type": "integer"
    }
  },
  "required": [
    "specificUnitIDToToggle"
  ],
  "additionalProperties": false
}
```

### `editor_uiToggleUnitPerspectiveCamera`

intended for ui use only. Tries to set the camera into override attached mode if possible. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `specificUnitIDToToggle`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "specificUnitIDToToggle": {
      "type": "integer"
    }
  },
  "required": [
    "specificUnitIDToToggle"
  ],
  "additionalProperties": false
}
```

### `editor_uiTransformSelectedUnit`

transforms the selected unit into the specified proto unit. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `protoName`, `forceFullyBuilt`, `unitID`, `placementPlayer`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "protoName": {
      "type": "string"
    },
    "forceFullyBuilt": {
      "type": "boolean"
    },
    "unitID": {
      "type": "integer"
    },
    "placementPlayer": {
      "type": "boolean"
    }
  },
  "required": [
    "protoName",
    "forceFullyBuilt",
    "unitID",
    "placementPlayer"
  ],
  "additionalProperties": false
}
```

### `editor_uiTriggerResetParameters`

TODO Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiTriggerResetSounds`

TODO Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiTriggerSelectLocation`

Pans the camera to the location. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiUnSelectWater`

intended for ui use only.  Unselects currently selected water. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiUnbuildSelectedUnit`

enter unbuild mode with selected building. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiUnbuildSelectedUnitAtPointer`

actually unbuild selected building. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiUniformLowerElevation`

uiUniformLowerElevation - lowers the terrain height uniformly in the brush region Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `delta`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "delta": {
      "type": "integer"
    }
  },
  "required": [
    "delta"
  ],
  "additionalProperties": false
}
```

### `editor_uiUniformRaiseElevation`

uiUniformRaiseElevation - raises the terrain height uniformly in the brush region Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `delta`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "delta": {
      "type": "integer"
    }
  },
  "required": [
    "delta"
  ],
  "additionalProperties": false
}
```

### `editor_uiUnitCommandsMenuPress`

intended for ui use only.  Opens the unit commands gamepad menu menu. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiUnitCommandsMenuRelease`

intended for ui use only.  Opens the unit commands gamepad menu menu. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiVillagerPrioritiesPress`

intended for ui use only.  Opens the villager priorities gamepad menu menu. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiVillagerPrioritiesRelease`

intended for ui use only.  Opens the villager priorities gamepad menu menu. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiVillagerReturnResources`

Tell villagers in a defined area to return currently held resources Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `targetUnitID`, `range`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "targetUnitID": {
      "type": "integer"
    },
    "range": {
      "type": "number"
    }
  },
  "required": [
    "targetUnitID",
    "range"
  ],
  "additionalProperties": false
}
```

### `editor_uiWheelRotate`

intended for ui use only.  Rotate with wheel -- i.e. intended to be mapped to wheel Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `amount`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "amount": {
      "type": "integer"
    }
  },
  "required": [
    "amount"
  ],
  "additionalProperties": false
}
```

### `editor_uiWheelRotateCamera`

intended for ui use only.  Rotate with wheel -- i.e. intended to be mapped to wheel Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `amount`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "amount": {
      "type": "integer"
    }
  },
  "required": [
    "amount"
  ],
  "additionalProperties": false
}
```

### `editor_uiWheelRotatePlacedUnit`

intended for ui use only.  Rotate placed unit with wheel -- i.e. intended to be mapped to wheel Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `amount`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "amount": {
      "type": "integer"
    }
  },
  "required": [
    "amount"
  ],
  "additionalProperties": false
}
```

### `editor_uiWorkAtPointer`

intended for ui use only.  Issues "work" at pointer location. mode 1 - ignore when click-drag is enabled, mode 2 - ignore when click-drag is disabled Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `mode`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "mode": {
      "type": "integer"
    }
  },
  "required": [
    "mode"
  ],
  "additionalProperties": false
}
```

### `editor_uiYawUnitLeft`

intended for ui use only.  Indicates that the yaw unit left key has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiYawUnitRight`

intended for ui use only.  Indicates that the yaw unit right key has gone up/down. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `keyState`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "keyState": {
      "type": "integer"
    }
  },
  "required": [
    "keyState"
  ],
  "additionalProperties": false
}
```

### `editor_uiZoomToMinimapEvent`

zooms to the most recent minimap event Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiZoomToMinimapEvent2`

zooms to the most recent minimap event Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_uiZoomToProto`

zooms to the first instance of proto unit X owned by the current player. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `protoname`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "protoname": {
      "type": "string"
    }
  },
  "required": [
    "protoname"
  ],
  "additionalProperties": false
}
```

### `editor_undo`

undoes the last editing operation. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_unitReturnToWork`

Issues a return to work for the selected unit(s). Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```

### `editor_unitSetStance`

Sets the unit stance for all selected units to the given AI stance ID. Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** `stanceID`

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "stanceID": {
      "type": "integer"
    }
  },
  "required": [
    "stanceID"
  ],
  "additionalProperties": false
}
```

### `editor_unitTownBell`

Issues a town bell for the selected unit(s). Native void; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.

**Required arguments:** None.

**MCP annotations:**

```json
{
  "destructiveHint": false,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {},
  "required": [],
  "additionalProperties": false
}
```


## Shipped editor actions

### `action_BrushFunctionsCopyPasteHeight`

Shipped editor UI action: BrushFunctionsCopyPasteHeight. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_BrushFunctionsCopyPasteTexture`

Shipped editor UI action: BrushFunctionsCopyPasteTexture. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_BrushFunctionsCopyPasteWater`

Shipped editor UI action: BrushFunctionsCopyPasteWater. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_BrushFunctionsPlaceGateBtn`

Shipped editor UI action: BrushFunctionsPlaceGateBtn. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_BrushFunctionsPlacePlaceWallBtn`

Shipped editor UI action: BrushFunctionsPlacePlaceWallBtn. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_BrushFunctionsTerrainBrushMask`

Shipped editor UI action: BrushFunctionsTerrainBrushMask. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_BrushFunctionsTerrainEdit`

Shipped editor UI action: BrushFunctionsTerrainEdit. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_BrushFunctionsWaterEdit`

Shipped editor UI action: BrushFunctionsWaterEdit. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_BrushSettingsButton`

Shipped editor UI action: BrushSettingsButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_BrushSettingsCloseButton`

Shipped editor UI action: BrushSettingsCloseButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_CameraStartButton`

Shipped editor UI action: CameraStartButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_CinematicsButton`

Shipped editor UI action: CinematicsButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_CliffPaintButton`

Shipped editor UI action: CliffPaintButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_CombatCommands_AggressiveBtn`

Shipped editor UI action: CombatCommands-AggressiveBtn. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_CombatCommands_AttackMoveBtn`

Shipped editor UI action: CombatCommands-AttackMoveBtn. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_CombatCommands_BellBtn`

Shipped editor UI action: CombatCommands-BellBtn. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_CombatCommands_BoxFormationBtn`

Shipped editor UI action: CombatCommands-BoxFormationBtn. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_CombatCommands_DefensiveBtn`

Shipped editor UI action: CombatCommands-DefensiveBtn. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_CombatCommands_LineFormationBtn`

Shipped editor UI action: CombatCommands-LineFormationBtn. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_CombatCommands_NoAttackBtn`

Shipped editor UI action: CombatCommands-NoAttackBtn. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_CombatCommands_PatrolBtn`

Shipped editor UI action: CombatCommands-PatrolBtn. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_CombatCommands_SpreadFormationBtn`

Shipped editor UI action: CombatCommands-SpreadFormationBtn. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_CombatCommands_StandGroundBtn`

Shipped editor UI action: CombatCommands-StandGroundBtn. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_CombatCommands_StopBtn`

Shipped editor UI action: CombatCommands-StopBtn. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_CombatCommands_WorkBtn`

Shipped editor UI action: CombatCommands-WorkBtn. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_DeleteUnitButton`

Shipped editor UI action: DeleteUnitButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_DiplomacySettingsCloseButton`

Shipped editor UI action: DiplomacySettingsCloseButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_EditWaterButton`

Shipped editor UI action: EditWaterButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ForestButton`

Shipped editor UI action: ForestButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_GroupingSettingsCancelButton`

Shipped editor UI action: GroupingSettingsCancelButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_GroupingSettingsDialog_OKButton`

Shipped editor UI action: GroupingSettingsDialog-OKButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_LandButton`

Shipped editor UI action: LandButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_LetterBoxBars`

Shipped editor UI action: LetterBoxBars. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_LightingButton`

Shipped editor UI action: LightingButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_LoadButton`

Shipped editor UI action: LoadButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_MapElevationCloseButton`

Shipped editor UI action: MapElevationCloseButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_MixButton`

Shipped editor UI action: MixButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_MoveUnitButton`

Shipped editor UI action: MoveUnitButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_NewButton`

Shipped editor UI action: NewButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_NewScenarioCloseButton`

Shipped editor UI action: NewScenarioCloseButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_OceanButton`

Shipped editor UI action: OceanButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_PaintTerrainButton`

Shipped editor UI action: PaintTerrainButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_PlaceUnitButton`

Shipped editor UI action: PlaceUnitButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_PlaytestScenarioCancelButton`

Shipped editor UI action: PlaytestScenarioCancelButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_PlaytestScenarioNOWButton`

Shipped editor UI action: PlaytestScenarioNOWButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_PlaytestScenarioOKButton`

Shipped editor UI action: PlaytestScenarioOKButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_RaiseLowerButton`

Shipped editor UI action: RaiseLowerButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_RedoButton`

Shipped editor UI action: RedoButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_RoughenButton`

Shipped editor UI action: RoughenButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_SampleElevationButton`

Shipped editor UI action: SampleElevationButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_SaveButton`

Shipped editor UI action: SaveButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_SaveButton_2`

Shipped editor UI action: SaveButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ScenarioSummaryCloseButton`

Shipped editor UI action: ScenarioSummaryCloseButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_SmoothButton`

Shipped editor UI action: SmoothButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_TerrainCopyButton`

Shipped editor UI action: TerrainCopyButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_TerrainPasteButton`

Shipped editor UI action: TerrainPasteButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_TriggersButton`

Shipped editor UI action: TriggersButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_UndoButton`

Shipped editor UI action: UndoButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_UnitCopyButton`

Shipped editor UI action: UnitCopyButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_UnitPasteButton`

Shipped editor UI action: UnitPasteButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_WorldLightingCloseButton`

Shipped editor UI action: WorldLightingCloseButton. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_Track_copybtn`

Shipped editor UI action: camEdit-Track-copybtn. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_Track_delbtn`

Shipped editor UI action: camEdit-Track-delbtn. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_Track_downbtn`

Shipped editor UI action: camEdit-Track-downbtn. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_Track_downbtn_2`

Shipped editor UI action: camEdit-Track_downbtn. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_Track_insbtn`

Shipped editor UI action: camEdit-Track-insbtn. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_Track_loadbtn`

Shipped editor UI action: camEdit-Track-loadbtn. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_Track_preview`

Shipped editor UI action: camEdit-Track-preview. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_Track_savebtn`

Shipped editor UI action: camEdit-Track-savebtn. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_Track_upbtn`

Shipped editor UI action: camEdit-Track-upbtn. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_Track_upbtn_2`

Shipped editor UI action: camEdit-Track_upbtn. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_addbtn`

Shipped editor UI action: camEdit-addbtn. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_applybtn`

Shipped editor UI action: camEdit-applybtn. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_backbtn`

Shipped editor UI action: camEdit-backbtn. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_camRotation`

Shipped editor UI action: camEdit-camRotation. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_camStates`

Shipped editor UI action: camEdit-camStates. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_delbtn`

Shipped editor UI action: camEdit-delbtn. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_freeCam`

Shipped editor UI action: camEdit-freeCam. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_fwdbtn`

Shipped editor UI action: camEdit-fwdbtn. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_newbtn`

Shipped editor UI action: camEdit-newbtn. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_pausebtn`

Shipped editor UI action: camEdit-pausebtn. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_playbtn`

Shipped editor UI action: camEdit-playbtn. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_precise_waypoint`

Shipped editor UI action: camEdit-precise-waypoint. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_reset`

Shipped editor UI action: camEdit-reset. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_resetFov`

Shipped editor UI action: camEdit-resetFov. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_resetPitch`

Shipped editor UI action: camEdit-resetPitch. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_resetRot`

Shipped editor UI action: camEdit-resetRot. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_resetZoom`

Shipped editor UI action: camEdit-resetZoom. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_camEdit_stopbtn`

Shipped editor UI action: camEdit-stopbtn. Source ui_camera_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_cameraStates_FreeCam`

Shipped editor UI action: cameraStates-FreeCam. Source ui_camera_states.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_cameraStates_ResetView`

Shipped editor UI action: cameraStates-ResetView. Source ui_camera_states.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_cameraStates_Rotate`

Shipped editor UI action: cameraStates-Rotate. Source ui_camera_states.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_LocSelect_esc`

Shipped editor UI action: Hotkey esc; context LocSelect. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_LocSelect_mouse1down`

Shipped editor UI action: Hotkey mouse1down; context LocSelect. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_LocSelect_mouse2up`

Shipped editor UI action: Hotkey mouse2up; context LocSelect. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_TerrainPaste_esc`

Shipped editor UI action: Hotkey esc; context TerrainPaste. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_TerrainPaste_mouse1up`

Shipped editor UI action: Hotkey mouse1up; context TerrainPaste. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_TerrainPaste_mouse2up`

Shipped editor UI action: Hotkey mouse2up; context TerrainPaste. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_UnitPaste_esc`

Shipped editor UI action: Hotkey esc; context UnitPaste. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_UnitPaste_mouse1up`

Shipped editor UI action: Hotkey mouse1up; context UnitPaste. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_UnitPaste_mousez`

Shipped editor UI action: Hotkey mousez; context UnitPaste. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_camtrack_esc`

Shipped editor UI action: Hotkey esc; context camtrack. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_convertunits_esc`

Shipped editor UI action: Hotkey esc; context convertunits. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_convertunits_mouse1down`

Shipped editor UI action: Hotkey mouse1down; context convertunits. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_convertunits_mouse1up`

Shipped editor UI action: Hotkey mouse1up; context convertunits. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_copy_control_v`

Shipped editor UI action: Hotkey control-v; context copy. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_copy_esc`

Shipped editor UI action: Hotkey esc; context copy. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_copy_mouse1down`

Shipped editor UI action: Hotkey mouse1down; context copy. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_copy_mouse1up`

Shipped editor UI action: Hotkey mouse1up; context copy. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_definegrouping_control_v`

Shipped editor UI action: Hotkey control-v; context definegrouping. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_definegrouping_esc`

Shipped editor UI action: Hotkey esc; context definegrouping. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_definegrouping_mouse1down`

Shipped editor UI action: Hotkey mouse1down; context definegrouping. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_definegrouping_mouse1up`

Shipped editor UI action: Hotkey mouse1up; context definegrouping. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_deleteunits_esc`

Shipped editor UI action: Hotkey esc; context deleteunits. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_deleteunits_mouse1down`

Shipped editor UI action: Hotkey mouse1down; context deleteunits. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_deleteunits_mouse1up`

Shipped editor UI action: Hotkey mouse1up; context deleteunits. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_detailHelp__`

Shipped editor UI action: Hotkey .; context detailHelp. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_0`

Shipped editor UI action: Hotkey 0; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_1`

Shipped editor UI action: Hotkey 1; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_2`

Shipped editor UI action: Hotkey 2; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_3`

Shipped editor UI action: Hotkey 3; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_4`

Shipped editor UI action: Hotkey 4; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_5`

Shipped editor UI action: Hotkey 5; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_6`

Shipped editor UI action: Hotkey 6; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_7`

Shipped editor UI action: Hotkey 7; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_8`

Shipped editor UI action: Hotkey 8; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_9`

Shipped editor UI action: Hotkey 9; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor__`

Shipped editor UI action: Hotkey [; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor___2`

Shipped editor UI action: Hotkey ]; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor__alt_shift_y`

Shipped editor UI action: Hotkey +alt-shift-y; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor__alt_y`

Shipped editor UI action: Hotkey +alt-y; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor__k`

Shipped editor UI action: Hotkey +k; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor__shift_k`

Shipped editor UI action: Hotkey +shift-k; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor__shift_space`

Shipped editor UI action: Hotkey +shift-space; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor__shift_y`

Shipped editor UI action: Hotkey +shift-y; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor__space`

Shipped editor UI action: Hotkey +space; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor__y`

Shipped editor UI action: Hotkey +y; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_alt_1`

Shipped editor UI action: Hotkey alt-1; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_alt_2`

Shipped editor UI action: Hotkey alt-2; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_alt_3`

Shipped editor UI action: Hotkey alt-3; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_alt_4`

Shipped editor UI action: Hotkey alt-4; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_alt_a`

Shipped editor UI action: Hotkey alt-a; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_alt_b`

Shipped editor UI action: Hotkey alt-b; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_alt_c`

Shipped editor UI action: Hotkey alt-c; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_alt_mousez`

Shipped editor UI action: Hotkey alt-mousez; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_alt_r`

Shipped editor UI action: Hotkey alt-r; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_alt_u`

Shipped editor UI action: Hotkey alt-u; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_alt_v`

Shipped editor UI action: Hotkey alt-v; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_b`

Shipped editor UI action: Hotkey b; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_c`

Shipped editor UI action: Hotkey c; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_0`

Shipped editor UI action: Hotkey control-0; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_1`

Shipped editor UI action: Hotkey control-1; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_2`

Shipped editor UI action: Hotkey control-2; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_3`

Shipped editor UI action: Hotkey control-3; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_4`

Shipped editor UI action: Hotkey control-4; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_5`

Shipped editor UI action: Hotkey control-5; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_6`

Shipped editor UI action: Hotkey control-6; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_7`

Shipped editor UI action: Hotkey control-7; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_8`

Shipped editor UI action: Hotkey control-8; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_9`

Shipped editor UI action: Hotkey control-9; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control__`

Shipped editor UI action: Hotkey control-,; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control___2`

Shipped editor UI action: Hotkey control-.; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_c`

Shipped editor UI action: Hotkey control-c; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_f`

Shipped editor UI action: Hotkey control-f; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_f1`

Shipped editor UI action: Hotkey control-f1; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_f10`

Shipped editor UI action: Hotkey control-f10; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_f11`

Shipped editor UI action: Hotkey control-f11; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_f12`

Shipped editor UI action: Hotkey control-f12; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_f2`

Shipped editor UI action: Hotkey control-f2; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_f3`

Shipped editor UI action: Hotkey control-f3; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_f4`

Shipped editor UI action: Hotkey control-f4; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_f5`

Shipped editor UI action: Hotkey control-f5; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_f6`

Shipped editor UI action: Hotkey control-f6; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_f7`

Shipped editor UI action: Hotkey control-f7; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_f8`

Shipped editor UI action: Hotkey control-f8; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_f9`

Shipped editor UI action: Hotkey control-f9; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_h`

Shipped editor UI action: Hotkey control-h; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_k`

Shipped editor UI action: Hotkey control-k; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_l`

Shipped editor UI action: Hotkey control-l; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_mouse1down`

Shipped editor UI action: Hotkey control-mouse1down; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_mouse1up`

Shipped editor UI action: Hotkey control-mouse1up; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_mousez`

Shipped editor UI action: Hotkey control-mousez; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_n`

Shipped editor UI action: Hotkey control-n; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_r`

Shipped editor UI action: Hotkey control-r; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_s`

Shipped editor UI action: Hotkey control-s; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_shift_s`

Shipped editor UI action: Hotkey control-shift-s; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_v`

Shipped editor UI action: Hotkey control-v; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_y`

Shipped editor UI action: Hotkey control-y; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_control_z`

Shipped editor UI action: Hotkey control-z; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_delete`

Shipped editor UI action: Hotkey delete; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_e`

Shipped editor UI action: Hotkey e; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_f`

Shipped editor UI action: Hotkey f; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_g`

Shipped editor UI action: Hotkey g; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_i`

Shipped editor UI action: Hotkey i; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_l`

Shipped editor UI action: Hotkey l; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_mouse1down`

Shipped editor UI action: Hotkey mouse1down; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_mouse1up`

Shipped editor UI action: Hotkey mouse1up; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_mouse3down`

Shipped editor UI action: Hotkey mouse3down; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_mouse3up`

Shipped editor UI action: Hotkey mouse3up; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_o`

Shipped editor UI action: Hotkey o; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_s`

Shipped editor UI action: Hotkey s; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_shift_0`

Shipped editor UI action: Hotkey shift-0; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_shift_c`

Shipped editor UI action: Hotkey shift-c; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_shift_e`

Shipped editor UI action: Hotkey shift-e; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_shift_f1`

Shipped editor UI action: Hotkey shift-f1; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_shift_f10`

Shipped editor UI action: Hotkey shift-f10; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_shift_f11`

Shipped editor UI action: Hotkey shift-f11; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_shift_f12`

Shipped editor UI action: Hotkey shift-f12; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_shift_f2`

Shipped editor UI action: Hotkey shift-f2; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_shift_f3`

Shipped editor UI action: Hotkey shift-f3; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_shift_f4`

Shipped editor UI action: Hotkey shift-f4; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_shift_f5`

Shipped editor UI action: Hotkey shift-f5; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_shift_f6`

Shipped editor UI action: Hotkey shift-f6; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_shift_f7`

Shipped editor UI action: Hotkey shift-f7; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_shift_f8`

Shipped editor UI action: Hotkey shift-f8; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_shift_f9`

Shipped editor UI action: Hotkey shift-f9; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_shift_l`

Shipped editor UI action: Hotkey shift-l; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_shift_mouse3down`

Shipped editor UI action: Hotkey shift-mouse3down; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_shift_mouse3up`

Shipped editor UI action: Hotkey shift-mouse3up; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_shift_mousez`

Shipped editor UI action: Hotkey shift-mousez; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_shift_r`

Shipped editor UI action: Hotkey shift-r; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_shift_s`

Shipped editor UI action: Hotkey shift-s; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_shift_w`

Shipped editor UI action: Hotkey shift-w; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_t`

Shipped editor UI action: Hotkey t; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_u`

Shipped editor UI action: Hotkey u; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_v`

Shipped editor UI action: Hotkey v; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editor_w`

Shipped editor UI action: Hotkey w; context editor. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editwater_esc`

Shipped editor UI action: Hotkey esc; context editwater. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_editwater_mouse1down`

Shipped editor UI action: Hotkey mouse1down; context editwater. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_elevation_esc`

Shipped editor UI action: Hotkey esc; context elevation. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_elevation_mouse1down`

Shipped editor UI action: Hotkey mouse1down; context elevation. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_elevation_mouse1up`

Shipped editor UI action: Hotkey mouse1up; context elevation. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_elevation_mouse2down`

Shipped editor UI action: Hotkey mouse2down; context elevation. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_elevation_mouse2up`

Shipped editor UI action: Hotkey mouse2up; context elevation. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_elevationsample_esc`

Shipped editor UI action: Hotkey esc; context elevationsample. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_elevationsample_mouse1down`

Shipped editor UI action: Hotkey mouse1down; context elevationsample. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_elevationsample_mouse1up`

Shipped editor UI action: Hotkey mouse1up; context elevationsample. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_elevationsample_mouse2down`

Shipped editor UI action: Hotkey mouse2down; context elevationsample. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_list_esc`

Shipped editor UI action: Hotkey esc; context list. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_modifyterrain___`

Shipped editor UI action: Hotkey +[; context modifyterrain. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_modifyterrain____2`

Shipped editor UI action: Hotkey +]; context modifyterrain. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_modifyterrain__alt__`

Shipped editor UI action: Hotkey +alt-[; context modifyterrain. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_modifyterrain__alt___2`

Shipped editor UI action: Hotkey +alt-]; context modifyterrain. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_modifyterrain_control_space`

Shipped editor UI action: Hotkey control-space; context modifyterrain. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_modifyterrain_esc`

Shipped editor UI action: Hotkey esc; context modifyterrain. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_modifyterrain_mouse1down`

Shipped editor UI action: Hotkey mouse1down; context modifyterrain. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_modifyterrain_mouse1up`

Shipped editor UI action: Hotkey mouse1up; context modifyterrain. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_modifyterrain_space`

Shipped editor UI action: Hotkey space; context modifyterrain. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__a`

Shipped editor UI action: Hotkey +a; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__alt_1`

Shipped editor UI action: Hotkey +alt-1; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__alt_2`

Shipped editor UI action: Hotkey +alt-2; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__alt_3`

Shipped editor UI action: Hotkey +alt-3; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__alt_4`

Shipped editor UI action: Hotkey +alt-4; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__alt_a`

Shipped editor UI action: Hotkey +alt-a; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__alt_arrowdown`

Shipped editor UI action: Hotkey +alt-arrowdown; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__alt_arrowleft`

Shipped editor UI action: Hotkey +alt-arrowleft; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__alt_arrowright`

Shipped editor UI action: Hotkey +alt-arrowright; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__alt_arrowup`

Shipped editor UI action: Hotkey +alt-arrowup; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__alt_control_arrowdown`

Shipped editor UI action: Hotkey +alt-control-arrowdown; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__alt_control_arrowdown_2`

Shipped editor UI action: Hotkey +alt-control-arrowdown; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__alt_control_arrowleft`

Shipped editor UI action: Hotkey +alt-control-arrowleft; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__alt_control_arrowleft_2`

Shipped editor UI action: Hotkey +alt-control-arrowleft; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__alt_control_arrowright`

Shipped editor UI action: Hotkey +alt-control-arrowright; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__alt_control_arrowright_2`

Shipped editor UI action: Hotkey +alt-control-arrowright; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__alt_control_arrowup`

Shipped editor UI action: Hotkey +alt-control-arrowup; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__alt_control_arrowup_2`

Shipped editor UI action: Hotkey +alt-control-arrowup; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__alt_d`

Shipped editor UI action: Hotkey +alt-d; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__alt_d_2`

Shipped editor UI action: Hotkey +alt-d; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__alt_s`

Shipped editor UI action: Hotkey +alt-s; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__alt_s_2`

Shipped editor UI action: Hotkey +alt-s; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__alt_w`

Shipped editor UI action: Hotkey +alt-w; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__arrowdown`

Shipped editor UI action: Hotkey +arrowdown; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__arrowleft`

Shipped editor UI action: Hotkey +arrowleft; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__arrowright`

Shipped editor UI action: Hotkey +arrowright; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__arrowup`

Shipped editor UI action: Hotkey +arrowup; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__control_arrowdown`

Shipped editor UI action: Hotkey +control-arrowdown; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__control_arrowdown_2`

Shipped editor UI action: Hotkey +control-arrowdown; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__control_arrowleft`

Shipped editor UI action: Hotkey +control-arrowleft; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__control_arrowleft_2`

Shipped editor UI action: Hotkey +control-arrowleft; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__control_arrowright`

Shipped editor UI action: Hotkey +control-arrowright; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__control_arrowright_2`

Shipped editor UI action: Hotkey +control-arrowright; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__control_arrowup`

Shipped editor UI action: Hotkey +control-arrowup; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__control_arrowup_2`

Shipped editor UI action: Hotkey +control-arrowup; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__d`

Shipped editor UI action: Hotkey +d; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__d_2`

Shipped editor UI action: Hotkey +d; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__s`

Shipped editor UI action: Hotkey +s; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__s_2`

Shipped editor UI action: Hotkey +s; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit__w`

Shipped editor UI action: Hotkey +w; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit_esc`

Shipped editor UI action: Hotkey esc; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit_mouse1down`

Shipped editor UI action: Hotkey mouse1down; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit_mouse1up`

Shipped editor UI action: Hotkey mouse1up; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_moveunit_mousez`

Shipped editor UI action: Hotkey mousez; context moveunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_paint_alt_mouse1down`

Shipped editor UI action: Hotkey alt-mouse1down; context paint. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_paint_alt_mouse1down_2`

Shipped editor UI action: Hotkey alt-mouse1down; context paint. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_paint_esc`

Shipped editor UI action: Hotkey esc; context paint. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_paint_mouse1down`

Shipped editor UI action: Hotkey mouse1down; context paint. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_paint_mouse1up`

Shipped editor UI action: Hotkey mouse1up; context paint. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_paint_mouse2down`

Shipped editor UI action: Hotkey mouse2down; context paint. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_paint_mouse2up`

Shipped editor UI action: Hotkey mouse2up; context paint. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_paintcliff_esc`

Shipped editor UI action: Hotkey esc; context paintcliff. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_paintcliff_mouse1down`

Shipped editor UI action: Hotkey mouse1down; context paintcliff. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_paintcliff_mouse2down`

Shipped editor UI action: Hotkey mouse2down; context paintcliff. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_paintcliff_mouse2up`

Shipped editor UI action: Hotkey mouse2up; context paintcliff. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_paintforest_esc`

Shipped editor UI action: Hotkey esc; context paintforest. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_paintforest_mouse1down`

Shipped editor UI action: Hotkey mouse1down; context paintforest. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_paintforest_mouse1up`

Shipped editor UI action: Hotkey mouse1up; context paintforest. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_paintwater_c`

Shipped editor UI action: Hotkey c; context paintwater. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_paintwater_d`

Shipped editor UI action: Hotkey d; context paintwater. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_paintwater_esc`

Shipped editor UI action: Hotkey esc; context paintwater. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_paintwater_mouse1doubleup`

Shipped editor UI action: Hotkey mouse1doubleup; context paintwater. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_paintwater_mouse1down`

Shipped editor UI action: Hotkey mouse1down; context paintwater. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_paintwater_mouse2down`

Shipped editor UI action: Hotkey mouse2down; context paintwater. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_paintwater_mouse2up`

Shipped editor UI action: Hotkey mouse2up; context paintwater. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_placeTradeRoute_esc`

Shipped editor UI action: Hotkey esc; context placeTradeRoute. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_placeTradeRoute_mouse1up`

Shipped editor UI action: Hotkey mouse1up; context placeTradeRoute. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_placeTradeRoute_mouse2up`

Shipped editor UI action: Hotkey mouse2up; context placeTradeRoute. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_placeWall_esc`

Shipped editor UI action: Hotkey esc; context placeWall. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_placeWall_mouse1doubledown`

Shipped editor UI action: Hotkey mouse1doubledown; context placeWall. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_placeWall_mouse1doubleup`

Shipped editor UI action: Hotkey mouse1doubleup; context placeWall. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_placeWall_mouse1down`

Shipped editor UI action: Hotkey mouse1down; context placeWall. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_placeWall_shift_mouse1down`

Shipped editor UI action: Hotkey shift-mouse1down; context placeWall. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_placeunit_esc`

Shipped editor UI action: Hotkey esc; context placeunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_placeunit_mouse1down`

Shipped editor UI action: Hotkey mouse1down; context placeunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_placeunit_mouse2down`

Shipped editor UI action: Hotkey mouse2down; context placeunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_placeunit_mousez`

Shipped editor UI action: Hotkey mousez; context placeunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_placeunit_shift_mouse1down`

Shipped editor UI action: Hotkey shift-mouse1down; context placeunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_placeunit_shift_mouse2down`

Shipped editor UI action: Hotkey shift-mouse2down; context placeunit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_placeunitselect_alt_w`

Shipped editor UI action: Hotkey alt-w; context placeunitselect. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_placeunitselect_esc`

Shipped editor UI action: Hotkey esc; context placeunitselect. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_recalcvariation_esc`

Shipped editor UI action: Hotkey esc; context recalcvariation. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_recalcvariation_mouse1down`

Shipped editor UI action: Hotkey mouse1down; context recalcvariation. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_recalcvariation_mouse1up`

Shipped editor UI action: Hotkey mouse1up; context recalcvariation. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_roughen_esc`

Shipped editor UI action: Hotkey esc; context roughen. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_roughen_mouse1down`

Shipped editor UI action: Hotkey mouse1down; context roughen. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_roughen_mouse1up`

Shipped editor UI action: Hotkey mouse1up; context roughen. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_selectTransportUnit_esc`

Shipped editor UI action: Hotkey esc; context selectTransportUnit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_selectTransportUnit_mouse1up`

Shipped editor UI action: Hotkey mouse1up; context selectTransportUnit. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_smooth_esc`

Shipped editor UI action: Hotkey esc; context smooth. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_smooth_mouse1down`

Shipped editor UI action: Hotkey mouse1down; context smooth. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_smooth_mouse1up`

Shipped editor UI action: Hotkey mouse1up; context smooth. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_smooth_mouse2down`

Shipped editor UI action: Hotkey mouse2down; context smooth. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_smooth_mouse2up`

Shipped editor UI action: Hotkey mouse2up; context smooth. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_smooth_shift_space`

Shipped editor UI action: Hotkey shift-space; context smooth. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_trigger_esc`

Shipped editor UI action: Hotkey esc; context trigger. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_triggroups_esc`

Shipped editor UI action: Hotkey esc; context triggroups. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_trigrectselect_esc`

Shipped editor UI action: Hotkey esc; context trigrectselect. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_trigrectselect_mouse1down`

Shipped editor UI action: Hotkey mouse1down; context trigrectselect. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_trigrectselect_mouse1up`

Shipped editor UI action: Hotkey mouse1up; context trigrectselect. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_trigrectselect_mouse2up`

Shipped editor UI action: Hotkey mouse2up; context trigrectselect. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_trigselect_esc`

Shipped editor UI action: Hotkey esc; context trigselect. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_trigselect_mouse1down`

Shipped editor UI action: Hotkey mouse1down; context trigselect. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_trigselect_mouse2up`

Shipped editor UI action: Hotkey mouse2up; context trigselect. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_key_world_control_shift_f10`

Shipped editor UI action: Hotkey control-shift-f10; context world. Source editor.con. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_mapsize_CancelBtn`

Shipped editor UI action: mapsize_CancelBtn. Source ui_map_size_dlg.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_pitchEdit_CinematicsButton`

Shipped editor UI action: pitchEdit-CinematicsButton. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_pitchEdit_bias_10`

Shipped editor UI action: pitchEdit-bias-10. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_pitchEdit_bias_110`

Shipped editor UI action: pitchEdit-bias-110. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_pitchEdit_bias_135`

Shipped editor UI action: pitchEdit-bias-135. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_pitchEdit_bias_15`

Shipped editor UI action: pitchEdit-bias-15. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_pitchEdit_bias_25`

Shipped editor UI action: pitchEdit-bias-25. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_pitchEdit_bias_35`

Shipped editor UI action: pitchEdit-bias-35. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_pitchEdit_bias_5`

Shipped editor UI action: pitchEdit-bias-5. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_pitchEdit_bias_55`

Shipped editor UI action: pitchEdit-bias-55. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_pitchEdit_bias_70`

Shipped editor UI action: pitchEdit-bias-70. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_pitchEdit_bias_90`

Shipped editor UI action: pitchEdit-bias-90. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_pitchEdit_close`

Shipped editor UI action: pitchEdit-close. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_pitchEdit_defaultOrientation`

Shipped editor UI action: pitchEdit-defaultOrientation. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_pitchEdit_freeCam`

Shipped editor UI action: pitchEdit-freeCam. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_pitchEdit_normalpitch`

Shipped editor UI action: pitchEdit-normalpitch. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_pitchEdit_normalzoom`

Shipped editor UI action: pitchEdit-normalzoom. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_pitchEdit_resetPitch`

Shipped editor UI action: pitchEdit-resetPitch. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_pitchEdit_rotate`

Shipped editor UI action: pitchEdit-rotate. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_pitchEdit_zoom20`

Shipped editor UI action: pitchEdit-zoom20. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_pitchEdit_zoom5`

Shipped editor UI action: pitchEdit-zoom5. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_pitchEdit_zoomdefault`

Shipped editor UI action: pitchEdit-zoomdefault. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_trEdit_LoadAllTriggers`

Shipped editor UI action: trEdit-LoadAllTriggers. Source ui_trigger_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_trEdit_SaveAllTriggers`

Shipped editor UI action: trEdit-SaveAllTriggers. Source ui_trigger_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_61`

Shipped editor UI action: ui_editor_menu_item_61. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_62`

Shipped editor UI action: ui_editor_menu_item_62. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_63`

Shipped editor UI action: ui_editor_menu_item_63. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_64`

Shipped editor UI action: ui_editor_menu_item_64. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_65`

Shipped editor UI action: ui_editor_menu_item_65. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_66`

Shipped editor UI action: ui_editor_menu_item_66. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_67`

Shipped editor UI action: ui_editor_menu_item_67. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_68`

Shipped editor UI action: ui_editor_menu_item_68. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_69`

Shipped editor UI action: ui_editor_menu_item_69. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_70`

Shipped editor UI action: ui_editor_menu_item_70. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_71`

Shipped editor UI action: ui_editor_menu_item_71. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_72`

Shipped editor UI action: ui_editor_menu_item_72. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_73`

Shipped editor UI action: ui_editor_menu_item_73. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_74`

Shipped editor UI action: ui_editor_menu_item_74. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_75`

Shipped editor UI action: ui_editor_menu_item_75. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_76`

Shipped editor UI action: ui_editor_menu_item_76. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_77`

Shipped editor UI action: ui_editor_menu_item_77. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_78`

Shipped editor UI action: ui_editor_menu_item_78. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_79`

Shipped editor UI action: ui_editor_menu_item_79. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_80`

Shipped editor UI action: ui_editor_menu_item_80. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_81`

Shipped editor UI action: ui_editor_menu_item_81. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_82`

Shipped editor UI action: ui_editor_menu_item_82. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_83`

Shipped editor UI action: ui_editor_menu_item_83. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_84`

Shipped editor UI action: ui_editor_menu_item_84. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_85`

Shipped editor UI action: ui_editor_menu_item_85. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_86`

Shipped editor UI action: ui_editor_menu_item_86. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_87`

Shipped editor UI action: ui_editor_menu_item_87. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_88`

Shipped editor UI action: ui_editor_menu_item_88. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_89`

Shipped editor UI action: ui_editor_menu_item_89. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_editor_menu_item_90`

Shipped editor UI action: ui_editor_menu_item_90. Source ui_editor_menu.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_pitch_editor_item_21`

Shipped editor UI action: ui_pitch_editor_item_21. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_pitch_editor_item_22`

Shipped editor UI action: ui_pitch_editor_item_22. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_pitch_editor_item_23`

Shipped editor UI action: ui_pitch_editor_item_23. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_pitch_editor_item_24`

Shipped editor UI action: ui_pitch_editor_item_24. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_pitch_editor_item_25`

Shipped editor UI action: ui_pitch_editor_item_25. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_pitch_editor_item_26`

Shipped editor UI action: ui_pitch_editor_item_26. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_pitch_editor_item_27`

Shipped editor UI action: ui_pitch_editor_item_27. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_pitch_editor_item_28`

Shipped editor UI action: ui_pitch_editor_item_28. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_pitch_editor_item_29`

Shipped editor UI action: ui_pitch_editor_item_29. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_pitch_editor_item_30`

Shipped editor UI action: ui_pitch_editor_item_30. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```

### `action_ui_pitch_editor_item_31`

Shipped editor UI action: ui_pitch_editor_item_31. Source ui_pitch_editor.xml. Execute its original command expression; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.

**Required arguments:** `confirmDestructive`

**MCP annotations:**

```json
{
  "destructiveHint": true,
  "idempotentHint": false,
  "openWorldHint": false,
  "readOnlyHint": false
}
```

**Input schema:**

```json
{
  "type": "object",
  "properties": {
    "confirmDestructive": {
      "type": "boolean",
      "const": true
    }
  },
  "required": [
    "confirmDestructive"
  ],
  "additionalProperties": false
}
```
