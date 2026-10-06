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

    internal WorkflowFailure(string code, string phase, string message, bool nativeDispatched,
        bool outcomeUnknown, string nextAction, string? stagingPath = null, string? destinationPath = null,
        Exception? inner = null) : base(message, inner)
    {
        Code = code;
        Phase = phase;
        NativeDispatched = nativeDispatched;
        OutcomeUnknown = outcomeUnknown;
        StagingPath = stagingPath;
        DestinationPath = destinationPath;
        NextAction = nextAction;
    }

    internal object Details => new
    {
        code = Code,
        phase = Phase,
        message = Message,
        nativeDispatched = NativeDispatched,
        outcomeUnknown = OutcomeUnknown,
        retrySafe = !NativeDispatched,
        stagingPath = StagingPath,
        destinationPath = DestinationPath,
        nextAction = NextAction,
    };
}
