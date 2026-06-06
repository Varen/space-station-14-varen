using Content.Shared.Atmos.Monitor.Components;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Atmos.Piping.Binary.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class GasHeatPumpComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Enabled = false;

    [DataField, AutoNetworkedField]
    public bool Blocked = false;

    [DataField]
    public string RegulatedName = "regulated";

    [DataField]
    public string ExternalName = "external";

    /// <summary>
    ///   Target tempfor the regulated pipe side
    /// </summary>
    [DataField, AutoNetworkedField]
    public float TargetTemperature = Atmospherics.T20C;

    /// <summary>
    ///     How much heat the pump moves between the two pipe sides  (joules per second).
    ///     Not dependant on actual electricity it draws
    ///		High number = Faster heat transfer
    /// </summary>
    [DataField]
    public float HeatTransferRate = 5000f;

    /// <summary>
    ///     Minim pressure before the pump enters Blocked state.
    /// </summary>
    [DataField]
    public float MinExternalPressure = 0.5f;

    /// <summary>
    ///     Is either side above / below min or max temperature?
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool TemperatureLocked = false;

    /// <summary>
    ///     Min temperature below which it locks out.
    /// </summary>
    [DataField]
    public float MinOperatingTemperature = Atmospherics.T0C - 100f;

    /// <summary>
    ///     Max temp above which it locks out
    /// </summary>
    [DataField]
    public float MaxOperatingTemperature = Atmospherics.T0C + 100f;

    public GasHeatPumpData ToAirAlarmData() => new()
    {
        Enabled = Enabled,
        Dirty = false,
        IgnoreAlarms = false,
        TargetTemperature = TargetTemperature,
        MinOperatingTemperature = MinOperatingTemperature,
        MaxOperatingTemperature = MaxOperatingTemperature,
    };

    public void FromAirAlarmData(GasHeatPumpData data)
    {
        Enabled = data.Enabled;
        TargetTemperature = data.TargetTemperature;
    }
}

[Serializable, NetSerializable]
public sealed class GasHeatPumpData : IAtmosDeviceData
{
    public bool Enabled { get; set; }
    public bool Dirty { get; set; }
    public bool IgnoreAlarms { get; set; }
    public float TargetTemperature { get; set; }
    public float MinOperatingTemperature { get; set; }
    public float MaxOperatingTemperature { get; set; }
}
