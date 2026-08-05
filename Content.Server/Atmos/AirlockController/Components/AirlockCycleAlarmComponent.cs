using Robust.Shared.Audio;

namespace Content.Server.Atmos.AirlockController.Components;

/// <summary>
///     Repeating warning for anything that shows an airlock cycle, controller or panel.
/// </summary>
[RegisterComponent]
public sealed partial class AirlockCycleAlarmComponent : Component
{
    /// <summary>
    ///     Set from the cycle status
    /// </summary>
    [ViewVariables]
    public bool Active;

    [DataField]
    public SoundSpecifier Sound = new SoundPathSpecifier("/Audio/Machines/alarm.ogg");

    [DataField]
    public float Volume = -10f;

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(6);

    [ViewVariables]
    public TimeSpan NextSound;
}
