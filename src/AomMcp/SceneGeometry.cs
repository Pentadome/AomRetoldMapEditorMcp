using System.Text.Json;

namespace AomMcp;

/// <summary>Pure world-space planning helpers for scene tools; no game access, fully self-tested.</summary>
internal static class SceneGeometry
{
    internal const int MaxLayoutItems = 32, MaxFootprintItems = 64;
    internal sealed record WorldPoint(double X, double Z);
    internal sealed record Rect(double MinX, double MinZ, double MaxX, double MaxZ)
    {
        internal bool Overlaps(Rect o) => MinX < o.MaxX && o.MinX < MaxX && MinZ < o.MaxZ && o.MinZ < MaxZ;
        internal bool Inside(double width, double depth) => MinX >= 0 && MinZ >= 0 && MaxX <= width && MaxZ <= depth;
        internal static Rect Around(double x, double z, double rx, double rz, double margin) =>
            new(x - rx - margin, z - rz - margin, x + rx + margin, z + rz + margin);
    }

    /// <summary>Alternative-UI minimap diamond model: world (x,z) to client pixel.</summary>
    /// <remarks>Screen up = +X+Z; screen right = +X-Z (measured). Closed loop corrects residual scale error.</remarks>
    internal static (double X, double Y) MinimapPixel(double x, double z, double width, double depth, double cx, double cy, double half)
    {
        double u = (x - width / 2) / width, v = (z - depth / 2) / depth;
        return (cx + half * (u - v), cy - half * (u + v));
    }

    /// <summary>Pixel correction for a world-space residual using the same linear minimap model.</summary>
    internal static (double DX, double DY) MinimapDelta(double ex, double ez, double width, double depth, double half) =>
        (half * (ex / width - ez / depth), -half * (ex / width + ez / depth));

    /// <summary>Clamps a pixel into the minimap diamond (L1 ball) with an inner margin.</summary>
    internal static (int X, int Y) ClampDiamond(double x, double y, double cx, double cy, double half, double margin)
    {
        double dx = x - cx, dy = y - cy, limit = Math.Max(1, half - margin), l1 = Math.Abs(dx) + Math.Abs(dy);
        if (l1 > limit) { dx *= limit / l1; dy *= limit / l1; }
        return ((int)Math.Round(cx + dx, MidpointRounding.AwayFromZero), (int)Math.Round(cy + dy, MidpointRounding.AwayFromZero));
    }

    /// <summary>World-unit rows/ring formation; rows run along +X, successive rows along +Z, then rotated.</summary>
    internal static WorldPoint[] Formation(string shape, int count, double spacing, double x, double z, int? columns, double angleDegrees)
    {
        if (shape is not ("rows" or "ring") || count is < 1 or > MaxLayoutItems || !(spacing > 0) || spacing > 1000)
            throw new ArgumentException("Formation shape/count/spacing outside host bounds (count 1..32, spacing (0,1000] world units).");
        if (shape == "ring" && columns is not null) throw new ArgumentException("Ring does not accept columns.");
        var cols = columns ?? (int)Math.Ceiling(Math.Sqrt(count));
        if (cols < 1 || cols > count) throw new ArgumentException("Rows columns must be 1..count.");
        var rows = (count + cols - 1) / cols;
        var angle = angleDegrees * Math.PI / 180;
        var points = new WorldPoint[count];
        for (var i = 0; i < count; i++)
        {
            double dx, dz;
            if (shape == "rows")
            {
                var row = i / cols;
                var rowCount = Math.Min(cols, count - row * cols);
                dx = (i % cols - (rowCount - 1) / 2.0) * spacing;
                dz = (row - (rows - 1) / 2.0) * spacing;
            }
            else
            {
                var radius = count == 1 ? 0 : spacing / (2 * Math.Sin(Math.PI / count)); // Neighbor chord equals spacing.
                var a = 2 * Math.PI * i / count;
                dx = radius * Math.Cos(a); dz = radius * Math.Sin(a);
            }
            points[i] = new(x + dx * Math.Cos(angle) - dz * Math.Sin(angle), z + dx * Math.Sin(angle) + dz * Math.Cos(angle));
        }
        return points;
    }

    internal sealed record Change(string Kind, int? UnitId, int? PreviousUnitId, LiveUnits.Unit? Before, LiveUnits.Unit? After, double? Moved);

