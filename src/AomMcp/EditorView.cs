using System.Numerics;
using System.Text.Json;

namespace AomMcp;

/// <summary>Reviewed native editor selection container fields, never inferred from cursor state.</summary>
/// <param name="Player">Editor branch's current-selection player, not placement player.</param>
/// <param name="SlotsOffset">Game object's per-player selection-slot base.</param>
/// <param name="SlotStride">Bytes between player slots.</param>
/// <param name="GroupOffset">Selection holder's group pointer member.</param>
/// <param name="CountOffset">Group count member.</param>
/// <param name="ArrayOffset">Group record-array member.</param>
/// <param name="RecordStride">Bytes between typed selection records.</param>
/// <param name="KindOffset">Record type member; native zero type denotes simulation unit.</param>
/// <param name="IdOffset">Full ID member for unit records.</param>
/// <param name="Signatures">Reviewed selection/owner code supporting the fields.</param>
public sealed record SelectionReadLayout(int Player, int SlotsOffset, int SlotStride, int GroupOffset,
    int CountOffset, int ArrayOffset, int RecordStride, int KindOffset, int IdOffset, UnitReadSignature[] Signatures);

/// <summary>Reviewed terrain, active camera and render-state fields for passive map queries.</summary>
/// <param name="WorldOffset">Command context's world-mirror pointer member.</param>
/// <param name="TerrainOffset">World mirror's terrain pointer member.</param>
/// <param name="TileOffsets">Terrain X/Z tile counts.</param>
/// <param name="VertexOffsets">Terrain X/Z vertex counts.</param>
/// <param name="ScaleOffset">Terrain world-units-per-tile member.</param>
/// <param name="InverseScaleOffset">Terrain tiles-per-world-unit member.</param>
/// <param name="GridOffset">Terrain height-grid inline structure.</param>
/// <param name="GridCountOffset">Height-grid scalar element count.</param>
/// <param name="GridDataOffset">Height-grid float array pointer.</param>
/// <param name="GridStrideOffset">Height-grid row stride (Z-fast).</param>
/// <param name="CameraOffset">Game object's active camera pointer, not default camera.</param>
/// <param name="PoseOffsets">Camera position/forward/up/right vector members.</param>
/// <param name="FovOffset">Active camera field-of-view radians member.</param>
/// <param name="RendererGlobalRva">Render-state global found through native fov setter.</param>
/// <param name="ViewportOffsets">Render-state client viewport X/Y/width/height.</param>
/// <param name="ProjectionOffset">Render-state untransposed Direct3D projection matrix.</param>
/// <param name="Signatures">Reviewed map/camera/height/projection code fragments.</param>
public sealed record MapReadLayout(int WorldOffset, int TerrainOffset, int[] TileOffsets, int[] VertexOffsets,
    int ScaleOffset, int InverseScaleOffset, int GridOffset, int GridCountOffset, int GridDataOffset,
    int GridStrideOffset, int CameraOffset, int[] PoseOffsets, int FovOffset, uint RendererGlobalRva,
    int[] ViewportOffsets, int ProjectionOffset, UnitReadSignature[] Signatures);

/// <summary>Actual selection/map state; native commands are never executed to obtain getter values.</summary>
internal static class EditorView
{
    // Host read/geometry ceilings; not game capacity promises. AMD64 pointer/binary32 widths are ABI.
    const int MaxSelected = 4096, MaxGridSide = 8192, MaxOffset = 65536, FloatSize = 4;
    // Numerical policy for degenerate rays, matrices and approximately normalized camera bases.
    const float Epsilon = 0.00001f, BasisTolerance = 0.01f;
    internal sealed record Selection(int Kind, int Id);
    internal sealed record Pose(Vector3 Position, Vector3 Forward, Vector3 Up, Vector3 Right);
    internal sealed record Viewport(int X, int Y, int Width, int Height);

