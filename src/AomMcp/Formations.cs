using System.Text.Json;

namespace AomMcp;

/// <summary>Plans bounded client-pixel rows/rings and reuses guarded one-shot placement with independently observed IDs.</summary>
internal static class Formations
{
    // Host work/pixel ceilings, not engine limits. Count matches one batch's maximum work budget.
    internal const int MaxCount = 32, MaxPixel = 65535, MaxSpacing = 10000;
    internal sealed record Point(int X, int Y);
    internal sealed record Result(bool Preview, Point[] Points, int Attempted, int Verified, bool StoppedOnError,
        object[] Steps, string? Error, string Limitation, bool Atomic = false);

    internal static Point[] Plan(JsonElement args)
    {
        var shape = args.GetProperty("shape").GetString();
        var count = args.GetProperty("count").GetInt32();
        var spacing = args.GetProperty("spacingPixels").GetInt32();
        var x = args.GetProperty("x").GetInt32(); var y = args.GetProperty("y").GetInt32();
        if (shape is not ("rows" or "ring") || count is < 1 or > MaxCount || spacing is < 1 or > MaxSpacing
            || x is < 0 or > MaxPixel || y is < 0 or > MaxPixel)
            throw new ArgumentException("Formation shape/count/spacing/anchor outside host bounds.");
        if (string.IsNullOrWhiteSpace(args.GetProperty("proto").GetString())) throw new ArgumentException("Empty formation prototype.");
        Catalog.Quote(args.GetProperty("proto").GetString()!); // Validate typed native string before any work.
        var columns = args.TryGetProperty("columns", out var c) ? c.GetInt32() : (int)Math.Ceiling(Math.Sqrt(count));
        if (columns < 1 || columns > count || (shape == "ring" && args.TryGetProperty("columns", out _)))
            throw new ArgumentException("Rows columns must be 1..count; ring does not accept columns.");
        var rows = (count + columns - 1) / columns; // Integer ceiling division gives number of grid rows.
        var points = new List<Point>();
        for (var i = 0; i < count; i++)
        {
            double dx, dy;
            if (shape == "rows")
            {
                var row = i / columns;
                var rowCount = Math.Min(columns, count - row * columns);
                dx = (i % columns - (rowCount - 1) / 2.0) * spacing; // Center each row on anchor, including incomplete last row.
                dy = (row - (rows - 1) / 2.0) * spacing;
            }
            else
            {
                // Euclidean chord spacing s=2*r*sin(pi/n); one object lies at center. Full turn is 2*pi radians.
                var radius = count == 1 ? 0 : spacing / (2 * Math.Sin(Math.PI / count));
                var angle = 2 * Math.PI * i / count;
                dx = radius * Math.Cos(angle); dy = radius * Math.Sin(angle);
            }
            var point = new Point(checked((int)Math.Round(x + dx, MidpointRounding.AwayFromZero)),
                checked((int)Math.Round(y + dy, MidpointRounding.AwayFromZero)));
            if (point.X is < 0 or > MaxPixel || point.Y is < 0 or > MaxPixel)
                throw new ArgumentException("Formation contains out-of-bound client-pixel point; no placement requested.");
            points.Add(point);
        }
        if (points.Distinct().Count() != count) throw new ArgumentException("Pixel rounding collapses formation points; increase spacing.");
        if (!Preview(args)) EditorFiles.Confirm(args, "confirmPlacement");
        return points.ToArray();
    }

    internal static bool Preview(JsonElement args) => !args.TryGetProperty("preview", out var value) || value.GetBoolean();
    internal static Result PreviewResult(JsonElement args) => new(true, Plan(args), 0, 0, false, [], null,
        "Plan only; no game connection/input. Full-resolution client pixels, not world distance. Actual client/map hover/occlusion not checked. Rows centered on anchor; ring spacing is neighbor chord before pixel rounding.");