    /// <summary>Compares two object snapshots by full ID; pairs removed/added same proto/player/position as reidentified.</summary>
    internal static Change[] Diff(LiveUnits.Unit[] before, LiveUnits.Unit[] after, double tolerance)
    {
        var old = before.ToDictionary(u => u.UnitId);
        var now = after.ToDictionary(u => u.UnitId);
        double Distance(LiveUnits.Unit a, LiveUnits.Unit b) =>
            Math.Sqrt(Math.Pow(a.Position.X - b.Position.X, 2) + Math.Pow(a.Position.Y - b.Position.Y, 2) + Math.Pow(a.Position.Z - b.Position.Z, 2));
        var changes = new List<Change>();
        foreach (var (id, a) in now)
            if (old.TryGetValue(id, out var b))
            {
                var moved = Distance(a, b);
                if (b.ProtoId != a.ProtoId || b.Player != a.Player)
                    changes.Add(new("changed", id, null, b, a, moved));
                else if (moved > tolerance)
                    changes.Add(new("moved", id, null, b, a, moved));
                else if (Math.Abs(b.Health - a.Health) > 0.001 || Math.Abs(b.MaxHealth - a.MaxHealth) > 0.001)
                    changes.Add(new("health", id, null, b, a, moved));
            }
        var removed = old.Values.Where(u => !now.ContainsKey(u.UnitId)).OrderBy(u => u.UnitId).ToList();
        var added = now.Values.Where(u => !old.ContainsKey(u.UnitId)).OrderBy(u => u.UnitId).ToList();
        foreach (var r in removed.ToArray())
        {
            var match = added.FirstOrDefault(a => a.ProtoId == r.ProtoId && a.Player == r.Player && Distance(a, r) <= tolerance);
            if (match is null) continue;
            changes.Add(new("reidentified", match.UnitId, r.UnitId, r, match, Distance(match, r)));
            removed.Remove(r); added.Remove(match);
        }
        changes.AddRange(removed.Select(r => new Change("removed", null, r.UnitId, r, null, null)));
        changes.AddRange(added.Select(a => new Change("added", a.UnitId, null, null, a, null)));
        return changes.OrderBy(c => c.Kind, StringComparer.Ordinal).ThenBy(c => c.UnitId ?? c.PreviousUnitId).ToArray();
    }

    internal sealed record FlatSpot(double X, double Z, double MinHeight, double MaxHeight, double Delta);

    /// <summary>Finds non-overlapping square windows whose node-height range is within maxDelta.</summary>
    /// <param name="heights">Node heights [ix - ix0][iz - iz0].</param>
    /// <param name="ix0">First X node index of the height block.</param>
    /// <param name="iz0">First Z node index of the height block.</param>
    /// <param name="scale">World units per node.</param>
    /// <param name="windowNodes">Square window side in nodes.</param>
    /// <param name="maxDelta">Maximum allowed max-min height inside a window.</param>
    /// <param name="maxResults">Maximum non-overlapping sites returned.</param>
    /// <returns>Window centers in world units, flattest and most central first.</returns>
    internal static FlatSpot[] FlatSpots(float[][] heights, int ix0, int iz0, double scale, int windowNodes, double maxDelta, int maxResults)
    {
        var nx = heights.Length; var nz = nx == 0 ? 0 : heights[0].Length;
        var candidates = new List<(int I, int J, double Min, double Max)>();
        for (var i = 0; i + windowNodes <= nx; i++)
            for (var j = 0; j + windowNodes <= nz; j++)
            {
                double lo = double.MaxValue, hi = double.MinValue;
                for (var a = i; a < i + windowNodes && hi - lo <= maxDelta + 1e-9; a++)
                    for (var b = j; b < j + windowNodes; b++) { lo = Math.Min(lo, heights[a][b]); hi = Math.Max(hi, heights[a][b]); }
                if (hi - lo <= maxDelta + 1e-9) candidates.Add((i, j, lo, hi));
            }
        double cx = (nx - 1) / 2.0, cz = (nz - 1) / 2.0;
        var chosen = new List<(int I, int J, double Min, double Max)>();
        foreach (var c in candidates.OrderBy(c => c.Max - c.Min)
            .ThenBy(c => Math.Pow(c.I + (windowNodes - 1) / 2.0 - cx, 2) + Math.Pow(c.J + (windowNodes - 1) / 2.0 - cz, 2))
            .ThenBy(c => c.I).ThenBy(c => c.J))
        {
            if (chosen.Any(o => Math.Abs(o.I - c.I) < windowNodes && Math.Abs(o.J - c.J) < windowNodes)) continue;
            chosen.Add(c);
            if (chosen.Count >= maxResults) break;
        }
        return chosen.Select(c => new FlatSpot((ix0 + c.I + (windowNodes - 1) / 2.0) * scale, (iz0 + c.J + (windowNodes - 1) / 2.0) * scale,
            c.Min, c.Max, c.Max - c.Min)).ToArray();
    }

