namespace AomMcp;

/// <summary>Optional passive runtime metadata. No fields are enabled without independent build-specific review.</summary>
public sealed record RuntimeReadLayout
{
    /// <summary>Gets supported metadata schema version.</summary>
    public int Version { get; init; } = 1;
    /// <summary>Gets independently corroborated executable hash.</summary>
    public string ExeSha256 { get; init; } = "";
    /// <summary>Gets reviewed runtime modes, distinct from editor-only unit layouts.</summary>
    public string[] Modes { get; init; } = [];
    /// <summary>Gets independent XS/debug observation evidence hash.</summary>
    public string XsEvidenceSha256 { get; init; } = "";
    /// <summary>Gets passive memory observation evidence hash.</summary>
    public string MemoryEvidenceSha256 { get; init; } = "";
    /// <summary>Gets candidate action field offset; never discovered by guessing.</summary>
    public uint ActionOffset { get; init; }
    /// <summary>Gets candidate target field offset; never discovered by guessing.</summary>
    public uint TargetOffset { get; init; }
    /// <summary>Gets candidate generation/version field offset for identity/race checks.</summary>
    public uint GenerationOffset { get; init; }
}

internal static class RuntimeTelemetry
{
    // Deliberately empty. Reviewed editor IDs/positions do not establish runtime action/target/plan layouts.
    internal static bool Available => false;
    internal static object Capabilities() => new { available = false, workerActions = false, workerTargets = false, aiPlans = false,
        reason = "No independently corroborated build/mode-specific runtime layout. Query/read access only; no debugger, memory writes or native getter dispatch." };
    internal static void ValidateLayout(RuntimeReadLayout layout, string exeHash)
    {
        ValidateDefinition(layout, exeHash);
        throw new InvalidDataException("Runtime telemetry metadata is not registered as independently reviewed. Do not enable candidate offsets.");
    }
    internal static void ValidateDefinition(RuntimeReadLayout layout, string exeHash)
    {
        static bool Hash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
        if (layout.Version != 1 || layout.ExeSha256 != exeHash || !Hash(exeHash) || !Hash(layout.XsEvidenceSha256) || !Hash(layout.MemoryEvidenceSha256)
            || layout.XsEvidenceSha256 == layout.MemoryEvidenceSha256 || layout.Modes.Length != 1 || layout.Modes[0] != "playtest"
            || layout.ActionOffset is 0 or > 4096 || layout.TargetOffset is 0 or > 4096 || layout.GenerationOffset is 0 or > 4096
            || layout.ActionOffset % 4 != 0 || layout.TargetOffset % 4 != 0 || layout.GenerationOffset % 4 != 0
            || layout.ActionOffset == layout.TargetOffset || layout.ActionOffset == layout.GenerationOffset || layout.TargetOffset == layout.GenerationOffset)
            throw new InvalidDataException("Incomplete/stale/unbounded runtime telemetry candidate; independent evidence, exact mode and distinct aligned fields required.");
    }
    internal sealed record Observation(ulong Pointer, uint Id, uint Player, uint Proto, uint Generation, int Action, int Target);
    internal static Observation Stable(Func<Observation> read)
    {
        var before = read(); var after = read();
        if (before.Pointer == 0 || before.Id > int.MaxValue || before.Player > 12 || before.Proto > int.MaxValue || before != after)
            throw new InvalidDataException("Runtime identity/value/generation changed during passive read; observation unavailable, no retry.");
        return before;
    }
    internal static void SelfTest()
    {
        var sample = new Observation(0x1000, 701, 6, 12, 8, 9, 801); _ = Stable(() => sample);
        var turn = 0;
        try { _ = Stable(() => ++turn == 1 ? sample : sample with { Generation = 9 }); throw new InvalidOperationException("Torn telemetry accepted."); } catch (InvalidDataException) { }
        turn = 0; try { _ = Stable(() => ++turn == 1 ? sample : sample with { Id = 702 }); throw new InvalidOperationException("Reused telemetry identity accepted."); } catch (InvalidDataException) { }
        var candidate = new RuntimeReadLayout { ExeSha256 = new('a', 64), XsEvidenceSha256 = new('b', 64), MemoryEvidenceSha256 = new('c', 64), Modes = ["playtest"], ActionOffset = 4, TargetOffset = 8, GenerationOffset = 12 };
        ValidateDefinition(candidate, candidate.ExeSha256);
        try { ValidateLayout(candidate, candidate.ExeSha256); throw new InvalidOperationException("Unreviewed telemetry enabled."); } catch (InvalidDataException) { }
        try { ValidateDefinition(candidate with { ExeSha256 = new('d', 64) }, candidate.ExeSha256); throw new InvalidOperationException("Stale telemetry build accepted."); } catch (InvalidDataException) { }
    }
}
