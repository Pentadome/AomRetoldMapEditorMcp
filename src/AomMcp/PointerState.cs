using System.Diagnostics;
using System.Numerics;

namespace AomMcp;

/// <summary>Reviewed pointer/placement-cursor fields (read-only); see research/LIVE-WORLD.md "Pointer and placement cursor".</summary>
/// <param name="CursorProtoOffset">Pointer-state int: placement-cursor prototype ID, -1 without cursor (0x443800 compares
/// <c>[ptr+0xd20]</c> with 0; live: 526 while a BerryBush cursor is set).</param>
/// <param name="CursorObjectOffset">Pointer-state pointer to the cursor object transform (0x443800 reads <c>[ptr+0xd38]</c>).</param>
/// <param name="CursorTranslationOffsets">X/Y/Z of the cursor transform (0x443800 copies +0xc/+0x1c/+0x2c); this is the
/// position <c>uiPlaceAtPointer</c> uses while a cursor prototype is set.</param>
/// <param name="PickRayOffset">Game-object member: current pointer pick-ray direction (0x443800 and
/// <c>uiSampleTerrainAtPointer</c> pass <c>game+0x100</c> to the terrain ray cast).</param>
/// <param name="Signatures">Reviewed code fragments of 0x443800 and uiSampleTerrainAtPointer.</param>
public sealed record PointerReadLayout(
    int CursorProtoOffset,
    int CursorObjectOffset,
    int[] CursorTranslationOffsets,
    int PickRayOffset,
    UnitReadSignature[] Signatures
);

/// <summary>Passive reads of the engine pointer state: hover byte, placement cursor and pick ray.</summary>
internal static class PointerState
{
    /// <summary>Host read bound for layout offsets, not an engine object size.</summary>
    const int MaxOffset = 65536;

    /// <summary>binary32 width (ABI).</summary>
    const int FloatSize = 4;

    /// <summary>Vector component count of a world position or direction.</summary>
    const int Components = 3;

    /// <summary>Pick-ray match tolerance in client pixels (host choice: the angle of this many pixels at the target).</summary>
    const int RayPixelTolerance = 2;

    /// <summary>Host poll interval while waiting for pointer state.</summary>
    const int PollMs = 10;

    /// <summary>Host bound on the whole hover wait (live: ray ≤54 ms, cursor one frame later at ~30 fps).</summary>
    const int TimeoutMs = 2000;

    /// <summary>Minimum wait after the ray matches before the cursor is trusted; the former fixed placement settle
    /// (Server.Place slept 150 ms), kept as a floor because the cursor may have been set just before.</summary>
    const int MinCursorWaitMs = 150;

    /// <summary>Cursor wait in multiples of the observed ray latency (live: cursor follows one frame after the ray;
    /// two latencies cover one frame at the observed frame rate).</summary>
    const int CursorWaitLatencies = 2;

    /// <summary>Default cursor-vs-target tolerance in tiles (live: grid snap moved BerryBush ≤1.41 units = 0.7 tiles).</summary>
    const double CursorSnapTiles = 2;

    /// <summary>Fallback total settle time of <see cref="Ui.Settle"/> (two moves, 250 ms each).</summary>
    const int SettleFallbackMs = 500;

