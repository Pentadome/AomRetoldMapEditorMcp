using System.Text.Json;

namespace AomMcp;

/// <summary>Connection-free script diagnostics tools: editor_xs_diagnostics (MythLog / trigtemp.xs readers).</summary>
internal sealed partial class Server
{
    static readonly HashSet<string> ScriptNames = new(StringComparer.Ordinal)
    {
        XsDiagnostics.Tool,
        TriggerElements.Tool,
        XsCheck.Tool,
        HelpTools.HelpTool,
        HelpTools.DigestTool,
    };

    static void PreflightScript(string name, JsonElement args)
    {
        if (name == XsDiagnostics.Tool)
            XsDiagnostics.Preflight(args);
        else if (name == TriggerElements.Tool)
            TriggerElements.Preflight(args);
        else if (name == XsCheck.Tool)
            XsCheck.Preflight(args);
        else if (name == HelpTools.HelpTool)
            HelpTools.PreflightHelp(args);
        else if (name == HelpTools.DigestTool)
            HelpTools.PreflightDigest(args);
    }

    object InvokeScript(string name, JsonElement args) =>
        name == TriggerElements.Tool ? TriggerElements.Query(args, exe, _layout.ExeSha256)
        : name == XsCheck.Tool ? XsCheck.Query(args, exe)
        : name == HelpTools.HelpTool ? HelpTools.Help(args)
        : name == HelpTools.DigestTool ? HelpTools.Digest(args, exe)
        : XsDiagnostics.Query(args, exe);

