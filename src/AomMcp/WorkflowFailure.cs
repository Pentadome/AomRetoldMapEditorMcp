namespace AomMcp;

/// <summary>Machine-readable failure context; never implies a dispatched native call was rolled back.</summary>
internal sealed class WorkflowFailure : Exception
{
    internal string Code { get; }
    internal string Phase { get; }
    internal bool NativeDispatched { get; }
    internal bool OutcomeUnknown { get; }
    internal string? StagingPath { get; }
    internal string? DestinationPath { get; }
    internal string NextAction { get; }
    internal string[] RetainedPaths { get; }

    /// <summary>Native edit dispatches (clicks/strokes) already applied before the failure, each expected to need one
    /// editor_undo; null when not counted. Host count, not a read of the native undo stack.</summary>
    internal int? UndoSteps { get; }

    /// <summary>Explicit retry safety when the host verified the scene is unchanged after a dispatch (e.g. no new
    /// object after a rescan); null derives it from the dispatch/outcome flags.</summary>
    internal bool? RetrySafe { get; }

    internal WorkflowFailure(
        string code,
        string phase,
        string message,
        bool nativeDispatched,
        bool outcomeUnknown,
        string nextAction,
        string? stagingPath = null,
        string? destinationPath = null,
        Exception? inner = null,
        string[]? retainedPaths = null,
        int? undoSteps = null,
        bool? retrySafe = null
    )
        : base(message, inner)
    {
        Code = code;
        Phase = phase;
        NativeDispatched = nativeDispatched;
        OutcomeUnknown = outcomeUnknown;
        StagingPath = stagingPath;
        DestinationPath = destinationPath;
        NextAction = nextAction;
        RetainedPaths = retainedPaths ?? [];
        UndoSteps = undoSteps;
        RetrySafe = retrySafe;
    }

    internal object Details =>
        new
        {
            code = Code,
            phase = Phase,
            message = Message,
            nativeDispatched = NativeDispatched,
            outcomeUnknown = OutcomeUnknown,
            retrySafe = RetrySafe ?? (!NativeDispatched && !OutcomeUnknown),
            retainedPaths = RetainedPaths,
            stagingPath = StagingPath,
            destinationPath = DestinationPath,
            nextAction = NextAction,
            undoSteps = UndoSteps,
        };
}
