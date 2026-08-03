using Content.Server.Atmos.AirlockController.Components;
using Content.Server.Atmos.Monitor.Components;
using Content.Server.Atmos.Piping.Unary.Components;
using Content.Server.Doors.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.AirlockController;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Doors;
using Content.Shared.Interaction;
using Content.Shared.Wires;

namespace Content.Server.Atmos.AirlockController.Systems;

/// <summary>
///     Two windows: Main controller and config window.
///     Separate so we only send data if config itself is open.
/// </summary>
public sealed partial class AirlockControllerSystem
{
    private void InitializeUi()
    {
        SubscribeLocalEvent<AirlockControllerComponent, ActivateInWorldEvent>(OnActivate);

        Subs.BuiEvents<AirlockControllerComponent>(AirlockControllerUiKey.Key,
            subs =>
        {
            subs.Event<AirlockControllerCycleMessage>(OnCycleMessage);
            subs.Event<AirlockControllerCancelMessage>(OnCancelMessage);
            subs.Event<AirlockControllerOpenConfigMessage>(OnOpenConfigMessage);
        });

        Subs.BuiEvents<AirlockControllerComponent>(AirlockControllerUiKey.Config,
            subs =>
        {
            subs.Event<AirlockControllerSetVentRolesMessage>(OnSetVentRoles);
            subs.Event<AirlockControllerSetDoorSideMessage>(OnSetDoorSide);
            subs.Event<AirlockControllerSetTargetSensorMessage>(OnSetTargetSensor);
            subs.Event<AirlockControllerSetPresetMessage>(OnSetPreset);
            subs.Event<AirlockControllerSetMaintenanceMessage>(OnSetMaintenance);
            subs.Event<AirlockControllerForceSideMessage>(OnForceSide);
        });
    }

    private void OnActivate(Entity<AirlockControllerComponent> ent, ref ActivateInWorldEvent args)
    {
        if (!args.Complex || args.Handled)
            return;

        // Mappers get the config, regular UI is useless to them
        if (IsMapping(ent))
        {
            _ui.OpenUi(ent.Owner, AirlockControllerUiKey.Config, args.User);
            UpdateConfigUi(ent);
            args.Handled = true;
            return;
        }

        if (!this.IsPowered(ent, EntityManager))
            return;

        _ui.OpenUi(ent.Owner, AirlockControllerUiKey.Key, args.User);
        UpdateUi(ent);
        args.Handled = true;
    }

    #region Status window

    public void UpdateUi(Entity<AirlockControllerComponent> ent)
    {
        var comp = ent.Comp;

        var state = new AirlockControllerUiState
        {
            State = comp.State,
            CurrentSide = comp.CurrentSide,
            TargetSide = comp.TargetSide,
            StallReason = comp.StallReason,
            CancelRequested = comp.CancelRequested,
            MaintenanceMode = comp.MaintenanceMode,
            ChamberPressure = TryGetChamberPressure(ent, out var pressure) ? pressure : null,
        };

        _ui.SetUiState(ent.Owner, AirlockControllerUiKey.Key, state);

        if (_ui.IsUiOpen(ent.Owner, AirlockControllerUiKey.Config))
            UpdateConfigUi(ent);
    }

    private void OnCycleMessage(Entity<AirlockControllerComponent> ent, ref AirlockControllerCycleMessage args)
    {
        if (!IsValidSide(args.Side))
            return;

        TryRequestCycle(ent, args.Side, args.Actor);
        UpdateUi(ent);
    }

    private void OnCancelMessage(Entity<AirlockControllerComponent> ent, ref AirlockControllerCancelMessage args)
    {
        RequestCancel(ent);
        UpdateUi(ent);
    }

    private void OnOpenConfigMessage(Entity<AirlockControllerComponent> ent, ref AirlockControllerOpenConfigMessage args)
    {
        if (!CheckConfigAccess(ent, args.Actor))
            return;

        _ui.OpenUi(ent.Owner, AirlockControllerUiKey.Config, args.Actor);
        UpdateConfigUi(ent);
    }

    #endregion

    #region Config window

