using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

namespace AomMcp;

/// <summary>Camera zoom/pitch control with live pose readback, framed captures and PNG saving (plan C2).</summary>
/// <remarks>Live probe 2026-10-13 (build 100.19.17020.0, research/LIVE-WORLD.md "Camera zoom and pitch"):
/// native cameraMaxZoomSet(z) sets the camera's distance along the view ray to the world plane y=0 exactly and unclamped
/// (2..250 tested; 75 and 120 matched position.y/sin(pitch) within 0.01 while the terrain under the view was y=4),
/// cameraPitchAngle(a) sets pitch about the same pivot, cameraZoomReset/cameraPitchReset return to 75/47°.
/// Mouse wheel: positive steps zoom in by 4 units per notch, clamped to 29..85.</remarks>
internal sealed partial class Server
{
    /// <summary>Camera/capture helper names dispatched by <see cref="InvokeCamera"/>.</summary>
    static readonly HashSet<string> CameraNames = new(StringComparer.Ordinal)
    {
        "editor_camera",
        "editor_capture",
    };

    /// <summary>Operations accepted by editor_camera.</summary>
    static readonly string[] CameraOperations = ["get", "zoom", "pitch", "reset"];

    /// <summary>Host zoom bounds (camera-to-pivot distance, world units): the live-tested range 10..250.</summary>
    const double MinZoom = 10,
        MaxZoom = 250;

    /// <summary>Host pitch bounds in degrees: live-tested 30..89 (90 would make the view basis degenerate).</summary>
    const double MinPitch = 30,
        MaxPitch = 89;

    /// <summary>Accepted zoom readback error (world units) before one corrective dispatch.</summary>
    const double ZoomTolerance = 0.5;

    /// <summary>Accepted pitch readback error in degrees.</summary>
    const double PitchTolerance = 0.25;

    /// <summary>Observed native defaults after cameraZoomReset/cameraPitchReset (live probe; reported, not enforced).</summary>
    const double DefaultZoom = 75,
        DefaultPitch = 47;

    /// <summary>Observed wheel behaviour: units per notch and the clamp range (live probe; documentation only).</summary>
    const double WheelUnitsPerNotch = 4,
        WheelMinZoom = 29,
        WheelMaxZoom = 85;

    /// <summary>Default fraction of the viewport kept free around a framed area.</summary>
    const double DefaultMargin = 0.05,
        MaxMargin = 0.4;

    /// <summary>Fit iterations for editor_capture (each is one native zoom + settle).</summary>
    const int FitIterations = 6;

    /// <summary>A fit is accepted once the area's largest normalized extent lies in [FitLow, 1] of the usable viewport.</summary>
    const double FitLow = 0.85;

    /// <summary>Zoom-out factor applied when a framed corner is behind the camera.</summary>
    const double BehindFactor = 2;

    /// <summary>Minimap closed-loop centering tolerance and click budget (same defaults as editor_camera_look_at).</summary>
    const double CenterTolerance = 3;

    /// <summary>Maximum minimap clicks per centering.</summary>
    const int CenterClicks = 4;

    /// <summary>Native command names (generated/catalog.json; help text there).</summary>
    const string ZoomCommand = "cameraMaxZoomSet",
        PitchCommand = "cameraPitchAngle",
        ZoomResetCommand = "cameraZoomReset",
        PitchResetCommand = "cameraPitchReset";

    /// <summary>editor_screenshot sizing arguments that saveTo excludes (it always writes the full client).</summary>
    static readonly string[] ScreenshotSizing = ["maxWidth", "region", "scale", "resolutionScale"];

    /// <summary>PNG extension required for saved captures.</summary>
    const string PngExtension = ".png";

    /// <summary>Limitation text shared by the camera tools.</summary>
    const string CameraLimitation =
        "Camera only (no scene/file effect). Pose is the live renderer camera; zoom = native distance along the view direction to the world plane y=0 (what cameraMaxZoomSet sets); groundDistance = distance to the terrain under the view center. uiToggleUI had no visible effect in the editor (live probe), so UI panels stay in captures.";