    /// <summary>Reads selected typed records and joins full live object IDs to actual properties.</summary>
    /// <param name="game">Validated read-only process connection.</param>
    /// <param name="args">Optional bounded offset/limit.</param>
    /// <returns>Actual selected object properties, non-unit record types and unresolved references.</returns>
    public static object InspectSelection(Game game, JsonElement args)
    {
        var layout = game.Layout.Selection ?? throw new InvalidDataException("Selection layout unavailable; passive patch review required.");
        var records = ReadSelection(game);
        var count = records.Length;
        var units = LiveUnits.Read(game).ToDictionary(u => u.UnitId);
        var offset = args.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : LiveUnits.DefaultLimit;
        if (!ReadSelection(game).SequenceEqual(records))
            throw new InvalidDataException("Selection changed during inspection; no listing returned.");
        return new
        {
            pid = game.Pid, buildHash = game.Layout.ExeSha256, selectionPlayer = layout.Player,
            total = count, offset, limit,
            nextOffset = offset < count && Math.Min(limit, count - offset) < count - offset ? (int?)(offset + limit) : null,
            entries = records.Skip(offset).Take(limit).Select(r => new
            {
                kind = r.Kind, unitId = r.Kind == 0 ? (int?)r.Id : null,
                properties = r.Kind == 0 && units.TryGetValue(r.Id, out var u) ? u : null,
                resolved = r.Kind == 0 && units.ContainsKey(r.Id),
            }).ToArray(),
            atomic = false,
            limitation = "Actual selection records and simulation properties, not command acknowledgements. Non-unit selections retain kind but are not interpreted as unit IDs. Unresolved IDs may indicate changing/deleted objects. Position/health can change between reads; no atomic frame claim.",
        };
    }

    /// <summary>Reads typed selection records with identity recheck; read-only process access.</summary>
    internal static Selection[] ReadSelection(Game game)
    {
        var layout = game.Layout.Selection ?? throw new InvalidDataException("Selection layout unavailable; passive patch review required.");
        CheckOffsets([layout.SlotsOffset, layout.SlotStride, layout.GroupOffset, layout.CountOffset,
            layout.ArrayOffset, layout.RecordStride, layout.KindOffset, layout.IdOffset]);
        if (layout.Player is < 0 or > LiveUnits.MaxPlayer || layout.RecordStride < Math.Max(layout.KindOffset, layout.IdOffset) + FloatSize)
            throw new InvalidDataException("Invalid selection layout.");
        var editor = LiveUnits.ValidateRuntime(game, layout.Signatures);
        var root = game.Pointer(game.Base + checked((int)game.Layout.EditorGlobalRva));
        var holder = game.Pointer(root + layout.SlotsOffset + layout.Player * layout.SlotStride);
        var group = holder == 0 ? 0 : game.Pointer(holder + layout.GroupOffset);
        var count = group == 0 ? 0 : checked((int)game.UInt(group + layout.CountOffset));
        if (count > MaxSelected)
            throw new InvalidDataException("Selection exceeds host read bound.");
        var table = group == 0 ? 0 : game.Pointer(group + layout.ArrayOffset);
        if (count > 0 && table == 0) throw new InvalidDataException("Selection records unavailable.");
        var bytes = count == 0 ? [] : game.Read(table, checked(count * layout.RecordStride));
        var records = ParseSelection(bytes, layout);
        if (game.Editor() != editor || game.Pointer(root + layout.SlotsOffset + layout.Player * layout.SlotStride) != holder
            || (holder != 0 && game.Pointer(holder + layout.GroupOffset) != group)
            || (group != 0 && (game.UInt(group + layout.CountOffset) != count || game.Pointer(group + layout.ArrayOffset) != table))
            || (count > 0 && !game.Read(table, bytes.Length).AsSpan().SequenceEqual(bytes)))
            throw new InvalidDataException("Selection changed during inspection; no listing returned.");
        return records;
    }

