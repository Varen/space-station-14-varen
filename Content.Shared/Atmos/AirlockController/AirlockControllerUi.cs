using Robust.Shared.Serialization;

namespace Content.Shared.Atmos.AirlockController;

/// <summary>
///     One device bound to the controller, with whatever roles it currently has.
/// </summary>
[Serializable, NetSerializable]
public sealed class AirlockDeviceEntry
{
    public NetEntity Device;

    /// <summary>
    ///     Display only
    /// </summary>
    public string Address = string.Empty;

    public string Name = string.Empty;

    public bool IsVent;

    // Scrubbers can only siphon
    public bool IsScrubber;

    public bool IsSensor;
    public bool IsDoor;

    public AirlockVentRole VentRoles;

    public AirlockSide? DoorSide;
}

/// <summary>
///     Sensor option to be used outside the chamber on a side
/// </summary>
[Serializable, NetSerializable]
public sealed class AirlockSensorOption
{
    public NetEntity Device;
    public string Name = string.Empty;
}

/// <summary>
///     Main panel view status
/// </summary>
[Serializable, NetSerializable]
public sealed class AirlockControllerUiState : BoundUserInterfaceState
{
    public AirlockCycleState State;
    public AirlockSide CurrentSide;
    public AirlockSide TargetSide;
    public AirlockStallReason? StallReason;
    public bool CancelRequested;
    public bool MaintenanceMode;

    public float? ChamberPressure;
}

/// <summary>
///     Configuration view status.
/// </summary>
[Serializable, NetSerializable]
public sealed class AirlockControllerConfigUiState : BoundUserInterfaceState
{
    public List<AirlockDeviceEntry> Devices = new();

    public string Address = string.Empty;

    public int DoorCount;

    public float PresetPressureA;
    public float PresetPressureB;
    public bool MaintenanceMode;
    public AirlockSide CurrentSide;

    /// <summary>
    ///     Everything a side can be pointed at, Custom pressure is null.
    /// </summary>
    public List<AirlockSensorOption> Sensors = new();

    public NetEntity? TargetSensorA;
    public NetEntity? TargetSensorB;

    /// <summary>
    ///     What the chosen sensor reads, shown in place of the input.
    /// </summary>
    public float? TargetPressureA;
    public float? TargetPressureB;
}

[Serializable, NetSerializable]
public sealed class AirlockControllerCycleMessage : BoundUserInterfaceMessage
{
    public AirlockSide Side;

    public AirlockControllerCycleMessage(AirlockSide side)
    {
        Side = side;
    }
}

[Serializable, NetSerializable]
public sealed class AirlockControllerCancelMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class AirlockControllerOpenConfigMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class AirlockControllerSetVentRolesMessage : BoundUserInterfaceMessage
{
    public NetEntity Device;
    public AirlockVentRole Roles;

    public AirlockControllerSetVentRolesMessage(NetEntity device, AirlockVentRole roles)
    {
        Device = device;
        Roles = roles;
    }
}

[Serializable, NetSerializable]
public sealed class AirlockControllerSetDoorSideMessage : BoundUserInterfaceMessage
{
    public NetEntity Device;

    public AirlockSide? Side;

    public AirlockControllerSetDoorSideMessage(NetEntity device, AirlockSide? side)
    {
        Device = device;
        Side = side;
    }
}

[Serializable, NetSerializable]
public sealed class AirlockControllerSetTargetSensorMessage : BoundUserInterfaceMessage
{
    public AirlockSide Side;

    public NetEntity? Device;

    public AirlockControllerSetTargetSensorMessage(AirlockSide side, NetEntity? device)
    {
        Side = side;
        Device = device;
    }
}

[Serializable, NetSerializable]
public sealed class AirlockControllerSetPresetMessage : BoundUserInterfaceMessage
{
    public AirlockSide Side;
    public float Pressure;

    public AirlockControllerSetPresetMessage(AirlockSide side, float pressure)
    {
        Side = side;
        Pressure = pressure;
    }
}

[Serializable, NetSerializable]
public sealed class AirlockControllerSetMaintenanceMessage : BoundUserInterfaceMessage
{
    public bool Enabled;

    public AirlockControllerSetMaintenanceMessage(bool enabled)
    {
        Enabled = enabled;
    }
}

[Serializable, NetSerializable]
public sealed class AirlockControllerForceSideMessage : BoundUserInterfaceMessage
{
    public AirlockSide Side;

    public AirlockControllerForceSideMessage(AirlockSide side)
    {
        Side = side;
    }
}