    /// <summary>Result of <see cref="HoverMap"/>.</summary>
    /// <param name="RayConfirmed">Engine pick ray matched the target pixel's ray.</param>
    /// <param name="CursorProto">Placement-cursor prototype, -1 without cursor.</param>
    /// <param name="Cursor">Final placement-cursor position (null without cursor or layout).</param>
    /// <param name="CursorNear">Cursor within tolerance of <paramref name="Expected"/>.</param>
    /// <param name="CursorChanged">Cursor position differs from before the move (fresh, not the previous spot).</param>
    /// <param name="Expected">Host ground hit of the target pixel.</param>
    /// <param name="MapHover">Engine map-hover byte after the wait.</param>
    /// <param name="WaitedMs">Wait duration.</param>
    /// <param name="Limitation">Set when only timed settling was possible.</param>
    internal sealed record Hover(
        bool RayConfirmed,
        int CursorProto,
        Vector3? Cursor,
        bool CursorNear,
        bool CursorChanged,
        Vector3? Expected,
        bool MapHover,
        int WaitedMs,
        string? Limitation
    )
    {
        /// <summary>True when a placement cursor still sits at its previous, distant position (placing would land there).</summary>
        internal bool CursorStale =>
            CursorProto >= 0 && Cursor is not null && !CursorNear && !CursorChanged;

        /// <summary>Compact report object for tool output.</summary>
        internal object Report() =>
            new
            {
                rayConfirmed = RayConfirmed,
                cursor = Cursor is { } c ? new[] { Math.Round(c.X, 2), Math.Round(c.Z, 2) } : null,
                cursorNear = CursorProto >= 0 ? CursorNear : (bool?)null,
                hover = MapHover,
                waitedMs = WaitedMs,
                limitation = Limitation,
            };
    }

    /// <summary>Moves the pointer to a client pixel over the map and waits until the engine follows: pick ray equals the
    /// pixel's ray, then (with a placement cursor) the cursor object is near the pixel's ground hit or has moved.</summary>
    /// <param name="game">Connected editor.</param>
    /// <param name="x">Client pixel X.</param>
    /// <param name="y">Client pixel Y.</param>
    /// <param name="cursorTolerance">Cursor-vs-ground-hit tolerance in world units; default <see cref="CursorSnapTiles"/> tiles.</param>
    /// <returns>What was confirmed; callers refuse on <see cref="Hover.CursorStale"/>.</returns>
    /// <remarks>Live 2026-10-22 (research/LIVE-WORLD.md "Pointer and placement cursor"): uiPlaceAtPointer uses the cursor
    /// object, which follows one frame after the ray; a fixed 150 ms sleep placed at the previous spot at low frame rates.</remarks>
    internal static Hover HoverMap(Game game, int x, int y, double? cursorTolerance = null)
    {
        if (!Available(game) || game.Layout.Map is null)
        {
            Ui.Settle(game, x, y);
            return new(
                false,
                -1,
                null,
                false,
                false,
                null,
                false,
                SettleFallbackMs,
                "No reviewed pointer layout for this build: timed settle only (two moves, 250 ms each)."
            );
        }
        var view = EditorView.ReadView(game);
        var expectedRay = view.RayAt(x, y);
        var (width, _) = Ui.ClientSize(game);
        var side = x + RayPixelTolerance < width ? x + RayPixelTolerance : x - RayPixelTolerance;
        var maxAngle = Angle(expectedRay, view.RayAt(side, y));
        var expected = view.GroundHit(expectedRay);
        var tolerance = cursorTolerance ?? CursorSnapTiles * view.Scale;
        var before = Read(game);
        var watch = Stopwatch.StartNew();
        game.Move(x, y);
        long? rayMs = null;
        var now = before;
        bool near = false,
            changed = false;
        while (watch.ElapsedMilliseconds < TimeoutMs)
        {
            now = Read(game);
            if (rayMs is null && Angle(expectedRay, now.PickRay) <= maxAngle)
                rayMs = watch.ElapsedMilliseconds;
            near =
                expected is { } e
                && now.Cursor is { } c
                && Math.Sqrt(Math.Pow(c.X - e.X, 2) + Math.Pow(c.Z - e.Z, 2)) <= tolerance;
            changed = now.Cursor != before.Cursor;
            if (
                rayMs is { } r
                && watch.ElapsedMilliseconds
                    >= r + Math.Max(MinCursorWaitMs, CursorWaitLatencies * r)
                && (now.CursorProto < 0 || now.Cursor is null || near)
            )
                break;
            Thread.Sleep(PollMs);
        }
        return new(
            rayMs is not null,
            now.CursorProto,
            now.CursorProto >= 0 ? now.Cursor : null,
            near,
            changed,
            expected,
            now.Hover,
            (int)watch.ElapsedMilliseconds,
            rayMs is null ? "Engine pick ray did not reach the target pixel within the wait." : null
        );
    }

