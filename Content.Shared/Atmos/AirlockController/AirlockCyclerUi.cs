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

    public AirlockSide Side;

    public AirlockCycleState State;
    public AirlockSide CurrentSide;
    public AirlockStallReason? StallReason;
    public bool MaintenanceMode;

    public float? ChamberPressure;
}

[Serializable, NetSerializable]
public sealed class AirlockCyclerCycleMessage : BoundUserInterfaceMessage
{
}
