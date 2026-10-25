using System.Globalization;

namespace AomMcp;

/// <summary>
/// Latest progress of the running tool call, read by the MCP progress heartbeat in <see cref="Server.RunAsync"/>.
/// Calls are serialized (one slot), so one process-wide sink is enough. Long tools report at natural boundaries
/// (stroke, chunk, pass); the heartbeat sends the latest report so clients re-arm their request timeout.
/// </summary>
internal static class ProgressReporter
{
    /// <summary>Guards the report fields between the tool thread and the heartbeat.</summary>
    static readonly Lock Gate = new();

    /// <summary>Units done of the latest report, or null before the first report of the call.</summary>
    static double? _done;

    /// <summary>Total units of the latest report, or null when unknown.</summary>
    static double? _total;

    /// <summary>Short description of the latest report.</summary>
    static string? _message;

    /// <summary>Clears the sink at the start of a tool call.</summary>
    internal static void Begin()
    {
        lock (Gate)
        {
            _done = null;
            _total = null;
            _message = null;
        }
    }

    /// <summary>Records progress of the running call (no I/O; the heartbeat sends it).</summary>
    /// <param name="done">Units done so far.</param>
    /// <param name="total">Total units, or null when unknown.</param>
    /// <param name="message">Short stage text, e.g. "stroke".</param>
    internal static void Report(double done, double? total, string message)
    {
        lock (Gate)
        {
            _done = done;
            _total = total;
            _message = message;
        }
    }

    /// <summary>Heartbeat text: latest report plus elapsed seconds, or elapsed seconds alone before any report.</summary>
    /// <param name="elapsed">Time since the call started.</param>
    /// <returns>Notification message.</returns>
    internal static string Describe(TimeSpan elapsed)
    {
        var seconds = ((int)elapsed.TotalSeconds).ToString(CultureInfo.InvariantCulture) + " s";
        lock (Gate)
        {
            if (_done is not { } done)
                return "running, " + seconds;
            var count = _total is { } total
                ? done.ToString(CultureInfo.InvariantCulture)
                    + "/"
                    + total.ToString(CultureInfo.InvariantCulture)
                : done.ToString(CultureInfo.InvariantCulture);
            return _message + " " + count + ", " + seconds;
        }
    }

    /// <summary>Checks message composition before/after a report and reset by <see cref="Begin"/>.</summary>
    internal static void SelfTest()
    {
        Begin();
        var elapsed = TimeSpan.FromSeconds(12);
        if (Describe(elapsed) != "running, 12 s")
            throw new InvalidOperationException("Progress heartbeat text without report failed.");
        Report(3, 10, "stroke");
        if (Describe(elapsed) != "stroke 3/10, 12 s")
            throw new InvalidOperationException("Progress heartbeat text with report failed.");
        Report(4, null, "pass");
        if (Describe(elapsed) != "pass 4, 12 s")
            throw new InvalidOperationException("Progress heartbeat text without total failed.");
        Begin();
        if (Describe(elapsed) != "running, 12 s")
            throw new InvalidOperationException("Progress reset failed.");
    }
}
