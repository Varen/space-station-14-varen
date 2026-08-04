using Content.Shared.Atmos.AirlockController;
using Robust.Shared.Audio;

namespace Content.Server.Atmos.AirlockController.Components;

/// <summary>
///     Wallmount outside the airlock that calls the chamber over. Everything written
///     by the controller that has it assigned.
/// </summary>
[RegisterComponent]
public sealed partial class AirlockCyclerComponent : Component
{
    /// <summary>
    ///     Null until a controller claims us
    /// </summary>
    [ViewVariables]
    public EntityUid? Controller;

    /// <summary>
    ///     The side we call the chamber to
    /// </summary>
    [ViewVariables]
    public AirlockSide Side;

    #region Mirrored state

    [ViewVariables]
    public AirlockCycleState State = AirlockCycleState.Idle;

    [ViewVariables]
    public AirlockSide CurrentSide;

    [ViewVariables]
    public AirlockStallReason? StallReason;

    [ViewVariables]
    public bool MaintenanceMode = true;

    [ViewVariables]
    public float? ChamberPressure;

    /// <summary>
    ///     Light and alarm, the controller decides when
    /// </summary>
    [ViewVariables]
    public bool Warning;

    #endregion

    #region Cycle warning

    [DataField]
    public SoundSpecifier CycleSound = new SoundPathSpecifier("/Audio/Machines/alarm.ogg");

    [DataField]
    public float CycleVolume = -10f;

    [DataField]
    public TimeSpan CycleSoundInterval = TimeSpan.FromSeconds(6);

    [ViewVariables]
    public TimeSpan NextCycleSound;

    #endregion
}
