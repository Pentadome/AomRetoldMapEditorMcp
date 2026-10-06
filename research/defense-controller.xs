// Inline XS: Code Snippet effect; active, looping, Always condition.
// First editor-placed object is TownCenter, scenario unit ID 0.
if (trQuestVarGet("defense_done") == 0) {
    if (trQuestVarGet("defense_init") == 0) {
        trDisableConquestCheck(true);
        trPlayerSetDiplomacy(1, 2, "Enemy", true);
        trPlayerSetName(1, "Base Defenders");
        trPlayerSetName(2, "Invading Army");
        trPlayerGrantResources(1, "Food", 1000);
        trPlayerGrantResources(1, "Wood", 1000);
        trPlayerGrantResources(1, "Gold", 1000);
        trPlayerGrantResources(1, "Favor", 100);
        trQuestVarSet("defense_start", xsGetTime());
        trQuestVarSet("defense_init", 1);
        trUnitSelectClear();
        trUnitSelectByID(0);
        trCameraCutToUnit();
        trUnitSelectClear();
        trChatSend(1, "BASE DEFENSE: Protect your Town Center. Three waves at 10, 50 and 90 seconds. Clear the last wave to win.");
    }
    if (kbUnitTypeCount("TownCenter", 1, cUnitStateAlive) == 0) {
        trChatSend(1, "Your Town Center fell. Defense failed.");
        trQuestVarSet("defense_done", 1);
        trPlayerSetDefeated(1);
    } else {
        int elapsed = xsGetTime() - trQuestVarGet("defense_start");
        int wave = trQuestVarGet("defense_wave");
        if ((wave == 0 && elapsed >= 10) || (wave == 1 && elapsed >= 50) || (wave == 2 && elapsed >= 90)) {
            wave = wave + 1;
            vector base = trUnitGetPosition(0);
            float x = base.x;
            float z = base.z;
            if (wave == 1) { z = z + 32; }
            if (wave == 2) { x = x + 32; }
            if (wave == 3) { z = z - 32; }
            int count = 6 + 6 * wave;
            for (int i = 0; i < count; i++) {
                // Trigger templates consume percent-delimited placeholders; avoid modulo syntax.
                int col = i - 6 * (i / 6);
                string proto = "Hoplite";
                if (col == 1 || col == 4) { proto = "Toxotes"; }
                if (wave > 1 && col == 5) { proto = "Minotaur"; }
                int id = trUnitCreate(proto, x + col * 2 - 5, base.y, z + (i / 6) * 2, 180, 2, true);
                if (id >= 0) {
                    trUnitSelectClear();
                    trUnitSelectByID(id);
                    trUnitSetStance("Aggressive");
                    trUnitMoveToUnit(0, -1, true, false, 1.0);
                }
            }
            trQuestVarSet("defense_wave", wave);
            trChatSend(1, "WAVE " + wave + " / 3: " + count + " attackers incoming!");
        }
        int remaining = kbUnitTypeCount("Hoplite", 2, cUnitStateAlive) + kbUnitTypeCount("Toxotes", 2, cUnitStateAlive) + kbUnitTypeCount("Minotaur", 2, cUnitStateAlive);
        if (wave == 3 && remaining == 0) {
            trChatSend(1, "All three waves defeated. Your base survived!");
            trQuestVarSet("defense_done", 1);
            trPlayerSetVictorious(1);
        }
    }
}
