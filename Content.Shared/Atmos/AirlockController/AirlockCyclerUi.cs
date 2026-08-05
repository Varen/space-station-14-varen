using Robust.Shared.Serialization;

namespace Content.Shared.Atmos.AirlockController;

[Serializable, NetSerializable]
public enum AirlockCyclerUiKey : byte
{
    Key,
}

/// <summary>
///     What the panel mirrors from its controller. Read only, all it can do is ask
/// </summary>
[Serializable, NetSerializable]
public sealed class AirlockCyclerUiState : BoundUserInterfaceState
{
    /// <summary>
    ///     False when no controller has it assigned
    /// </summary>
    public bool Bound;

    /// <summary>
    ///     The side we call the chamber to
    /// </summary>
    public AirlockSide Side;

    public AirlockCycleStatus Status;

    public float? ChamberPressure;
}

[Serializable, NetSerializable]
public sealed class AirlockCyclerCycleMessage : BoundUserInterfaceMessage
{
}
