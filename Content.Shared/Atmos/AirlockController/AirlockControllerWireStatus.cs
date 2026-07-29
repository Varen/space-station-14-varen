using Robust.Shared.Serialization;

namespace Content.Shared.Atmos.AirlockController;

[Serializable, NetSerializable]
public enum AirlockControllerWireStatus : byte
{
    BoltingIndicator,
    EmergencyLightIndicator,
}