    static Selection[] ParseSelection(byte[] bytes, SelectionReadLayout layout)
    {
        if (bytes.Length % layout.RecordStride != 0) throw new InvalidDataException("Torn selection array.");
        return Enumerable.Range(0, bytes.Length / layout.RecordStride).Select(i => new Selection(
            BitConverter.ToInt32(bytes, i * layout.RecordStride + layout.KindOffset),
            BitConverter.ToInt32(bytes, i * layout.RecordStride + layout.IdOffset))).ToArray();
    }

    static void CheckOffsets(int[] offsets)
    {
        if (offsets.Any(o => o is < 0 or > MaxOffset)) throw new InvalidDataException("Unbounded live map/selection field offset.");
    }

    /// <summary>Validated passive map/camera/projection state with bounded terrain/projection helpers.</summary>
    internal sealed class ViewState
    {
        internal required Game Game { get; init; }
        internal required MapReadLayout L { get; init; }
        internal required nint EditorPtr { get; init; }
        internal required nint Context { get; init; }
        internal required nint World { get; init; }
        internal required nint Terrain { get; init; }
        internal required nint Root { get; init; }
        internal required nint Camera { get; init; }
        internal required nint Renderer { get; init; }
        internal required int[] Tiles { get; init; }
        internal required int[] Vertices { get; init; }
        internal required int[] ViewValues { get; init; }
        internal required float Scale { get; init; }
        internal required float InverseScale { get; init; }
        internal required float Fov { get; init; }
        internal required byte[][] VectorBytes { get; init; }
        internal required byte[] MatrixBytes { get; init; }
        internal required float[] M { get; init; }
        internal required Pose Pose { get; init; }
        internal required Matrix4x4 Projection { get; init; }
        internal required Viewport Viewport { get; init; }
        internal float WorldWidth => Tiles[0] * Scale;
        internal float WorldDepth => Tiles[1] * Scale;
        (int Count, int Stride, nint Data)? _grid;

        int Int(nint a) => BitConverter.ToInt32(Game.Read(a, FloatSize));
        float Float(nint a) => BitConverter.ToSingle(Game.Read(a, FloatSize));

        (int Count, int Stride, nint Data) Grid()
        {
            if (_grid is { } cached) return cached;
            var grid = Terrain + L.GridOffset;
            var count = Int(grid + L.GridCountOffset); var stride = Int(grid + L.GridStrideOffset);
            var data = Game.Pointer(grid + L.GridDataOffset);
            if (stride != Vertices[1] || count != (long)Vertices[0] * Vertices[1] || data == 0)
                throw new InvalidDataException("Terrain height-grid bounds mismatch.");
            _grid = (count, stride, data);
            return _grid.Value;
        }

        internal bool InsideMap(double x, double z) => x >= 0 && z >= 0 && x <= WorldWidth && z <= WorldDepth;

        /// <summary>Native trGetTerrainHeight-style quantized node lookup (truncating tile coordinates).</summary>
        internal (float Height, int TileX, int TileZ) Height(double x, double z)
        {
            if (!InsideMap(x, z)) throw new ArgumentException("Terrain sample outside map.");
            var ix = (int)(x * InverseScale); var iz = (int)(z * InverseScale); // Native getter truncates nonnegative tile coordinates.
            var (_, stride, data) = Grid();
            if (ix >= Vertices[0] || iz >= Vertices[1]) throw new InvalidDataException("Terrain height-grid bounds mismatch.");
            var height = Float(data + checked((ix * stride + iz) * FloatSize));
            if (!float.IsFinite(height)) throw new InvalidDataException("Invalid terrain height.");
            return (height, ix, iz);
        }