    internal static LiveUnits.Unit ObservedNewUnit(LiveUnits.Unit[] before, LiveUnits.Unit[] after, string proto, int player)
    {
        var prior = before.Select(u => u.UnitId).ToHashSet();
        var current = after.Select(u => u.UnitId).ToHashSet();
        if (!prior.IsSubsetOf(current)) throw new InvalidDataException("Scene objects disappeared during formation; stop, never infer placement order IDs.");
        var added = after.Where(u => !prior.Contains(u.UnitId)).ToArray();
        if (added.Length != 1 || added[0].Player != player || !string.Equals(added[0].Proto, proto, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Expected exactly one new matching live object; placement outcome not verified. Do not retry.");
        return added[0];
    }

    /// <summary>Places planned points once each, stops on first error and reports partial progress without rollback/retry.</summary>
    /// <param name="game">Guarded editor connection with independently reviewed live-unit layout.</param>
    /// <param name="args">Validated plan and explicit nonpreview placement confirmation.</param>
    /// <param name="place">Existing Server.Place implementation, including cursor cleanup and all native guards.</param>
    /// <returns>Actual observed IDs/properties, partial effects and an explicit error marker if stopped.</returns>
    public static Result Place(Game game, JsonElement args, Func<JsonElement, object> place)
    {
        var plan = Plan(args);
        _ = game.Editor();
        Win.Check(Win.GetClientRect(game.Window, out var rect), "GetClientRect");
        if (plan.Any(p => p.X >= rect.Right || p.Y >= rect.Bottom))
            throw new ArgumentException("Formation point outside actual client; entire plan refused before placement.");
        var proto = args.GetProperty("proto").GetString()!;
        var player = args.TryGetProperty("player", out var p) ? p.GetInt32() : 1; // Same host default as single placement; Gaia is zero.
        var before = LiveUnits.Read(game); // Refuse unknown read layouts before any placement.
        var steps = new List<object>();
        var attempted = 0;
        string? error = null;
        foreach (var point in plan)
        {
            try
            {
                Win.Check(Win.GetClientRect(game.Window, out var current), "GetClientRect");
                if (current.Right != rect.Right || current.Bottom != rect.Bottom)
                    throw new InvalidOperationException("Client size changed; formation stopped.");
                attempted++;
                _ = place(JsonSerializer.SerializeToElement(new { proto, player, x = point.X, y = point.Y }));
                var after = LiveUnits.Read(game);
                var unit = ObservedNewUnit(before, after, proto, player);
                steps.Add(new { point, unit.UnitId, unit.Proto, unit.Player, unit.Position, unit.Health, unit.MaxHealth });
                before = after;
            }
            catch (Exception e) { error = e.Message; break; }
        }
        return new(false, plan, attempted, steps.Count, error is not null, steps.ToArray(), error,
            "Non-atomic screen-space formation; existing Place guards/cleanup retained. Each successful step observes exactly one new full live ID, never guessed from order. Concurrent edits/async effects can cause refusal after placement. Earlier effects remain; no automatic retry/undo. Pixel spacing is not world spacing; actual positions returned. Scenario not saved.");
    }

    /// <summary>Checks row/ring geometry and full-ID observation using synthetic inputs only.</summary>
    public static void SelfTest()
    {
        // Synthetic 100px anchor, four members at 20px spacing; two columns. Not engine defaults.
        var rows = JsonSerializer.SerializeToElement(new { proto = "Hoplite", shape = "rows", count = 4, spacingPixels = 20, x = 100, y = 100, columns = 2 });
        var plan = Plan(rows);
        if (!plan.SequenceEqual(new[] { new Point(90, 90), new Point(110, 90), new Point(90, 110), new Point(110, 110) }))
            throw new InvalidOperationException("Rows fixture failed.");
        var ring = Plan(JsonSerializer.SerializeToElement(new { proto = "Hoplite", shape = "ring", count = 2, spacingPixels = 20, x = 100, y = 100 }));
        if (!ring.SequenceEqual(new[] { new Point(110, 100), new Point(90, 100) })) throw new InvalidOperationException("Ring fixture failed.");
        // Synthetic generation-bearing ID400 is deliberately unrelated to placement index.
        var unit = new LiveUnits.Unit(400, 0, "Hoplite", 1, new(1, 0, 1), 100, 100);
        if (ObservedNewUnit([], [unit], "hoplite", 1).UnitId != 400) throw new InvalidOperationException("Formation ID fixture failed.");
        try { ObservedNewUnit([unit], [unit], "Hoplite", 1); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Formation unchanged-scene fixture did not refuse.");
    }
}
