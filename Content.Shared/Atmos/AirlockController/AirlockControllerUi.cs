using Robust.Shared.Serialization;

namespace Content.Shared.Atmos.AirlockController;

/// <summary>
///     One device bound to the controller, with whatever roles it currently has.
/// </summary>
[Serializable, NetSerializable]
public sealed class AirlockDeviceEntry
{
    public string Address = string.Empty;
    public string Name = string.Empty;

    public bool IsVent;

    // Scrubbers can only siphon
    public bool IsScrubber;

    public bool IsSensor;
    public bool IsDoor;

    public AirlockVentRole VentRoles;

    public AirlockSide? DoorSide;

    public AirlockSide? SensorTargetFor;
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

    /// <summary>
    ///     Set when a side follows a sensor
    /// </summary>
    public string? TargetSensorNameA;

    public string? TargetSensorNameB;
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
    public string Address;
    public AirlockVentRole Roles;

    public AirlockControllerSetVentRolesMessage(string address, AirlockVentRole roles)
    {
        Address = address;
        Roles = roles;
    }
}

[Serializable, NetSerializable]
public sealed class AirlockControllerSetDoorSideMessage : BoundUserInterfaceMessage
{
    public string Address;

    public AirlockSide? Side;

    public AirlockControllerSetDoorSideMessage(string address, AirlockSide? side)
    {
        Address = address;
        Side = side;
    }
}

[Serializable, NetSerializable]
public sealed class AirlockControllerSetTargetSensorMessage : BoundUserInterfaceMessage
{
    public AirlockSide Side;

    public string? Address;

    public AirlockControllerSetTargetSensorMessage(AirlockSide side, string? address)
    {
        Side = side;
        Address = address;
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