        /// <summary>Reads contiguous Z-fast node heights for one X node row.</summary>
        internal float[] HeightRow(int ix, int iz0, int count)
        {
            var (_, stride, data) = Grid();
            if (ix < 0 || iz0 < 0 || count < 1 || ix >= Vertices[0] || iz0 + count > Vertices[1])
                throw new ArgumentException("Terrain node row outside height grid.");
            var bytes = Game.Read(data + checked((ix * stride + iz0) * FloatSize), count * FloatSize);
            var values = Enumerable.Range(0, count).Select(i => BitConverter.ToSingle(bytes, i * FloatSize)).ToArray();
            if (values.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Invalid terrain height.");
            return values;
        }

        internal Projected Project(Vector3 world) => ProjectPoint(world, Pose, Projection, Viewport);
        internal Vector3 RayAt(float x, float y) => Ray(x, y, Pose, Projection, Viewport);

        /// <summary>Ray hit on quantized terrain node heights by fixed-point refinement; not collision/occlusion.</summary>
        internal Vector3? GroundHit(Vector3 direction)
        {
            if (direction.Y > -Epsilon) return null;
            float plane = 0;
            Vector3 hit = default;
            for (var i = 0; i < 4; i++) // Host refinement passes; converges on gentle quantized terrain.
            {
                var distance = (plane - Pose.Position.Y) / direction.Y;
                if (distance < 0) return null;
                hit = Pose.Position + direction * distance;
                plane = Height(Math.Clamp(hit.X, 0, WorldWidth), Math.Clamp(hit.Z, 0, WorldDepth)).Height;
            }
            return hit;
        }

        /// <summary>Camera forward-ray ground hit: world point at viewport center.</summary>
        internal Vector3? Target() => GroundHit(Pose.Forward);

        /// <summary>Refuses if map/camera/projection changed since the snapshot.</summary>
        internal void Verify()
        {
            var g = Game;
            if (g.Editor() != EditorPtr || g.Pointer(g.Base + checked((int)g.Layout.ContextRva)) != Context
                || g.Pointer(Context + L.WorldOffset) != World || g.Pointer(Root + L.CameraOffset) != Camera
                || g.Pointer(g.Base + checked((int)L.RendererGlobalRva)) != Renderer
                || g.Pointer(World + L.TerrainOffset) != Terrain
                || Float(Camera + L.FovOffset) != Fov
                || L.ViewportOffsets.Where((offset, i) => Int(Renderer + offset) != ViewValues[i]).Any()
                || L.TileOffsets.Where((offset, i) => Int(Terrain + offset) != Tiles[i]).Any()
                || L.VertexOffsets.Where((offset, i) => Int(Terrain + offset) != Vertices[i]).Any()
                || !g.Read(Renderer + L.ProjectionOffset, MatrixBytes.Length).AsSpan().SequenceEqual(MatrixBytes)
                || L.PoseOffsets.Where((offset, i) => !g.Read(Camera + offset, VectorBytes[i].Length).AsSpan().SequenceEqual(VectorBytes[i])).Any())
                throw new InvalidDataException("Map/camera changed during query; no result returned.");
        }
    }

    internal sealed record Projected(float X, float Y, float Depth, bool InFront, bool VisibleInViewport);