    private bool CheckConfigAccess(Entity<AirlockControllerComponent> ent, EntityUid user)
    {
        if (IsMapping(ent))
            return true;

        if (!_access.IsAllowed(user, ent))
        {
            DenyAccess(ent, user);
            return false;
        }

        return true;
    }

    private void UpdateConfigUi(Entity<AirlockControllerComponent> ent)
    {
        var comp = ent.Comp;
        var devices = _deviceList.GetDeviceList(ent.Owner);
        var entries = new List<AirlockDeviceEntry>();
        var sensors = new List<AirlockSensorOption>();

        foreach (var (address, uid) in devices)
        {
            var entry = new AirlockDeviceEntry
            {
                Device = GetNetEntity(uid),
                Address = address,
                Name = Name(uid),
                // Components because if we go for addresses saving and such breaks, thanks NetworkDevicebama
                IsVent = HasComp<GasVentPumpComponent>(uid),
                IsScrubber = HasComp<GasVentScrubberComponent>(uid),
                IsSensor = HasComp<AtmosMonitorComponent>(uid),
                IsDoor = HasComp<DoorDeviceControlComponent>(uid),
            };

            if (comp.VentRoles.TryGetValue(uid, out var roles))
                entry.VentRoles = roles;

            if (comp.DoorRoles.TryGetValue(uid, out var side))
                entry.DoorSide = side;

            // Vents inherently always inside
            if (entry.IsSensor && !entry.IsVent && !entry.IsScrubber)
            {
                sensors.Add(new AirlockSensorOption
                {
                    Device = entry.Device,
                    Name = $"{entry.Name} ({address})",
                });
            }

            entries.Add(entry);
        }

        sensors.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        entries.Sort((a, b) =>
        {
            var group = DeviceGroup(a).CompareTo(DeviceGroup(b));
            return group != 0 ? group : string.CompareOrdinal(a.Name, b.Name);
        });

        _ui.SetUiState(ent.Owner, AirlockControllerUiKey.Config, new AirlockControllerConfigUiState
        {
            Devices = entries,
            Address = CompOrNull<DeviceNetworkComponent>(ent)?.Address ?? string.Empty,
            DoorCount = BoundDoors(ent).Count,
            PresetPressureA = comp.PresetPressureA,
            PresetPressureB = comp.PresetPressureB,
            MaintenanceMode = comp.MaintenanceMode,
            CurrentSide = comp.CurrentSide,
            Sensors = sensors,
            TargetSensorA = BoundSensor(comp, devices, AirlockSide.A),
            TargetSensorB = BoundSensor(comp, devices, AirlockSide.B),
            TargetPressureA = SensorReading(comp, devices, AirlockSide.A),
            TargetPressureB = SensorReading(comp, devices, AirlockSide.B),
        });
    }

    /// <summary>
    ///     For UI sorting devices in neat groups
    /// </summary>
    private static int DeviceGroup(AirlockDeviceEntry device)
    {
        if (device.IsDoor)
            return 0;

        if (device.IsVent)
            return 1;

        if (device.IsScrubber)
            return 2;

        return device.IsSensor ? 3 : 4;
    }

    /// <summary>
    ///     Null puts the side back on its preset
    /// </summary>
    private NetEntity? BoundSensor(
        AirlockControllerComponent comp,
        Dictionary<string, EntityUid> devices,
        AirlockSide side)
    {
        if (!comp.TargetSensors.TryGetValue(side, out var sensor) || !devices.ContainsValue(sensor))
            return null;

        return GetNetEntity(sensor);
    }

    private float? SensorReading(
        AirlockControllerComponent comp,
        Dictionary<string, EntityUid> devices,
        AirlockSide side)
    {
        if (!comp.TargetSensors.TryGetValue(side, out var sensor))
            return null;

        foreach (var (address, uid) in devices)
        {
            if (uid == sensor && comp.SensorData.TryGetValue(address, out var data))
                return data.Pressure;
        }

        return null;
    }

    /// <summary>
    ///     Validate user input!!
    /// </summary>
    private static bool IsValidSide(AirlockSide side)
    {
        return side is AirlockSide.A or AirlockSide.B;
    }