    /// <summary>Validates camera/capture arguments before any game connection.</summary>
    /// <param name="name">Tool name.</param>
    /// <param name="args">Schema-validated arguments.</param>
    static void PreflightCamera(string name, JsonElement args)
    {
        if (name == "editor_camera")
        {
            var operation = String(args, "operation");
            if (operation == "zoom" && !args.TryGetProperty("distance", out _))
                throw new ArgumentException("operation=zoom requires distance.");
            if (operation == "pitch" && !args.TryGetProperty("degrees", out _))
                throw new ArgumentException("operation=pitch requires degrees.");
            return;
        }
        if (
            args.GetProperty("minX").GetDouble() >= args.GetProperty("maxX").GetDouble()
            || args.GetProperty("minZ").GetDouble() >= args.GetProperty("maxZ").GetDouble()
        )
            throw new ArgumentException("Area requires minX<maxX and minZ<maxZ.");
        if (args.TryGetProperty("saveTo", out var save))
        {
            _ = EditorFiles.ApprovedNewPath(save.GetString()!, PngExtension);
            if (
                args.TryGetProperty("maxWidth", out _)
                || args.TryGetProperty("resolutionScale", out _)
            )
                throw new ArgumentException(
                    "saveTo writes the full-resolution client; omit maxWidth/resolutionScale."
                );
        }
    }

    /// <summary>Runs editor_camera or editor_capture on one game connection.</summary>
    /// <param name="name">Tool name.</param>
    /// <param name="args">Preflighted arguments.</param>
    /// <param name="batchGame">Shared batch connection, if any.</param>
    object InvokeCamera(string name, JsonElement args, Game? batchGame)
    {
        if (_layout.Map is null)
            throw new InvalidDataException(
                "Live map/camera read layout unavailable for this build; camera helpers refuse. Offsets are never guessed."
            );
        using var owned = batchGame is null ? new Game(exe, _layout, pid) : null;
        var game = batchGame ?? owned!;
        return name == "editor_camera" ? Camera(game, args) : Capture(game, args);
    }

    /// <summary>Live camera pose summary.</summary>
    /// <param name="Zoom">Native zoom: distance along the view direction to the world plane y=0 (what cameraMaxZoomSet sets), or null when looking up.</param>
    /// <param name="GroundDistance">Distance to the terrain hit along the view direction, or null when the view misses the map.</param>
    /// <param name="Pitch">Downward pitch in degrees.</param>
    /// <param name="Yaw">Heading of the view direction in degrees (atan2 of X over Z).</param>
    /// <param name="Target">Terrain point under the view center, if any.</param>
    /// <param name="Position">Camera position.</param>
    sealed record CameraPose(
        double? Zoom,
        double? GroundDistance,
        double Pitch,
        double Yaw,
        Vector3? Target,
        Vector3 Position
    )
    {
        /// <summary>JSON-friendly shape.</summary>
        internal object Json() =>
            new
            {
                zoom = Zoom is { } z ? Math.Round(z, 2) : (double?)null,
                groundDistance = GroundDistance is { } g ? Math.Round(g, 2) : (double?)null,
                pitchDegrees = Math.Round(Pitch, 2),
                yawDegrees = Math.Round(Yaw, 2),
                target = Target is { } t ? P(t) : null,
                position = P(Position),
            };
    }

    /// <summary>Reads the camera pose from the live view.</summary>
    /// <param name="view">Live view state.</param>
    static CameraPose PoseOf(EditorView.ViewState view)
    {
        var forward = view.Pose.Forward;
        var target = view.Target();
        return new CameraPose(
            forward.Y < 0 ? view.Pose.Position.Y / -forward.Y : null,
            target is { } t ? Vector3.Distance(view.Pose.Position, t) : null,
            double.RadiansToDegrees(Math.Asin(-forward.Y)),
            double.RadiansToDegrees(Math.Atan2(forward.X, forward.Z)),
            target,
            view.Pose.Position
        );
    }

    /// <summary>Settles the camera and returns its pose.</summary>
    /// <param name="game">Validated editor connection.</param>
    static CameraPose SettledPose(Game game) => PoseOf(WaitCamera(game));

    /// <summary>Refuses before any camera input when a camera native is missing from the catalog (Catalog.Require).</summary>
    void RequireCameraNatives()
    {
        foreach (
            var command in new[] { ZoomCommand, PitchCommand, ZoomResetCommand, PitchResetCommand }
        )
            _ = _catalog.Require(command);
    }

    /// <summary>Dispatches one catalog camera command with named arguments.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="command">Catalog command name.</param>
    /// <param name="args">Argument object (property names as in the catalog).</param>
    void CameraNative(Game game, string command, object args) =>
        _bridge.Execute(
            game,
            Catalog.Build(_catalog.Require(command), JsonSerializer.SerializeToElement(args))
        );

