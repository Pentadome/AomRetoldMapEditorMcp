using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AomMcp;

/// <summary>editor_dialog_calibrate (full toolset): records open-dialog pixel signatures for the current UI kind and
/// client size into a NEW uilayouts/dialogs-&lt;ui&gt;-en-&lt;W&gt;x&lt;H&gt;.json, the same procedure as the 2560x1440 files
/// (research/ALTERNATIVE-UI.md, DialogState notes): every gadget opened with gadgetReal and closed with gadgetUnreal at
/// four camera poses; points are opaque (identical across poses), differ from the closed frame, avoid catalog controls
/// (state-dependent colours) and are unique against the other calibrated dialogs. Never overwrites a file.</summary>
internal static class DialogCalibration
{
    /// <summary>Public tool name.</summary>
    internal const string Tool = "editor_dialog_calibrate";

    /// <summary>Signature schema written (DialogState.Parse accepts schema 1 only).</summary>
    const int Schema = 1;

    /// <summary>Per-channel colour tolerance written to the file (same as the 2560x1440 files).</summary>
    const int Tolerance = 10;

    /// <summary>Open threshold written to the file (same as the 2560x1440 files).</summary>
    const double OpenFraction = 0.8;

    /// <summary>Partial threshold written to the file (same as the 2560x1440 files).</summary>
    const double PartialFraction = 0.4;

    /// <summary>Most points per signature (the 2560x1440 files use 24).</summary>
    const int MaxPoints = 24;

    /// <summary>Fewest points per signature (DialogState.Parse rejects fewer than 4).</summary>
    const int MinPoints = 4;

    /// <summary>Largest per-channel difference between poses for an opaque dialog pixel (host choice: well inside
    /// <see cref="Tolerance"/> so the live score survives screenshot noise).</summary>
    const int StableTolerance = 4;

    /// <summary>Smallest per-channel difference from the closed frame (host choice: twice <see cref="Tolerance"/>, so a
    /// closed frame never matches a point).</summary>
    const int ClosedMargin = 2 * Tolerance;

    /// <summary>Fewest changed frame pixels that count as "the dialog appeared" (host choice: a 1280x720 frame pixel is
    /// 4 client pixels at 2560; the smallest calibrated 2560 dialogs change thousands).</summary>
    const int MinOpenPixels = 200;

    /// <summary>Fraction of the open-only pixels still showing open colours after gadgetUnreal that means "still open"
    /// (host choice).</summary>
    const double StillOpenFraction = 0.5;

    /// <summary>Wait after gadgetReal/gadgetUnreal before the capture (host choice; DialogState polls at 200 ms and
    /// gadget show/hide lands within one or two frames, research/LIVE-MUTATIONS.md).</summary>
    const int SettleMs = 700;

    /// <summary>Frame pixels the ambient-motion mask grows by before it is removed from the open-only pixels (host
    /// choice: session 4 alternative-UI live run, drifting cloud shadows on a blank map passed as open dialogs).</summary>
    const int NoiseGrow = 2;

    /// <summary>Brightest channel value of a dark point that is never sampled (host choice from session 4 alternative-UI
    /// live: 12 black AnimationEditorGadget points scored a clear screen "partial" over the off-map void, and a modal
    /// dim of 13..24 per channel matched 28-grey RelicEditorGadget chrome within <see cref="Tolerance"/>; 24 + 10 = 34,
    /// plus margin).</summary>
    const int DarkMax = 40;

    /// <summary>Camera pose fractions of the map size: centre, both corners (off-map void behind dialogs, as for the
    /// normal 2560x1440 calibration) and a fourth quarter point so the background behind a dialog varies on maps with
    /// uniform edges (session 4 live: three water-backed poses let a semi-transparent border pass as opaque).</summary>
    static readonly (double X, double Z)[] Poses =
    [
        (0.5, 0.5),
        (0.05, 0.05),
        (0.95, 0.95),
        (0.25, 0.75),
    ];

    /// <summary>Smallest background change (per channel, any pose pair, closed frames) that proves a stable open pixel
    /// opaque (host choice: <see cref="ClosedMargin"/>).</summary>
    const int BackgroundMargin = ClosedMargin;

    /// <summary>Client pixels per frame pixel at the 2560 reference (signature bounds use 2560-reference pixels).</summary>
    const int ReferencePerFrame = 2;

    /// <summary>Signature file directory below the host base directory.</summary>
    const string LayoutDirectory = "uilayouts";

    /// <summary>File prefix of dialog signature files.</summary>
    const string FilePrefix = "dialogs-";

    /// <summary>Language tag in the file name (English UI; the OCR label checks also assume it).</summary>
    const string Language = "en";

    /// <summary>uiKind of the alternative editor UI.</summary>
    const string Alternative = "alternative";

    /// <summary>File name tag of the alternative UI.</summary>
    const string AlternativeTag = "alt";

