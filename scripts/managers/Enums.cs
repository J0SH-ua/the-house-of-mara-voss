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