    /// <summary>Reads and validates map dimensions, active camera, projection and viewport.</summary>
    internal static ViewState ReadView(Game game)
    {
        var l = game.Layout.Map ?? throw new InvalidDataException("Map layout unavailable; passive patch review required.");
        if (l.TileOffsets.Length != 2 || l.VertexOffsets.Length != 2 || l.PoseOffsets.Length != 4 || l.ViewportOffsets.Length != 4)
            throw new InvalidDataException("Incomplete map vector layout.");
        CheckOffsets([l.WorldOffset, l.TerrainOffset, l.ScaleOffset, l.InverseScaleOffset, l.GridOffset,
            l.GridCountOffset, l.GridDataOffset, l.GridStrideOffset, l.CameraOffset, l.FovOffset, l.ProjectionOffset,
            .. l.TileOffsets, .. l.VertexOffsets, .. l.PoseOffsets, .. l.ViewportOffsets]);
        var editor = LiveUnits.ValidateRuntime(game, l.Signatures);
        int Int(nint a) => BitConverter.ToInt32(game.Read(a, FloatSize));
        float Float(nint a) => BitConverter.ToSingle(game.Read(a, FloatSize));
        var context = game.Pointer(game.Base + checked((int)game.Layout.ContextRva));
        var world = game.Pointer(context + l.WorldOffset);
        var terrain = game.Pointer(world + l.TerrainOffset);
        var root = game.Pointer(game.Base + checked((int)game.Layout.EditorGlobalRva));
        var camera = game.Pointer(root + l.CameraOffset);
        var renderer = game.Pointer(game.Base + checked((int)l.RendererGlobalRva));
        if (terrain == 0 || camera == 0 || renderer == 0) throw new InvalidDataException("Live terrain/camera/renderer unavailable.");
        var tiles = l.TileOffsets.Select(o => Int(terrain + o)).ToArray();
        var vertices = l.VertexOffsets.Select(o => Int(terrain + o)).ToArray();
        var scale = Float(terrain + l.ScaleOffset);
        var inverseScale = Float(terrain + l.InverseScaleOffset);
        if (tiles.Any(v => v is < 1 or > MaxGridSide) || vertices.Any(v => v is < 1 or > MaxGridSide)
            || !float.IsFinite(scale) || scale <= 0 || !float.IsFinite(inverseScale) || inverseScale <= 0
            || Math.Abs(scale * inverseScale - 1) > BasisTolerance)
            throw new InvalidDataException("Invalid live map dimensions/scale.");
        var vectorBytes = l.PoseOffsets.Select(o => game.Read(camera + o, 3 * FloatSize)).ToArray(); // Three XYZ binary32 values per vector.
        var vectors = vectorBytes.Select(b => new Vector3(BitConverter.ToSingle(b, 0), BitConverter.ToSingle(b, FloatSize), BitConverter.ToSingle(b, 2 * FloatSize))).ToArray();
        var pose = new Pose(vectors[0], vectors[1], vectors[2], vectors[3]); // Layout order: position, forward, up, right.
        if (vectors.Any(v => !float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z))
            || vectors.Skip(1).Any(v => Math.Abs(v.LengthSquared() - 1) > BasisTolerance)
            || Vector3.Distance(Vector3.Cross(pose.Right, pose.Up), pose.Forward) > BasisTolerance)
            throw new InvalidDataException("Camera basis is not a supported finite orthonormal perspective pose.");
        var fov = Float(camera + l.FovOffset);
        if (!float.IsFinite(fov) || fov <= 0 || fov >= MathF.PI) // Perspective field of view must lie within (0, pi) radians.
            throw new InvalidDataException("Invalid live camera field of view.");
        var viewValues = l.ViewportOffsets.Select(o => Int(renderer + o)).ToArray();
        var viewport = new Viewport(viewValues[0], viewValues[1], viewValues[2], viewValues[3]);
        if (viewport.Width <= 0 || viewport.Height <= 0 || viewport.Width > MaxOffset || viewport.Height > MaxOffset)
            throw new InvalidDataException("Invalid render viewport.");
        var matrixBytes = game.Read(renderer + l.ProjectionOffset, 16 * FloatSize); // Four-by-four Direct3D matrix, row-major native store.
        var m = Enumerable.Range(0, 16).Select(i => BitConverter.ToSingle(matrixBytes, i * FloatSize)).ToArray();
        if (m.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Invalid render projection.");
        var projection = new Matrix4x4(m[0],m[1],m[2],m[3],m[4],m[5],m[6],m[7],m[8],m[9],m[10],m[11],m[12],m[13],m[14],m[15]);
        return new ViewState
        {
            Game = game, L = l, EditorPtr = editor, Context = context, World = world, Terrain = terrain, Root = root,
            Camera = camera, Renderer = renderer, Tiles = tiles, Vertices = vertices, ViewValues = viewValues,
            Scale = scale, InverseScale = inverseScale, Fov = fov, VectorBytes = vectorBytes, MatrixBytes = matrixBytes,
            M = m, Pose = pose, Projection = projection, Viewport = viewport,
        };
    }

