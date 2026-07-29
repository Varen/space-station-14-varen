using Content.Server.Atmos.AirlockController.Components;
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
            ChamberPressure = TryGetChamberPressure(comp, out var pressure) ? pressure : null,
        };

        _ui.SetUiState(ent.Owner, AirlockControllerUiKey.Key, state);

        if (_ui.IsUiOpen(ent.Owner, AirlockControllerUiKey.Config))
            UpdateConfigUi(ent);
    }

    private void OnCycleMessage(Entity<AirlockControllerComponent> ent, ref AirlockControllerCycleMessage args)
    {
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

        foreach (var (address, uid) in devices)
        {
            var entry = new AirlockDeviceEntry
            {
                Address = address,
                Name = Name(uid),
                IsVent = comp.VentData.ContainsKey(address),
                IsScrubber = comp.ScrubberData.ContainsKey(address),
                IsSensor = comp.SensorData.ContainsKey(address),
                IsDoor = HasComp<Content.Server.Doors.Components.DoorDeviceControlComponent>(uid),
            };

            if (comp.VentRoles.TryGetValue(address, out var roles))
                entry.VentRoles = roles;

            if (comp.DoorRoles.TryGetValue(address, out var side))
                entry.DoorSide = side;

            foreach (var (targetSide, targetAddress) in comp.TargetSensors)
            {
                if (targetAddress == address)
                    entry.SensorTargetFor = targetSide;
            }

            entries.Add(entry);
        }

        entries.Sort((a, b) => string.CompareOrdinal(a.Address, b.Address));

        _ui.SetUiState(ent.Owner, AirlockControllerUiKey.Config, new AirlockControllerConfigUiState
        {
            Devices = entries,
            Address = CompOrNull<DeviceNetworkComponent>(ent)?.Address ?? string.Empty,
            DoorCount = BoundDoors(ent).Count,
            PresetPressureA = comp.PresetPressureA,
            PresetPressureB = comp.PresetPressureB,
            MaintenanceMode = comp.MaintenanceMode,
            TargetSensorNameA = TargetSensorName(comp, devices, AirlockSide.A),
            TargetSensorNameB = TargetSensorName(comp, devices, AirlockSide.B),
        });
    }

    private string? TargetSensorName(
        AirlockControllerComponent comp,
        Dictionary<string, EntityUid> devices,
        AirlockSide side)
    {
        if (!comp.TargetSensors.TryGetValue(side, out var address))
            return null;

        return devices.TryGetValue(address, out var uid)
            ? $"{Name(uid)} ({address})"
            : null;
    }

    private void OnSetVentRoles(Entity<AirlockControllerComponent> ent, ref AirlockControllerSetVentRolesMessage args)
    {
        if (!CheckConfigAccess(ent, args.Actor))
            return;

        if (!_deviceList.ExistsInDeviceList(ent.Owner, args.Address))
            return;

        if (args.Roles == AirlockVentRole.None)
            ent.Comp.VentRoles.Remove(args.Address);
        else
            ent.Comp.VentRoles[args.Address] = args.Roles;

        UpdateConfigUi(ent);
    }
    // Side A, side B
    private void OnSetDoorSide(Entity<AirlockControllerComponent> ent, ref AirlockControllerSetDoorSideMessage args)
    {
        if (!CheckConfigAccess(ent, args.Actor))
            return;

        if (args.Side is not { } side)
        {
            UnassignDoor(ent, args.Address);
            UpdateConfigUi(ent);
            return;
        }

        if (!_deviceList.GetDeviceList(ent.Owner).TryGetValue(args.Address, out var door))
            return;

        TryAssignDoor(ent, door, side, args.Actor);
        UpdateConfigUi(ent);
    }

    private void OnSetTargetSensor(Entity<AirlockControllerComponent> ent, ref AirlockControllerSetTargetSensorMessage args)
    {
        if (!CheckConfigAccess(ent, args.Actor))
            return;

        if (args.Address == null)
            ent.Comp.TargetSensors.Remove(args.Side);
        else if (_deviceList.ExistsInDeviceList(ent.Owner, args.Address))
            ent.Comp.TargetSensors[args.Side] = args.Address;

        UpdateConfigUi(ent);
    }

    private void OnSetPreset(Entity<AirlockControllerComponent> ent, ref AirlockControllerSetPresetMessage args)
    {
        if (!CheckConfigAccess(ent, args.Actor))
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

        ForceSide(ent, args.Side);
        UpdateUi(ent);
    }

    #endregion
}