    /// <summary>Sets native zoom; one corrective dispatch if the readback misses (not observed live; safeguard).</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="distance">Requested camera-to-ground distance.</param>
    /// <returns>Settled pose, dispatched values and whether the readback is within tolerance.</returns>
    (CameraPose Pose, double[] Dispatched, bool Verified) SetZoom(Game game, double distance)
    {
        var sent = new List<double> { distance };
        CameraNative(game, ZoomCommand, new { zoom = distance });
        var pose = SettledPose(game);
        if (pose.Zoom is { } z && Math.Abs(z - distance) > ZoomTolerance)
        {
            var corrected = Math.Clamp(distance + (distance - z), MinZoom, MaxZoom);
            sent.Add(corrected);
            CameraNative(game, ZoomCommand, new { zoom = corrected });
            pose = SettledPose(game);
        }
        return (
            pose,
            [.. sent],
            pose.Zoom is { } after && Math.Abs(after - distance) <= ZoomTolerance
        );
    }

    /// <summary>editor_camera: get/zoom/pitch/reset with readback.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="args">Preflighted arguments.</param>
    object Camera(Game game, JsonElement args)
    {
        var operation = String(args, "operation");
        var before = PoseOf(EditorView.ReadView(game));
        object wheel = new
        {
            zoomInSign = "positive editor_mouse_wheel steps zoom in",
            unitsPerNotch = WheelUnitsPerNotch,
            minZoom = WheelMinZoom,
            maxZoom = WheelMaxZoom,
            note = "Wheel zoom is clamped and animated; editor_camera zoom uses native cameraMaxZoomSet, which is not clamped by the wheel range.",
        };
        if (operation == "get")
            return new
            {
                camera = before.Json(),
                defaults = new { zoom = DefaultZoom, pitchDegrees = DefaultPitch },
                wheel,
                limitation = CameraLimitation,
            };
        RequireCameraNatives();
        switch (operation)
        {
            case "zoom":
            {
                var distance = args.GetProperty("distance").GetDouble();
                var (after, sent, verified) = SetZoom(game, distance);
                return new
                {
                    requested = new { distance },
                    before = before.Json(),
                    after = after.Json(),
                    dispatched = sent,
                    verified,
                    atLimit = distance is MinZoom or MaxZoom,
                    wheel,
                    limitation = CameraLimitation,
                };
            }
            case "pitch":
            {
                var degrees = args.GetProperty("degrees").GetDouble();
                CameraNative(game, PitchCommand, new { angle = degrees });
                var after = SettledPose(game);
                return new
                {
                    requested = new { degrees },
                    before = before.Json(),
                    after = after.Json(),
                    verified = Math.Abs(after.Pitch - degrees) <= PitchTolerance,
                    note = "Native cameraPitchAngle also resets camera rotation (yaw) to the default per its help text; compare yawDegrees.",
                    limitation = CameraLimitation,
                };
            }
            default:
            {
                CameraNative(game, ZoomResetCommand, new { });
                CameraNative(game, PitchResetCommand, new { });
                var after = SettledPose(game);
                return new
                {
                    before = before.Json(),
                    after = after.Json(),
                    expectedDefaults = new { zoom = DefaultZoom, pitchDegrees = DefaultPitch },
                    limitation = CameraLimitation,
                };
            }
        }
    }