    /// <summary>Reads dimensions, camera, projection and optional terrain/coordinate samples.</summary>
    /// <param name="game">Validated read-only process connection.</param>
    /// <param name="args">Optional terrainAt [X,Z], world [X,Y,Z], screen [clientX,clientY], planeY.</param>
    /// <returns>Measured map/view data, projected coordinates, inverse ray and explicit plane intersection.</returns>
    public static object MapInfo(Game game, JsonElement args)
    {
        var v = ReadView(game);
        object? terrainSample = null, projected = null, screenRay = null, planeIntersection = null;
        if (args.TryGetProperty("terrainAt", out var at))
        {
            var x = at[0].GetDouble(); var z = at[1].GetDouble();
            var (height, ix, iz) = v.Height(x, z);
            terrainSample = new { x, z, height, tileX = ix, tileZ = iz, sampling = "Native trGetTerrainHeight quantized node lookup, not bilinear or collision surface." };
        }
        if (args.TryGetProperty("world", out var point))
            projected = Project(new Vector3(point[0].GetSingle(),point[1].GetSingle(),point[2].GetSingle()), v.Pose, v.Projection, v.Viewport);
        if (args.TryGetProperty("screen", out var screen))
        {
            var direction = Ray(screen[0].GetSingle(), screen[1].GetSingle(), v.Pose, v.Projection, v.Viewport);
            screenRay = new { origin = XYZ(v.Pose.Position), direction = XYZ(direction) };
            if (args.TryGetProperty("planeY", out var y))
                planeIntersection = IntersectPlane(v.Pose.Position, direction, y.GetSingle());
        }
        v.Verify();
        var pose = v.Pose;
        return new
        {
            pid = game.Pid, buildHash = game.Layout.ExeSha256, atomic = false,
            dimensions = new { tilesX = v.Tiles[0], tilesZ = v.Tiles[1], worldWidth = v.WorldWidth, worldDepth = v.WorldDepth, tileWorldSize = v.Scale },
            camera = new { position = XYZ(pose.Position), forward = XYZ(pose.Forward), up = XYZ(pose.Up), right = XYZ(pose.Right), fieldOfViewRadians = v.Fov },
            viewport = v.Viewport, projection = v.M, terrainSample, projected, screenRay, planeIntersection,
            limitation = "Measured live active-camera basis, renderer projection and full-resolution client viewport. Projection visibility is frustum-only, not occlusion/UI clickability. Screen inverse returns a ray; world point requires caller's explicit planeY, never guessed terrain intersection. Terrain height matches quantized native getter, not rendered triangle collision. Editor only; changing/unreviewed layouts refuse.",
        };
    }

    internal static Projected ProjectPoint(Vector3 world, Pose pose, Matrix4x4 matrix, Viewport viewport)
    {
        var delta = world - pose.Position;
        var clip = Vector4.Transform(new Vector4(Vector3.Dot(delta,pose.Right),Vector3.Dot(delta,pose.Up),Vector3.Dot(delta,pose.Forward),1),matrix); // Homogeneous point W=1.
        if (Math.Abs(clip.W) < Epsilon) throw new InvalidDataException("World point on camera projection singularity.");
        var ndc = new Vector3(clip.X,clip.Y,clip.Z) / clip.W;
        // Direct3D normalized clip X/Y [-1,1], Z [0,1]; screen Y increases downward.
        return new(viewport.X + (ndc.X + 1) * viewport.Width / 2, viewport.Y + (1 - ndc.Y) * viewport.Height / 2, ndc.Z,
            clip.W > 0, clip.W > 0 && Math.Abs(ndc.X) <= 1 && Math.Abs(ndc.Y) <= 1 && ndc.Z is >= 0 and <= 1);
    }