    static IEnumerable<object> ScriptExtras()
    {
        yield return Spec(
            XsDiagnostics.Tool,
            "Read the game's own XS compile errors without screenshots. operation=errors (default): parses MythLog.txt (<Games>/Age of Mythology Retold/temp/Logs; override logPath=MythLog*.txt), groups each AI/XS error chain, merges the per-player repeats (occurrences, players, compiledFiles), names the failing function/rule, adds a short hint and a source excerpt (context lines, default 3) resolved under profile Game/AI, INSTALLPATH/game/ai or the profile trigger dir, flagged modifiedAfterLog when the file changed after the log. loads[] lists scenario loads of this game session (log lines carry no wall-clock time); load=latest (default)|latestWithErrors|all or loadIndex selects; offset/limit page diagnostics (limit 1..100, default 20). operation=trigtemp: indexes trigger/trigtemp.xs, the game's output of its LAST trigger compile: list rules (filter, offset, limit), or one of trigger (rule or trigger name, or numeric id with triggerPath), line (compiled line to rule), eventId (handler to enabled rule); triggerPath+expectedSha256 (hash-pinned export) maps rules to trigger IDs and reports staleness; bodyOffset/maxLines page a rule body. Zero errors does not prove scripts compiled (AI scripts compile when their player's AI loads). Read-only, no game connection, nothing is compiled or written.",
            new Dictionary<string, object>
            {
                ["operation"] = new { type = "string", @enum = XsDiagnostics.Operations },
                ["logPath"] = new { type = "string" },
                ["load"] = new { type = "string", @enum = XsDiagnostics.LoadModes },
                ["loadIndex"] = new { type = "integer", minimum = 0 },
                ["context"] = new
                {
                    type = "integer",
                    minimum = 0,
                    maximum = XsDiagnostics.MaxContext,
                },
                ["trigger"] = new { type = "string" },
                ["line"] = new { type = "integer", minimum = 1 },
                ["eventId"] = new { type = "integer", minimum = 0 },
                ["triggerPath"] = new { type = "string" },
                ["expectedSha256"] = new { type = "string" },
                ["filter"] = new { type = "string" },
                ["bodyOffset"] = new { type = "integer", minimum = 0 },
                ["maxLines"] = new
                {
                    type = "integer",
                    minimum = 1,
                    maximum = XsDiagnostics.MaxBodyLines,
                },
                ["profileDirectory"] = new { type = "string" },
                ["offset"] = new { type = "integer", minimum = 0 },
                ["limit"] = new
                {
                    type = "integer",
                    minimum = 1,
                    maximum = XsDiagnostics.MaxLimit,
                },
            },
            [],
            true
        );
        yield return Spec(
            HelpTools.HelpTool,
            "Topical workflow guides (no game connection). Call without arguments to list topics; topic=<name> returns the guide (catalog, dialogs, placement, triggers, players, workflows, campaign, xs, ui). Holds the detailed guidance that used to live in AGENTS.md.",
            new Dictionary<string, object> { ["topic"] = new { type = "string" } },
            [],
            true
        );
        yield return Spec(
            HelpTools.DigestTool,
            "One compact summary of a saved scenario: players (civ, control, AI path and whether it resolves, missingIncludes, allies/enemies), the scenario's Triggers -> XS Scripts list (resolvedFrom, missingIncludes) and trigger counts by group, Code Snippet and victory/defeat trigger names (from a hash-pinned TR export when triggerPath is given, else the scenario's embedded TR). path=\"latest\" = newest save in the active profile scenario folder; expectedSha256 optional (hash echoed). Saved files only; no game connection, no runtime proof. Use editor_players / editor_trigger_list / editor_player_dependency_audit for detail.",
            new Dictionary<string, object>
            {
                ["path"] = new { type = "string" },
                ["expectedSha256"] = new { type = "string" },
                ["triggerPath"] = new { type = "string" },
                ["expectedTriggerSha256"] = new { type = "string" },
            },
            ["path"],
            true
        );
        yield return Spec(
            XsCheck.Tool,
            "HEURISTIC offline XS lint (stock-calibrated: method calls like list.size() and extern globals in includes are normal) (not a compiler; no findings proves nothing). File mode (path=.xs): the file plus its includes. Scenario mode (scenarioPath=.mythscn or \"latest\" = newest save in the profile scenario folder; optional expectedSha256): every player's AI script (PL aiPath) and every Triggers->XS Scripts entry, each with its include closure, resolved like the game (AI: profile Game/AI then INSTALLPATH/game/ai; trigger scripts: profile trigger folder then the stock scripts in Data.bar); missing scripts are errors (scenario-script-missing) with similar names, stock files are not linted. Checks: unbalanced braces/parens, for-loop-form (legacy `for (i = 0; < n)` header, MythLog 0172/0139, with a C-style rewrite), member access in for-loop conditions, rule without active/inactive, wrong argument counts and unknown calls against editor_xs_api's model, syscall-wrong-context (aifuncs in a trigger script and vice versa; context=auto from the path: ...\\trigger\\ = trigger, ...\\game\\ai\\ = ai), include-not-found with detail.resolvedAgainst and detail.suggestions. Returns {target, counts, diagnostics[{file,line,col,rule,severity,message,mythLogCode?,detail?}]} (errors first). MythLog (editor_xs_diagnostics) is authoritative after the game loads the script; trigger-script compile errors appear only as an in-game dialog.",
            new Dictionary<string, object>
            {
                ["path"] = new { type = "string" },
                ["scenarioPath"] = new { type = "string" },
                ["expectedSha256"] = new { type = "string" },
                ["context"] = new { type = "string", @enum = XsCheck.ContextModes },
                ["offset"] = new { type = "integer", minimum = 0 },
                ["limit"] = new
                {
                    type = "integer",
                    minimum = 1,
                    maximum = XsCheck.MaxLimit,
                },
            },
            [],
            true
        );
        yield return Spec(
            TriggerElements.Tool,
            "Look up the shipped trigger condition/effect catalog (trigger_data.xml, from editor_generate_catalog) offline: exact names, parameters with vartype, defaults, which parameters editor_trigger_edit can replace, the XS command/expression templates. With path+expectedSha256 (hash-pinned TR export) each entry also lists up to 5 existing exemplars (triggerId, elementIndex, current args) of that element type, which editor_trigger_edit can byte-copy via duplicates/copyEffects. library=true adds install-corpus exemplars (generated/trigger_exemplars.json from editor_generate_catalog: shipped TR v12 campaign scenarios, file relative to game/, sha256, triggerId, elementIndex) and per-parameter encodingProven. It never builds element bytes. kind=any|effect|condition; name exact, filter substring; limit 1..50 (default 10), offset. Read-only, no game connection.",
            new Dictionary<string, object>
            {
                ["kind"] = new { type = "string", @enum = TriggerElements.Kinds },
                ["name"] = new { type = "string" },
                ["filter"] = new { type = "string" },
                ["path"] = new { type = "string" },
                ["expectedSha256"] = new { type = "string" },
                ["library"] = new { type = "boolean" },
                ["offset"] = new { type = "integer", minimum = 0 },
                ["limit"] = new
                {
                    type = "integer",
                    minimum = 1,
                    maximum = TriggerElements.MaxLimit,
                },
            },
            [],
            true
        );
    }
}
