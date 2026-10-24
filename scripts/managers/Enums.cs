public enum LogLevel
{
    DEBUG,
    INFO,
    WARNING,
    ERROR
}

public enum CursorKind
{
    Default,
    Arrow,
    IBeam,
    Crosshair,
    PointingHand,
    Wait,
    Busy,
    ResizeLeftRight,
    ResizeUpDown,
    Help,
    None
}

/// <summary>
/// How far Mara's memory has destabilised through over-examination.
/// Thresholds live in <see cref="GameState"/>.
/// </summary>
public enum CorruptionTier
{
    /// <summary>The house as she insists it is.</summary>
    Calm = 0,
    /// <summary>Small wrongness at the edges.</summary>
    Unease = 1,
    /// <summary>The memory audibly and visibly frays.</summary>
    Fractured = 2,
    /// <summary>The memory can barely hold its shape.</summary>
    Shattered = 3
}

/// <summary>Outcome of trying to place a shard in Room 7.</summary>
public enum ShardPlaceResult
{
    Placed,
    AlreadyPlaced,
    NoShardCarried,
    AllPlaced
}

/// <summary>
/// The late-game ways of remembering. They are earned one room at a time,
/// then combined: Echo hears order, Anchor holds one truth still, Trace
/// connects causes, and Release lets a completed memory change Mara instead
/// of merely preserving her inside it.
/// </summary>
public enum MemoryVerb
{
    None = 0,
    Echo = 1,
    Anchor = 2,
    Trace = 3,
    Release = 4
}