    internal static object XYZ(Vector3 v) => new { x = v.X, y = v.Y, z = v.Z };
    static object Project(Vector3 world, Pose pose, Matrix4x4 matrix, Viewport viewport)
    {
        var p = ProjectPoint(world, pose, matrix, viewport);
        return new { x = p.X, y = p.Y, depth = p.Depth, inFront = p.InFront, visibleInViewport = p.VisibleInViewport };
    }

    internal static Vector3 Ray(float x, float y, Pose pose, Matrix4x4 projection, Viewport viewport)
    {
        if (!Matrix4x4.Invert(projection, out var inverse)) throw new InvalidDataException("Noninvertible projection.");
        // Direct3D far clip Z=1; normalized X/Y map full-resolution client rectangle.
        var clip = new Vector4((x - viewport.X) * 2 / viewport.Width - 1, 1 - (y - viewport.Y) * 2 / viewport.Height, 1, 1);
        var p = Vector4.Transform(clip, inverse);
        if (Math.Abs(p.W) < Epsilon) throw new InvalidDataException("Unsupported infinite-far projection.");
        var camera = new Vector3(p.X,p.Y,p.Z) / p.W;
        return Vector3.Normalize(pose.Right * camera.X + pose.Up * camera.Y + pose.Forward * camera.Z);
    }

    internal static object IntersectPlane(Vector3 origin, Vector3 direction, float planeY)
    {
        if (Math.Abs(direction.Y) < Epsilon) throw new ArgumentException("Screen ray parallel to requested plane.");
        var distance = (planeY - origin.Y) / direction.Y;
        if (distance < 0) throw new ArgumentException("Requested plane lies behind screen ray.");
        return XYZ(origin + direction * distance);
    }

    /// <summary>Checks typed selection parsing and exact project/unproject/plane geometry without game calls.</summary>
    public static void SelfTest()
    {
        // Synthetic selection record: kind+0, ID+4, 8 bytes; kind0 unit ID4, kind2 non-unit ID99.
        var l = new SelectionReadLayout(1,0,8,0,0,8,8,0,4,[]);
        var bytes = new byte[16]; BitConverter.GetBytes(4).CopyTo(bytes,4);
        BitConverter.GetBytes(2).CopyTo(bytes,8); BitConverter.GetBytes(99).CopyTo(bytes,12);
        if (!ParseSelection(bytes,l).SequenceEqual([new Selection(0,4),new Selection(2,99)])) throw new InvalidOperationException("Selection fixture failed.");
        // Mathematical fixture: camera at origin, forward +Z/up +Y/right +X, square 100px viewport,
        // 90-degree vertical FOV, near1/far100. Center ray reaches planeY1 after screen ray angled up.
        var pose = new Pose(Vector3.Zero,Vector3.UnitZ,Vector3.UnitY,Vector3.UnitX);
        var matrix = new Matrix4x4(1,0,0,0,0,1,0,0,0,0,100f/99,1,0,0,-100f/99,0);
        var viewport = new Viewport(0,0,100,100);
        var ray = Ray(50,25,pose,matrix,viewport);
        if (Vector3.Distance(ray,Vector3.Normalize(new Vector3(0,1,2))) > Epsilon) throw new InvalidOperationException("Projection inverse fixture failed.");
        var point = JsonSerializer.SerializeToElement(IntersectPlane(Vector3.Zero,ray,1));
        if (Math.Abs(point.GetProperty("z").GetSingle() - 2) > Epsilon) throw new InvalidOperationException("Plane fixture failed.");
        var pixel = JsonSerializer.SerializeToElement(Project(new Vector3(0,1,2),pose,matrix,viewport));
        if (Math.Abs(pixel.GetProperty("x").GetSingle() - 50) > Epsilon || Math.Abs(pixel.GetProperty("y").GetSingle() - 25) > Epsilon)
            throw new InvalidOperationException("Projection fixture failed.");
    }
}