    private const AirlockVentRole AllVentRoles =
        AirlockVentRole.VentA | AirlockVentRole.SiphonA | AirlockVentRole.VentB | AirlockVentRole.SiphonB;

    private void OnSetVentRoles(Entity<AirlockControllerComponent> ent, ref AirlockControllerSetVentRolesMessage args)
    {
        if (!CheckConfigAccess(ent, args.Actor))
            return;

        if (!TryGetEntity(args.Device, out var vent) || !InDeviceList(ent, vent.Value))
            return;

        if (!HasComp<GasVentPumpComponent>(vent) && !HasComp<GasVentScrubberComponent>(vent))
            return;

        var roles = args.Roles & AllVentRoles;

        if (roles == AirlockVentRole.None)
            ent.Comp.VentRoles.Remove(vent.Value);
        else
            ent.Comp.VentRoles[vent.Value] = roles;

        UpdateConfigUi(ent);
    }
    // Side A, side B
    private void OnSetDoorSide(Entity<AirlockControllerComponent> ent, ref AirlockControllerSetDoorSideMessage args)
    {
        if (!CheckConfigAccess(ent, args.Actor))
            return;

        if (!TryGetEntity(args.Device, out var door))
            return;

        if (args.Side is not { } side)
        {
            UnassignDoor(ent, door.Value);
            UpdateConfigUi(ent);
            return;
        }

        // Only consider actual doors
        if (!IsValidSide(side) || !HasComp<DoorDeviceControlComponent>(door))
            return;

        TryAssignDoor(ent, door.Value, side, args.Actor);
        UpdateConfigUi(ent);
    }

    private void OnSetTargetSensor(Entity<AirlockControllerComponent> ent, ref AirlockControllerSetTargetSensorMessage args)
    {
        if (!CheckConfigAccess(ent, args.Actor))
            return;

        if (!IsValidSide(args.Side))
            return;

        var comp = ent.Comp;

        if (args.Device is not { } netSensor)
        {
            comp.TargetSensors.Remove(args.Side);
        }
        else if (TryGetEntity(netSensor, out var sensor)
                 && InDeviceList(ent, sensor.Value)
                 && HasComp<AtmosMonitorComponent>(sensor.Value))
        {
            var other = args.Side == AirlockSide.A ? AirlockSide.B : AirlockSide.A;

            // Swap sensors if picking the one on the other side
            if (comp.TargetSensors.TryGetValue(other, out var taken) && taken == sensor.Value)
            {
                if (comp.TargetSensors.TryGetValue(args.Side, out var ours))
                    comp.TargetSensors[other] = ours;
                else
                    comp.TargetSensors.Remove(other);
            }

            comp.TargetSensors[args.Side] = sensor.Value;
        }

        UpdateConfigUi(ent);
    }

    private void OnSetPreset(Entity<AirlockControllerComponent> ent, ref AirlockControllerSetPresetMessage args)
    {
        if (!CheckConfigAccess(ent, args.Actor))
            return;

        // Make sure the pressure makes sense, NaN check
        if (!IsValidSide(args.Side) || !float.IsFinite(args.Pressure))
            return;

        var pressure = Math.Clamp(args.Pressure, 0f, Atmospherics.MaxOutputPressure);

        if (args.Side == AirlockSide.A)
            ent.Comp.PresetPressureA = pressure;
        else
            ent.Comp.PresetPressureB = pressure;

        UpdateConfigUi(ent);
    }

    private void OnSetMaintenance(Entity<AirlockControllerComponent> ent, ref AirlockControllerSetMaintenanceMessage args)
    {
        if (!CheckConfigAccess(ent, args.Actor))
            return;

        SetMaintenanceMode(ent, args.Enabled);
        UpdateUi(ent);
    }

    private void OnForceSide(Entity<AirlockControllerComponent> ent, ref AirlockControllerForceSideMessage args)
    {
        if (!CheckConfigAccess(ent, args.Actor))
            return;

        if (!IsValidSide(args.Side))
            return;

        ForceSide(ent, args.Side);
        UpdateUi(ent);
    }

    #endregion
}