    /// <summary>Gate points matching within tolerance at the exact frame pixel, or (radius &gt; 0, derived layouts) any pixel of the clamped neighborhood.</summary>
    internal static int GateMatches(ScreenProbe.Frame frame, JsonElement gate, int tolerance, int radius) =>
        gate.EnumerateArray().Count(p =>
        {
            int x = p.GetProperty("x").GetInt32(), y = p.GetProperty("y").GetInt32();
            int r = p.GetProperty("r").GetInt32(), g = p.GetProperty("g").GetInt32(), b = p.GetProperty("b").GetInt32();
            bool Near(ScreenProbe.Rgb c) => Math.Abs(c.R - r) <= tolerance && Math.Abs(c.G - g) <= tolerance && Math.Abs(c.B - b) <= tolerance;
            if (radius <= 0) return Near(frame.At(x, y));
            for (var dy = -radius; dy <= radius; dy++)
                for (var dx = -radius; dx <= radius; dx++)
                    if (frame.Contains(x + dx, y + dy) && Near(frame.At(x + dx, y + dy))) return true;
            return false;
        });

    /// <summary>Synthetic fixtures for minimap model, formations, diffs, overlaps and flat search.</summary>
    internal static void SelfTest()
    {
        void Check(bool ok, string what) { if (!ok) throw new InvalidOperationException("Scene geometry fixture failed: " + what); }
        // Measured alternative UI: center pixel 2318,1196 and half-diagonal 205 on a 256-unit map.
        var c = MinimapPixel(128, 128, 256, 256, 2318, 1196, 205);
        Check(Math.Abs(c.X - 2318) < 1e-9 && Math.Abs(c.Y - 1196) < 1e-9, "minimap center");
        var top = MinimapPixel(256, 256, 256, 256, 2318, 1196, 205);
        Check(Math.Abs(top.X - 2318) < 1e-9 && Math.Abs(top.Y - (1196 - 205)) < 1e-9, "minimap top vertex");
        var right = MinimapPixel(256, 0, 256, 256, 2318, 1196, 205);
        Check(Math.Abs(right.X - (2318 + 205)) < 1e-9 && Math.Abs(right.Y - 1196) < 1e-9, "minimap right vertex");
        var d = MinimapDelta(10, 0, 256, 256, 205);
        var moved = MinimapPixel(138, 128, 256, 256, 2318, 1196, 205);
        Check(Math.Abs(moved.X - (2318 + d.DX)) < 1e-9 && Math.Abs(moved.Y - (1196 + d.DY)) < 1e-9, "minimap delta");
        Check(ClampDiamond(2318 + 400, 1196, 2318, 1196, 205, 5) == (2518, 1196), "diamond clamp");
        var rows = Formation("rows", 4, 2, 10, 10, 2, 0);
        Check(rows.SequenceEqual([new WorldPoint(9, 9), new WorldPoint(11, 9), new WorldPoint(9, 11), new WorldPoint(11, 11)]), "world rows");
        var ring = Formation("ring", 2, 4, 0, 0, null, 0);
        Check(Math.Abs(ring[0].X - 2) < 1e-9 && Math.Abs(ring[1].X + 2) < 1e-9, "world ring");
        var turned = Formation("rows", 2, 2, 0, 0, 2, 90);
        Check(Math.Abs(turned[0].X) < 1e-9 && Math.Abs(turned[0].Z + 1) < 1e-9, "rotation");
        LiveUnits.Unit U(int id, int proto, float x, float hp = 10) => new(id, proto, "P" + proto, 1, new(x, 0, 0), hp, 10);
        var diff = Diff([U(1, 5, 0), U(2, 6, 5), U(3, 7, 9), U(4, 8, 1)], [U(1, 5, 3), U(262146, 6, 5), U(9, 9, 0), U(4, 8, 1, 4)], 0.05);
        Check(diff.Single(x => x.Kind == "moved").UnitId == 1, "diff moved");
        Check(diff.Single(x => x.Kind == "reidentified") is { UnitId: 262146, PreviousUnitId: 2 }, "diff reidentified");
        Check(diff.Single(x => x.Kind == "removed").PreviousUnitId == 3 && diff.Single(x => x.Kind == "added").UnitId == 9, "diff add/remove");
        Check(diff.Single(x => x.Kind == "health").UnitId == 4, "diff health");
        Check(Rect.Around(0, 0, 1, 1, 0).Overlaps(Rect.Around(1.5, 0, 1, 1, 0)) && !Rect.Around(0, 0, 1, 1, 0).Overlaps(Rect.Around(2, 0, 1, 1, 0)), "rect overlap");
        Check(!Rect.Around(1, 1, 2, 2, 0).Inside(10, 10) && Rect.Around(5, 5, 2, 2, 0).Inside(10, 10), "rect inside");
        var grid = Enumerable.Range(0, 6).Select(i => Enumerable.Range(0, 6).Select(j => i < 3 ? 0f : 5f).ToArray()).ToArray();
        var flats = FlatSpots(grid, 0, 0, 2, 3, 0.5, 4);
        Check(flats.Length == 2 && flats.All(f => f.Delta == 0), "flat search count");
        Check(flats.Any(f => f.X == 2 && f.MaxHeight == 0) && flats.Any(f => f.X == 8 && f.MinHeight == 5), "flat search spots");
    }
}