    /// <summary>Writes PNG bytes to a new approved path and verifies the written hash.</summary>
    /// <param name="path">Requested absolute new .png path.</param>
    /// <param name="png">Encoded image.</param>
    /// <param name="width">Image width.</param>
    /// <param name="height">Image height.</param>
    static object SavePng(string path, byte[] png, int width, int height)
    {
        path = EditorFiles.ApprovedNewPath(path, PngExtension);
        using (
            var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)
        )
        {
            file.Write(png);
            file.Flush(true);
        }
        var sha = Convert.ToHexStringLower(SHA256.HashData(png));
        if (Layout.Hash(path) != sha)
            throw new IOException("saveTo copy verification failed.");
        return new
        {
            savedTo = path,
            sha256 = sha,
            width,
            height,
            bytes = png.Length,
        };
    }

    /// <summary>Full-resolution client capture saved to a new PNG (editor_screenshot saveTo).</summary>
    /// <param name="game">Validated game connection.</param>
    /// <param name="path">Requested absolute new .png path.</param>
    static object SaveScreenshot(Game game, string path)
    {
        var (clientWidth, _) = Ui.ClientSize(game);
        var (width, height, pixels) = Ui.CapturePixels(
            game,
            Math.Min(clientWidth, Ui.MaxCaptureWidth)
        );
        return SavePng(path, Ui.Png(width, height, pixels), width, height);
    }

    /// <summary>editor_capture: center on a world area, zoom so it fits the viewport, capture, then restore the camera.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="args">Preflighted arguments.</param>
    object Capture(Game game, JsonElement args)
    {
        double minX = args.GetProperty("minX").GetDouble(),
            minZ = args.GetProperty("minZ").GetDouble(),
            maxX = args.GetProperty("maxX").GetDouble(),
            maxZ = args.GetProperty("maxZ").GetDouble();
        var margin = Dbl(args, "margin", DefaultMargin);
        var restore = !args.TryGetProperty("restore", out var r) || r.GetBoolean();
        RequireCameraNatives();
        var start = PoseOf(EditorView.ReadView(game));
        var center = CameraLookAt(
            game,
            (minX + maxX) / 2,
            (minZ + maxZ) / 2,
            CenterTolerance,
            CenterClicks
        );
        var fits = false;
        var atLimit = false;
        var poses = new List<object>();
        double Extent(EditorView.ViewState v, out bool behind)
        {
            behind = false;
            double worst = 0;
            float halfW = v.Viewport.Width / 2f,
                halfH = v.Viewport.Height / 2f;
            foreach (var (x, z) in new[] { (minX, minZ), (minX, maxZ), (maxX, minZ), (maxX, maxZ) })
            {
                var height = v.InsideMap(x, z) ? v.Height(x, z).Height : 0;
                var p = v.Project(new Vector3((float)x, height, (float)z));
                if (!p.InFront)
                {
                    behind = true;
                    continue;
                }
                worst = Math.Max(
                    worst,
                    Math.Abs(p.X - v.Viewport.X - halfW) / (halfW * (1 - margin))
                );
                worst = Math.Max(
                    worst,
                    Math.Abs(p.Y - v.Viewport.Y - halfH) / (halfH * (1 - margin))
                );
            }
            return worst;
        }
        var view = EditorView.ReadView(game);
        for (var i = 0; i < FitIterations; i++)
        {
            var pose = PoseOf(view);
            var extent = Extent(view, out var behind);
            poses.Add(
                new
                {
                    camera = pose.Json(),
                    extent = Math.Round(extent, 3),
                    behind,
                }
            );
            if (!behind && extent is >= FitLow and <= 1)
            {
                fits = true;
                break;
            }
            var current = pose.Zoom ?? DefaultZoom;
            // Perspective extent scales roughly inversely with distance around a centered target.
            var next = Math.Clamp(
                behind ? current * BehindFactor : current * extent / ((FitLow + 1) / 2),
                MinZoom,
                MaxZoom
            );
            if (Math.Abs(next - current) <= ZoomTolerance)
            {
                fits = !behind && extent <= 1;
                atLimit = next is MinZoom or MaxZoom;
                break;
            }
            SetZoom(game, next);
            view = EditorView.ReadView(game);
        }
        var framed = PoseOf(view);
        object? saved = null;
        byte[]? png = null;
        if (args.TryGetProperty("saveTo", out var save))
            saved = SaveScreenshot(game, save.GetString()!);
        else
        {
            var capture = Ui.CapturePixels(
                game,
                Int(args, "maxWidth", Ui.DefaultScreenshotWidth),
                args.TryGetProperty("resolutionScale", out var rs) ? rs.GetDouble() : 1
            );
            png = Ui.Png(capture.Width, capture.Height, capture.Bgra);
        }
        object? restored = null;
        if (restore)
        {
            if (start.Zoom is { } zoom)
                SetZoom(game, Math.Clamp(zoom, MinZoom, MaxZoom));
            if (start.Target is { } t)
                CameraLookAt(game, t.X, t.Z, CenterTolerance, CenterClicks);
            restored = PoseOf(EditorView.ReadView(game)).Json();
        }
        var result = new
        {
            area = new
            {
                minX,
                minZ,
                maxX,
                maxZ,
            },
            margin,
            fits,
            atLimit,
            centering = center,
            fitSteps = poses,
            framed = framed.Json(),
            before = start.Json(),
            restored,
            saved,
            limitation = CameraLimitation
                + " fits = the four area corners (terrain height) project inside the render viewport minus margin; UI panels may still cover parts. Large areas stop at the host zoom limit (atLimit); tile them with several calls.",
        };
        return png is null ? result : new ImageResult(png, result);
    }
}