    /// <summary>Angle between two directions in radians.</summary>
    /// <param name="a">First direction.</param>
    /// <param name="b">Second direction.</param>
    static double Angle(Vector3 a, Vector3 b)
    {
        var la = a.Length();
        var lb = b.Length();
        if (la < float.Epsilon || lb < float.Epsilon)
            return Math.PI;
        return Math.Acos(Math.Clamp(Vector3.Dot(a, b) / (la * lb), -1, 1));
    }

    /// <summary>One pointer-state reading.</summary>
    /// <param name="Hover">Map-hover byte (bridge guard input); written only when the engine resolves the pointer
    /// (placement/brush cursor per frame, uiPlaceAtPointer), so it can be stale in edit mode None.</param>
    /// <param name="CursorProto">Placement-cursor prototype ID, -1 without cursor.</param>
    /// <param name="Cursor">Placement-cursor world position, null without cursor object.</param>
    /// <param name="PickRay">Current pick-ray direction.</param>
    internal sealed record Reading(bool Hover, int CursorProto, Vector3? Cursor, Vector3 PickRay);

    /// <summary>Validates offsets and signature bounds of a loaded layout.</summary>
    /// <param name="l">Pointer layout.</param>
    internal static void ValidateLayout(PointerReadLayout l)
    {
        if (
            l.Signatures is not { Length: > 0 }
            || l.CursorTranslationOffsets is not { Length: Components }
            || new[] { l.CursorProtoOffset, l.CursorObjectOffset, l.PickRayOffset }
                .Concat(l.CursorTranslationOffsets)
                .Any(o => o is < 0 or > MaxOffset)
        )
            throw new InvalidDataException("Pointer layout incomplete or unbounded.");
    }

    /// <summary>True when the connected layout has reviewed pointer fields.</summary>
    /// <param name="game">Connected game.</param>
    internal static bool Available(Game game) => game.Layout.Cursor is not null;

    /// <summary>Reads hover byte, cursor proto/position and pick ray (signatures checked first).</summary>
    /// <param name="game">Connected editor.</param>
    /// <returns>Current reading.</returns>
    internal static Reading Read(Game game)
    {
        var l =
            game.Layout.Cursor
            ?? throw new InvalidDataException("No reviewed pointer layout for this build.");
        LiveUnits.ValidateRuntime(game, l.Signatures);
        var gameObject = game.Pointer(game.Base + checked((int)game.Layout.EditorGlobalRva));
        var pointer = game.Pointer(game.Base + checked((int)game.Layout.PointerGlobalRva));
        if (gameObject == 0 || pointer == 0)
            throw new InvalidDataException("Pointer state unavailable.");
        var hover = game.Read(pointer + checked((int)game.Layout.HoverOffset), 1)[0] != 0;
        var proto = (int)game.UInt(pointer + l.CursorProtoOffset);
        var cursorObject = game.Pointer(pointer + l.CursorObjectOffset);
        Vector3? cursor = null;
        if (cursorObject != 0)
        {
            var c = l.CursorTranslationOffsets.Select(o => Float(game, cursorObject + o)).ToArray();
            cursor = new Vector3(c[0], c[1], c[2]);
        }
        var ray = game.Read(gameObject + l.PickRayOffset, Components * FloatSize);
        return new(
            hover,
            proto,
            cursor,
            new Vector3(
                BitConverter.ToSingle(ray, 0),
                BitConverter.ToSingle(ray, FloatSize),
                BitConverter.ToSingle(ray, 2 * FloatSize)
            )
        );
    }

    /// <summary>Reads one binary32 value.</summary>
    /// <param name="game">Connected game.</param>
    /// <param name="address">Absolute address.</param>
    static float Float(Game game, nint address) =>
        BitConverter.ToSingle(game.Read(address, FloatSize));
}