    /// <summary>Signature file name for one UI kind and client size.</summary>
    /// <param name="uiKind">normal or alternative.</param>
    /// <param name="width">Client width.</param>
    /// <param name="height">Client height.</param>
    internal static string FileName(string uiKind, int width, int height) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{FilePrefix}{(uiKind == Alternative ? AlternativeTag : uiKind)}-{Language}-{width}x{height}.json"
        );

    /// <summary>Argument checks (no game access).</summary>
    /// <param name="args">Tool arguments.</param>
    internal static void Preflight(JsonElement args)
    {
        Catalog.ValidateObject(args, ["gadgets"]);
        if (!args.TryGetProperty("gadgets", out var g))
            return;
        if (g.ValueKind != JsonValueKind.Array || g.GetArrayLength() == 0)
            throw new ArgumentException("gadgets: non-empty array of gadget open names.");
        foreach (var e in g.EnumerateArray())
            if (e.ValueKind != JsonValueKind.String || e.GetString()!.Trim().Length == 0)
                throw new ArgumentException("gadgets: non-empty strings.");
    }

    /// <summary>Per-gadget working state across the poses.</summary>
    sealed class Work(string gadget)
    {
        /// <summary>Gadget open name.</summary>
        internal string Gadget => gadget;

        /// <summary>Open frame at pose 0 (colours written to the file).</summary>
        internal ScreenProbe.Frame? Open { get; set; }

        /// <summary>Pixels differing from the closed frame and stable across every pose so far.</summary>
        internal bool[,]? Opaque { get; set; }

        /// <summary>Pixels differing from the pre-open frame at any pose so far (opaque plus translucent window parts; a
        /// translucent body shows no change over the black off-map void, so the union is kept; the signature bounds come
        /// from its largest region).</summary>
        internal bool[,]? Shown { get; set; }

        /// <summary>Catalog window rect in frame pixels [x0,y0,x1,y1] grown by the nominal-edge margin; null when the
        /// catalog has no rect.</summary>
        internal int[]? Predicted { get; set; }

        /// <summary>Skip reason; null while calibrating.</summary>
        internal string? Skipped { get; set; }

        /// <summary>Close names that closed the dialog.</summary>
        internal string? ClosedBy { get; set; }

        /// <summary>Frame pixels inside catalog control rects (state-dependent: enabled/disabled, hover, text).</summary>
        internal bool[,]? Controls { get; set; }

        /// <summary>Catalog controls excluded from point selection.</summary>
        internal int ControlCount { get; set; }
    }

    /// <summary>Calibrates and writes the signature file. Moves the camera (restored at the end) and opens/closes every
    /// gadget; no scenario data changes.</summary>
    /// <param name="game">Editor connection.</param>
    /// <param name="hash">Pinned exe SHA-256.</param>
    /// <param name="catalog">Dialog catalog.</param>
    /// <param name="sets">Shipped signature sets.</param>
    /// <param name="native">Native bridge.</param>
    /// <param name="lookAt">Centres the camera on world (x, z).</param>
    /// <param name="args">Tool arguments.</param>
    internal static object Calibrate(
        Game game,
        string hash,
        DialogState.Dialogs catalog,
        DialogState.SignatureSet[] sets,
        DialogState.Native native,
        Action<double, double> lookAt,
        JsonElement args
    )
    {
        var (width, height) = Ui.ClientSize(game);
        var frame0 = SceneUi.Capture(game);
        var ui =
            SceneUi.TryLoad(game, frame0, out var reason)
            ?? throw new WorkflowFailure(
                "UI_UNKNOWN",
                "preflight",
                "UI kind not detected (close dialogs so the minimap gate shows): " + reason,
                false,
                false,
                "Close open dialogs, screenshot, retry; no input sent."
            );
        if (!frame0.IsReference)
            throw new WorkflowFailure(
                "UI_LAYOUT_UNREVIEWED",
                "preflight",
                $"Signatures use the 1280x720 frame; client {width}x{height} is not 16:9.",
                false,
                false,
                "Use a 16:9 client; no input sent."
            );
        var name = FileName(ui.Kind, width, height);
        var path = Path.Combine(AppContext.BaseDirectory, LayoutDirectory, name);
        if (File.Exists(path))
            throw new WorkflowFailure(
                "FILE_EXISTS",
                "preflight",
                $"{name} already exists; calibration never overwrites a signature file.",
                false,
                false,
                "Delete or rename the file deliberately (and review it) before recalibrating; no input sent."
            );
        var gadgets = args.TryGetProperty("gadgets", out var g)
            ? g.EnumerateArray().Select(e => e.GetString()!.Trim()).Distinct().ToArray()
            : DefaultGadgets(sets, hash, catalog, ui.Kind);
        if (gadgets.Length == 0)
            throw new ArgumentException("No gadgets to calibrate for this UI.");
        var work = gadgets.Select(n => new Work(n)).ToArray();
        var command = Catalog.Require(native.Commands, "gadgetReal");
        var unreal = Catalog.Require(native.Commands, "gadgetUnreal");
        var view = EditorView.ReadView(game);
        var start = view.Target();
        var dispatched = new List<string>();
        var closed = new List<ScreenProbe.Frame>();
        var total = Poses.Length * work.Length;
        var done = 0;
        try
        {
            for (var p = 0; p < Poses.Length; p++)
            {
                lookAt(view.WorldWidth * Poses[p].X, view.WorldDepth * Poses[p].Z);
                Thread.Sleep(SettleMs);
                var baseline = SceneUi.Capture(game);
                closed.Add(baseline);
                foreach (var w in work)
                {
                    ProgressReporter.Report(done++, total, $"pose {p + 1}: {w.Gadget}");
                    if (w.Skipped is not null)
                        continue;
                    native.RequireNoCursor();
                    var prior = SceneUi.Capture(game);
                    Thread.Sleep(SettleMs);
                    var before = SceneUi.Capture(game);
                    var noise = Grow(Moving(prior, before), NoiseGrow);
                    Run(native, command, w.Gadget, dispatched);
                    Thread.Sleep(SettleMs);
                    var open = SceneUi.Capture(game);
                    var changed = Without(Changed(open, before), noise);
                    if (Count(changed) < MinOpenPixels)
                    {
                        w.Skipped =
                            p == 0
                                ? "no visible change after gadgetReal (not a window in this UI, or already open)"
                                : $"no visible change after gadgetReal at pose {p + 1}";
                        continue;
                    }
                    Close(game, native, unreal, catalog, ui.Kind, w, open, changed, dispatched);
                    if (p == 0)
                    {
                        w.Open = open;
                        w.Opaque = changed;
                        w.Shown = changed;
                        w.Predicted = Predicted(
                            DialogState.Resolve(catalog.Windows, ui.Kind, w.Gadget),
                            open,
                            width,
                            height
                        );
                        (w.Controls, w.ControlCount) = ControlMask(
                            DialogState.Resolve(catalog.Windows, ui.Kind, w.Gadget),
                            open,
                            width,
                            height
                        );
                    }
                    else
                    {
                        w.Opaque = And(w.Opaque!, changed, Stable(open, w.Open!));
                        w.Shown = Or(w.Shown!, changed);
                    }
                }
            }
        }
        finally
        {
            if (start is { } s)
                try
                {
                    lookAt(s.X, s.Z);
                }
                catch (Exception e) when (e is WorkflowFailure or InvalidOperationException)
                {
                    // The calibration result is still valid; the camera stays at the last pose.
                }
        }
        var signatures = new Dictionary<string, DialogState.Signature>(StringComparer.Ordinal);
        var report = new List<object>();
        foreach (var w in work.Where(w => w.Skipped is null))
        {
            var (bounds, points) = Points(w, work, closed);
            if (points.Length < MinPoints)
            {
                w.Skipped = $"only {points.Length} distinct opaque points (need {MinPoints})";
                continue;
            }
            signatures[w.Gadget] = new(w.Gadget, bounds, points);
        }
        var set = new DialogState.SignatureSet(
            name,
            ui.Kind,
            width,
            height,
            hash,
            Tolerance,
            OpenFraction,
            PartialFraction,
            signatures
        );
        foreach (var (gadget, signature) in signatures)
        {
            var own = work.First(w => w.Gadget == gadget).Open!;
            report.Add(
                new
                {
                    gadget,
                    points = signature.Points.Length,
                    controlsExcluded = work.First(w => w.Gadget == gadget).ControlCount,
                    bounds = signature.Bounds,
                    closedBy = work.First(w => w.Gadget == gadget).ClosedBy,
                    ownScore = DialogState.Score(own, signature, set).Score,
                    maxClosedScore = closed.Max(c => DialogState.Score(c, signature, set).Score),
                    maxRivalScore = work.Where(w => w.Gadget != gadget && w.Open is not null)
                        .Select(w => DialogState.Score(w.Open!, signature, set).Score)
                        .DefaultIfEmpty(0)
                        .Max(),
                }
            );
        }
        if (signatures.Count == 0)
            throw new WorkflowFailure(
                "NOTHING_CALIBRATED",
                "verify",
                "No gadget produced a usable signature; no file written.",
                dispatched.Count > 0,
                false,
                "Screenshot; check that gadgets open in this UI. Skipped: "
                    + string.Join("; ", work.Select(w => $"{w.Gadget}: {w.Skipped}"))
            );
        var json = Serialize(set, ui.Kind);
        _ = DialogState.Parse(name, json);
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            writer.Write(json);
        return new
        {
            uiKind = ui.Kind,
            client = new { width, height },
            file = path,
            calibrated = signatures.Count,
            gadgets = report,
            skipped = work.Where(w => w.Skipped is not null)
                .Select(w => new { gadget = w.Gadget, reason = w.Skipped }),
            dispatchedCount = dispatched.Count,
            cameraRestored = start is not null,
            note = "Review the file (ownScore 1.0, maxClosedScore and maxRivalScore well below partialFraction) and copy it to the repository uilayouts/ to ship it; this host already loads it.",
        };
    }

    /// <summary>Default gadgets: every gadget calibrated in another shipped set of this build (normal UI: the 2560 set);
    /// for the alternative UI only those its catalog resolves or its own sets list.</summary>
    /// <param name="sets">Shipped sets.</param>
    /// <param name="hash">Pinned exe SHA-256.</param>
    /// <param name="catalog">Dialog catalog.</param>
    /// <param name="uiKind">Detected UI kind.</param>
    static string[] DefaultGadgets(
        DialogState.SignatureSet[] sets,
        string hash,
        DialogState.Dialogs catalog,
        string uiKind
    )
    {
        var own = sets.Where(s => s.BuildHash == hash && s.UiKind == uiKind)
            .SelectMany(s => s.Dialogs.Keys);
        var other = sets.Where(s => s.BuildHash == hash && s.UiKind != uiKind)
            .SelectMany(s => s.Dialogs.Keys)
            .Where(k => DialogState.Resolve(catalog.Windows, uiKind, k) is not null);
        return
        [
            .. own.Concat(other).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>Dispatches gadgetReal/gadgetUnreal(name).</summary>
    /// <param name="native">Native bridge.</param>
    /// <param name="command">gadgetReal or gadgetUnreal.</param>
    /// <param name="name">Gadget/window name.</param>
    /// <param name="dispatched">Dispatched expressions (appended).</param>
    static void Run(
        DialogState.Native native,
        Command command,
        string name,
        List<string> dispatched
    )
    {
        var expression = Catalog.Build(
            command,
            DialogState.Json(
                new Dictionary<string, object?> { [command.Parameters[0].Name] = name }
            )
        );
        DialogState.Dispatch(native, expression, dispatched);
        dispatched.Add(expression);
    }

    /// <summary>Closes the dialog with the catalog close names in order until the open-only pixels no longer show open
    /// colours; throws DIALOG_STILL_OPEN (no file written) otherwise.</summary>
    static void Close(
        Game game,
        DialogState.Native native,
        Command unreal,
        DialogState.Dialogs catalog,
        string uiKind,
        Work w,
        ScreenProbe.Frame open,
        bool[,] changed,
        List<string> dispatched
    )
    {
        var target = DialogState.Resolve(catalog.Windows, uiKind, w.Gadget);
        foreach (var name in DialogState.CloseNames(w.Gadget, target))
        {
            Run(native, unreal, name, dispatched);
            Thread.Sleep(SettleMs);
            if (StillOpen(SceneUi.Capture(game), open, changed) < StillOpenFraction)
            {
                w.ClosedBy = name;
                return;
            }
        }
        throw new WorkflowFailure(
            "DIALOG_STILL_OPEN",
            "verify",
            $"{w.Gadget} stayed open after gadgetUnreal with {string.Join(", ", DialogState.CloseNames(w.Gadget, target))}; calibration stopped, no file written.",
            true,
            false,
            "Close the dialog via its own Close button or ESC, screenshot, then rerun with gadgets excluding it."
        );
    }

    /// <summary>Margin in frame pixels around excluded control rects (host choice: catalog rects are size1024 geometry,
    /// a pixel or two off the live widget).</summary>
    const int ControlMargin = 2;

    /// <summary>Catalog window rect of <paramref name="target"/> in frame pixels [x0,y0,x1,y1], grown by
    /// <see cref="DialogState.NominalEdge"/> (2560 reference) so it bounds every window the live check calls nominal.</summary>
    /// <param name="target">Resolved catalog target; null gives null.</param>
    /// <param name="frame">Frame (size).</param>
    /// <param name="width">Client width.</param>
    /// <param name="height">Client height.</param>
    static int[]? Predicted(
        DialogState.Target? target,
        ScreenProbe.Frame frame,
        int width,
        int height
    )
    {
        if (target is null || DialogState.TargetRect(target) is not { Length: 4 } rect)
            return null;
        var r = DialogState.ToClient(rect, width, height);
        var edge = DialogState.NominalEdge / ReferencePerFrame;
        return
        [
            r[0] * frame.Width / width - edge,
            r[1] * frame.Height / height - edge,
            (r[0] + r[2]) * frame.Width / width + edge,
            (r[1] + r[3]) * frame.Height / height + edge,
        ];
    }

    /// <summary>Frame pixels of every non-container catalog control (buttons, fields, lists, text...): their colours
    /// depend on state (session 4 live: Camera Start Show/Delete turned from disabled 62 to enabled 96 grey after Set,
    /// which broke a signature sampled on them).</summary>
    /// <param name="target">Catalog target (null: nothing excluded).</param>
    /// <param name="frame">Open frame.</param>
    /// <param name="width">Client width.</param>
    /// <param name="height">Client height.</param>
    static (bool[,]? Mask, int Count) ControlMask(
        DialogState.Target? target,
        ScreenProbe.Frame frame,
        int width,
        int height
    )
    {
        if (target is null)
            return (null, 0);
        var mask = new bool[frame.Width, frame.Height];
        var count = 0;
        foreach (var c in target.Controls)
        {
            if (c.Size1024 is not { Length: 4 } || DialogState.ControlKind(c) == "container")
                continue;
            count++;
            var r = DialogState.ToClient(c.Size1024, width, height);
            int x0 = Math.Max(0, r[0] * frame.Width / width - ControlMargin),
                y0 = Math.Max(0, r[1] * frame.Height / height - ControlMargin),
                x1 = Math.Min(frame.Width - 1, (r[0] + r[2]) * frame.Width / width + ControlMargin),
                y1 = Math.Min(
                    frame.Height - 1,
                    (r[1] + r[3]) * frame.Height / height + ControlMargin
                );
            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
                mask[x, y] = true;
        }
        return (mask, count);
    }

    /// <summary>Largest per-channel difference of two colours.</summary>
    static int Diff(ScreenProbe.Rgb a, ScreenProbe.Rgb b) =>
        Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    /// <summary>Frame pixels whose colour differs from the closed frame by more than <see cref="ClosedMargin"/>.</summary>
    static bool[,] Changed(ScreenProbe.Frame open, ScreenProbe.Frame closed)
    {
        var mask = new bool[open.Width, open.Height];
        for (var y = 0; y < open.Height; y++)
        for (var x = 0; x < open.Width; x++)
            mask[x, y] = Diff(open.At(x, y), closed.At(x, y)) > ClosedMargin;
        return mask;
    }

    /// <summary>Frame pixels whose colour changed by more than <see cref="Tolerance"/> between two frames taken
    /// <see cref="SettleMs"/> apart with no input (ambient animation such as drifting cloud shadows).</summary>
    static bool[,] Moving(ScreenProbe.Frame a, ScreenProbe.Frame b)
    {
        var mask = new bool[a.Width, a.Height];
        for (var y = 0; y < a.Height; y++)
        for (var x = 0; x < a.Width; x++)
            mask[x, y] = Diff(a.At(x, y), b.At(x, y)) > Tolerance;
        return mask;
    }

    /// <summary>Mask grown by <paramref name="radius"/> frame pixels (square neighbourhood).</summary>
    static bool[,] Grow(bool[,] mask, int radius)
    {
        int w = mask.GetLength(0),
            h = mask.GetLength(1);
        var grown = new bool[w, h];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (!mask[x, y])
                continue;
            for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                int gx = x + dx,
                    gy = y + dy;
                if (gx >= 0 && gy >= 0 && gx < w && gy < h)
                    grown[gx, gy] = true;
            }
        }
        return grown;
    }

    /// <summary>Pixels set in <paramref name="mask"/> and not in <paramref name="exclude"/>.</summary>
    static bool[,] Without(bool[,] mask, bool[,] exclude)
    {
        var result = new bool[mask.GetLength(0), mask.GetLength(1)];
        for (var x = 0; x < mask.GetLength(0); x++)
        for (var y = 0; y < mask.GetLength(1); y++)
            result[x, y] = mask[x, y] && !exclude[x, y];
        return result;
    }

    /// <summary>Frame pixels whose colour is the same (within <see cref="StableTolerance"/>) in two open frames.</summary>
    static bool[,] Stable(ScreenProbe.Frame a, ScreenProbe.Frame b)
    {
        var mask = new bool[a.Width, a.Height];
        for (var y = 0; y < a.Height; y++)
        for (var x = 0; x < a.Width; x++)
            mask[x, y] = Diff(a.At(x, y), b.At(x, y)) <= StableTolerance;
        return mask;
    }

    /// <summary>Pixelwise OR of two masks.</summary>
    static bool[,] Or(bool[,] a, bool[,] b)
    {
        var mask = new bool[a.GetLength(0), a.GetLength(1)];
        for (var x = 0; x < a.GetLength(0); x++)
        for (var y = 0; y < a.GetLength(1); y++)
            mask[x, y] = a[x, y] || b[x, y];
        return mask;
    }

    /// <summary>Pixelwise AND of three masks.</summary>
    static bool[,] And(bool[,] a, bool[,] b, bool[,] c)
    {
        var mask = new bool[a.GetLength(0), a.GetLength(1)];
        for (var x = 0; x < a.GetLength(0); x++)
        for (var y = 0; y < a.GetLength(1); y++)
            mask[x, y] = a[x, y] && b[x, y] && c[x, y];
        return mask;
    }

    /// <summary>Set pixels of a mask.</summary>
    static int Count(bool[,] mask)
    {
        var n = 0;
        foreach (var b in mask)
            if (b)
                n++;
        return n;
    }

    /// <summary>Fraction of the open-only pixels that still show their open colour.</summary>
    static double StillOpen(ScreenProbe.Frame now, ScreenProbe.Frame open, bool[,] changed)
    {
        int total = 0,
            same = 0;
        for (var y = 0; y < now.Height; y++)
        for (var x = 0; x < now.Width; x++)
        {
            if (!changed[x, y])
                continue;
            total++;
            if (Diff(now.At(x, y), open.At(x, y)) <= Tolerance)
                same++;
        }
        return total == 0 ? 0 : (double)same / total;
    }

    /// <summary>Bounds (2560 reference) of the largest 8-connected opaque region and up to <see cref="MaxPoints"/>
    /// spread points inside it that have a uniform 3x3 neighborhood and do not match any other gadget's open frame.
    /// Dark points (every channel at most <see cref="DarkMax"/>) are dropped: the off-map void and modal dimming render
    /// dark backgrounds at poses that were not sampled.</summary>
    /// <param name="w">Gadget.</param>
    /// <param name="all">All gadgets (rival frames).</param>
    /// <param name="closed">Closed frame per pose (opacity proof).</param>
    static (int[] Bounds, int[][] Points) Points(Work w, Work[] all, List<ScreenProbe.Frame> closed)
    {
        var mask = w.Opaque!;
        var open = w.Open!;
        int fw = mask.GetLength(0),
            fh = mask.GetLength(1);
        // Bounds cover the whole shown window when it fits the catalog rect: a translucent body (alternative-UI Camera
        // Spawn Point, session 4) is not opaque, and title-bar-only bounds broke the nominal-geometry check, OCR rect and
        // click containment. Modal dimming makes the shown region full-screen; then the opaque region is kept.
        var (x0, y0, x1, y1) = LargestRegion(mask);
        var shown = LargestRegion(w.Shown!);
        if (
            w.Predicted is { } p
            && shown.X1 >= shown.X0
            && shown.X0 >= p[0]
            && shown.Y0 >= p[1]
            && shown.X1 <= p[2]
            && shown.Y1 <= p[3]
        )
            (x0, y0, x1, y1) = shown;
        if (x1 < x0)
            return ([], []);
        var rivals = all.Where(r => r != w && r.Open is not null).Select(r => r.Open!).ToArray();
        var unique = new List<(int X, int Y)>();
        var edge = new List<(int X, int Y)>();
        var shared = new List<(int X, int Y)>();
        for (var y = Math.Max(1, y0); y <= Math.Min(fh - 2, y1); y++)
        for (var x = Math.Max(1, x0); x <= Math.Min(fw - 2, x1); x++)
        {
            if (!mask[x, y] || w.Controls?[x, y] == true || !Varied(closed, x, y))
                continue;
            var c = open.At(x, y);
            if (Math.Max(c.R, Math.Max(c.G, c.B)) <= DarkMax)
                continue;
            var uniform = true;
            for (var dy = -1; dy <= 1 && uniform; dy++)
            for (var dx = -1; dx <= 1 && uniform; dx++)
                uniform =
                    mask[x + dx, y + dy] && Diff(open.At(x + dx, y + dy), c) <= StableTolerance;
            var distinct = rivals.All(r => Diff(r.At(x, y), c) > Tolerance);
            if (distinct)
                (uniform ? unique : edge).Add((x, y));
            else if (uniform)
                shared.Add((x, y));
        }
        // Tiers: points no rival shares (uniform areas, then glyph/edge pixels such as the title, deterministic at the
        // calibrated size), then shared chrome while every rival matches fewer than MaxRivalShare of the points.
        var picked = new List<(int X, int Y)>();
        Sample(unique, picked, (x0, y0), _ => true);
        Sample(edge, picked, (x0, y0), _ => true);
        var sharedWith = new int[rivals.Length];
        Sample(
            shared,
            picked,
            (x0, y0),
            p =>
            {
                var c = open.At(p.X, p.Y);
                var hits = rivals.Select(r => Diff(r.At(p.X, p.Y), c) <= Tolerance).ToArray();
                for (var i = 0; i < rivals.Length; i++)
                    if (hits[i] && sharedWith[i] + 1 >= MaxRivalShare * (picked.Count + 1))
                        return false;
                for (var i = 0; i < rivals.Length; i++)
                    if (hits[i])
                        sharedWith[i]++;
                return true;
            }
        );
        int[] bounds =
        [
            x0 * ReferencePerFrame,
            y0 * ReferencePerFrame,
            (x1 - x0 + 1) * ReferencePerFrame,
            (y1 - y0 + 1) * ReferencePerFrame,
        ];
        return (
            bounds,
            [
                .. picked.Select(p =>
                {
                    var c = open.At(p.X, p.Y);
                    return new[] { p.X, p.Y, c.R, c.G, c.B };
                }),
            ]
        );
    }

    /// <summary>True when the closed background at a pixel changed by more than <see cref="BackgroundMargin"/> between
    /// some pose pair: only then does a stable open colour prove the dialog pixel opaque.</summary>
    /// <param name="closed">Closed frame per pose.</param>
    /// <param name="x">Frame x.</param>
    /// <param name="y">Frame y.</param>
    static bool Varied(List<ScreenProbe.Frame> closed, int x, int y)
    {
        for (var i = 0; i < closed.Count; i++)
        for (var j = i + 1; j < closed.Count; j++)
            if (Diff(closed[i].At(x, y), closed[j].At(x, y)) > BackgroundMargin)
                return true;
        return false;
    }

    /// <summary>Largest fraction of a signature's points one rival dialog may share (host choice: below
    /// <see cref="PartialFraction"/> with margin, so a rival never scores "partial").</summary>
    const double MaxRivalShare = 0.3;

    /// <summary>Farthest-point sampling: adds candidates (farthest from every picked point, starting at
    /// <paramref name="seed"/>) until <see cref="MaxPoints"/>; candidates <paramref name="accept"/> rejects are dropped.</summary>
    /// <param name="candidates">Candidate pixels.</param>
    /// <param name="picked">Picked pixels (appended).</param>
    /// <param name="seed">Start point when nothing is picked yet.</param>
    /// <param name="accept">Final check per candidate (may update caller state when it accepts).</param>
    static void Sample(
        List<(int X, int Y)> candidates,
        List<(int X, int Y)> picked,
        (int X, int Y) seed,
        Func<(int X, int Y), bool> accept
    )
    {
        var left = candidates.ToList();
        var distance = left.Select(c =>
                (picked.Count == 0 ? [seed] : picked).Min(p =>
                    Math.Pow(c.X - p.X, 2) + Math.Pow(c.Y - p.Y, 2)
                )
            )
            .ToList();
        while (picked.Count < MaxPoints && left.Count > 0)
        {
            var best = 0;
            for (var i = 1; i < left.Count; i++)
                if (distance[i] > distance[best])
                    best = i;
            if (distance[best] == 0)
                return;
            var point = left[best];
            left.RemoveAt(best);
            distance.RemoveAt(best);
            if (!accept(point))
                continue;
            picked.Add(point);
            for (var i = 0; i < left.Count; i++)
                distance[i] = Math.Min(
                    distance[i],
                    Math.Pow(left[i].X - point.X, 2) + Math.Pow(left[i].Y - point.Y, 2)
                );
        }
    }

    /// <summary>Bounding box (inclusive) of the largest 8-connected set region; x1 &lt; x0 when empty.</summary>
    /// <param name="mask">Mask.</param>
    internal static (int X0, int Y0, int X1, int Y1) LargestRegion(bool[,] mask)
    {
        int w = mask.GetLength(0),
            h = mask.GetLength(1);
        var seen = new bool[w, h];
        var best = (Size: 0, X0: 0, Y0: 0, X1: -1, Y1: -1);
        var stack = new Stack<(int X, int Y)>();
        for (var sy = 0; sy < h; sy++)
        for (var sx = 0; sx < w; sx++)
        {
            if (!mask[sx, sy] || seen[sx, sy])
                continue;
            int size = 0,
                x0 = sx,
                y0 = sy,
                x1 = sx,
                y1 = sy;
            seen[sx, sy] = true;
            stack.Push((sx, sy));
            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();
                size++;
                x0 = Math.Min(x0, x);
                y0 = Math.Min(y0, y);
                x1 = Math.Max(x1, x);
                y1 = Math.Max(y1, y);
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx,
                        ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h || seen[nx, ny] || !mask[nx, ny])
                        continue;
                    seen[nx, ny] = true;
                    stack.Push((nx, ny));
                }
            }
            if (size > best.Size)
                best = (size, x0, y0, x1, y1);
        }
        return (best.X0, best.Y0, best.X1, best.Y1);
    }

    /// <summary>Serializer options keeping the notes readable (no < escapes); the file is local data.</summary>
    static readonly JsonSerializerOptions Relaxed = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Signature file JSON (schema 1, one point per line).</summary>
    /// <param name="set">Calibrated set.</param>
    /// <param name="uiKind">UI kind.</param>
    static string Serialize(DialogState.SignatureSet set, string uiKind)
    {
        static string J(object v) => JsonSerializer.Serialize(v, Relaxed);
        var notes = string.Create(
            CultureInfo.InvariantCulture,
            $"Open-dialog pixel signatures, {uiKind} English editor UI at {set.Width}x{set.Height}. Points are [x,y,r,g,b] in the 1280x720 host screenshot frame; bounds are [x,y,w,h] in 2560-reference client pixels of the default (unmoved) window. Written by {Tool} on {DateTime.UtcNow:yyyy-MM-dd}: each gadget opened with gadgetReal and closed with gadgetUnreal at four camera poses (map centre, both corners, a quarter point); points are stable across poses (<= {StableTolerance}) while the closed background behind them changed (> {BackgroundMargin}, opacity proof), differ from a fresh pre-open frame by > {ClosedMargin} (pixels moving without input, e.g. cloud shadows, excluded), are not dark (max channel <= {DarkMax}: off-map void, modal dim), prefer a uniform 3x3 neighborhood, and are unique against every other calibrated dialog's open frame (shared chrome only while each rival matches < 30% of the points); catalog control rects (buttons, fields, lists, text) are excluded because their colours depend on state."
        );
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append(CultureInfo.InvariantCulture, $"  \"schema\": {Schema},\n");
        sb.Append(CultureInfo.InvariantCulture, $"  \"uiKind\": {J(set.UiKind)},\n");
        sb.Append(CultureInfo.InvariantCulture, $"  \"width\": {set.Width},\n");
        sb.Append(CultureInfo.InvariantCulture, $"  \"height\": {set.Height},\n");
        sb.Append(
            CultureInfo.InvariantCulture,
            $"  \"frame\": [{ScreenProbe.FrameWidth}, {ScreenProbe.ReferenceFrameHeight}],\n"
        );
        sb.Append(CultureInfo.InvariantCulture, $"  \"buildHash\": {J(set.BuildHash)},\n");
        sb.Append(CultureInfo.InvariantCulture, $"  \"tolerance\": {set.Tolerance},\n");
        sb.Append(CultureInfo.InvariantCulture, $"  \"openFraction\": {set.OpenFraction},\n");
        sb.Append(CultureInfo.InvariantCulture, $"  \"partialFraction\": {set.PartialFraction},\n");
        sb.Append(CultureInfo.InvariantCulture, $"  \"notes\": {J(notes)},\n");
        sb.Append("  \"dialogs\": {\n");
        var entries = set.Dialogs.Values.OrderBy(d => d.Gadget, StringComparer.Ordinal).ToArray();
        for (var i = 0; i < entries.Length; i++)
        {
            var d = entries[i];
            sb.Append(CultureInfo.InvariantCulture, $"    {J(d.Gadget)}: {{\n");
            sb.Append(CultureInfo.InvariantCulture, $"      \"bounds\": {J(d.Bounds)},\n");
            sb.Append("      \"points\": [\n");
            for (var j = 0; j < d.Points.Length; j++)
                sb.Append(
                    CultureInfo.InvariantCulture,
                    $"        {J(d.Points[j])}{(j + 1 < d.Points.Length ? "," : "")}\n"
                );
            sb.Append("      ]\n");
            sb.Append(
                CultureInfo.InvariantCulture,
                $"    }}{(i + 1 < entries.Length ? "," : "")}\n"
            );
        }
        sb.Append("  }\n}\n");
        return sb.ToString();
    }

    /// <summary>Side of the square frames in <see cref="MaskSelfTest"/>.</summary>
    const int MaskTestSide = 8;

    /// <summary>Offline check of the ambient-motion mask: one pixel moving between two idle frames is masked with its
    /// <see cref="NoiseGrow"/> neighbourhood, and Without/Or drop and restore it.</summary>
    static void MaskSelfTest()
    {
        var a = new byte[MaskTestSide * MaskTestSide * 3];
        var b = (byte[])a.Clone();
        const int Cx = 4,
            Cy = 4;
        b[(Cy * MaskTestSide + Cx) * 3] = byte.MaxValue;
        var moving = Moving(
            new ScreenProbe.Frame(a, MaskTestSide, MaskTestSide),
            new ScreenProbe.Frame(b, MaskTestSide, MaskTestSide)
        );
        var grown = Grow(moving, NoiseGrow);
        var all = new bool[MaskTestSide, MaskTestSide];
        for (var x = 0; x < MaskTestSide; x++)
        for (var y = 0; y < MaskTestSide; y++)
            all[x, y] = true;
        var kept = Without(all, grown);
        var restored = Or(kept, grown);
        if (
            !moving[Cx, Cy]
            || moving[Cx + 1, Cy]
            || !grown[Cx + NoiseGrow, Cy - NoiseGrow]
            || grown[Cx + NoiseGrow + 1, Cy]
            || kept[Cx, Cy]
            || !kept[0, 0]
            || !restored[Cx, Cy]
        )
            throw new InvalidOperationException("Dialog calibration motion-mask self-test failed.");
    }

    /// <summary>Offline checks: region finder, motion mask, file name, serializer round trip.</summary>
    internal static void SelfTest()
    {
        var mask = new bool[10, 10];
        mask[1, 1] = true;
        for (var x = 4; x <= 7; x++)
        for (var y = 5; y <= 8; y++)
            mask[x, y] = true;
        if (LargestRegion(mask) != (4, 5, 7, 8) || LargestRegion(new bool[2, 2]).X1 >= 0)
            throw new InvalidOperationException("Dialog calibration region self-test failed.");
        MaskSelfTest();
        if (
            FileName(Alternative, 1920, 1080) != "dialogs-alt-en-1920x1080.json"
            || FileName("normal", 1920, 1080) != "dialogs-normal-en-1920x1080.json"
        )
            throw new InvalidOperationException("Dialog calibration file name self-test failed.");
        int[][] points =
        [
            [1, 2, 3, 4, 5],
            [6, 7, 8, 9, 10],
            [11, 12, 13, 14, 15],
            [16, 17, 18, 19, 20],
        ];
        var set = new DialogState.SignatureSet(
            "x",
            "normal",
            1920,
            1080,
            new string('0', 64),
            Tolerance,
            OpenFraction,
            PartialFraction,
            new() { ["G"] = new("G", [0, 0, 10, 10], points) }
        );
        var parsed = DialogState.Parse("x", Serialize(set, "normal"));
        if (parsed.Dialogs["G"].Points.Length != points.Length || parsed.Width != 1920)
            throw new InvalidOperationException("Dialog calibration serializer self-test failed.");
    }
}
