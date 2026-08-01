using Content.Server.Atmos.AirlockController.Components;
using Content.Server.Atmos.Monitor.Systems;
using Content.Server.DeviceLinking.Systems;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Power.EntitySystems;
using Content.Shared.Access.Systems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.AirlockController;
using Content.Shared.Atmos.Monitor;
using Content.Shared.Atmos.Monitor.Components;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.DeviceNetwork.Events;
using Content.Shared.DeviceNetwork.Systems;
using Content.Shared.Doors;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server.Atmos.AirlockController.Systems;

/// <summary>
///     Airlock cycle. Five states + cancel and stall flags
///     Vents, sensors and doors are all device-network ents
///     Signals are extras for player shenanigans
/// </summary>
public sealed partial class AirlockControllerSystem : EntitySystem
{
    [Dependency] private AtmosDeviceNetworkSystem _atmosDevNet = default!;
    [Dependency] private DeviceNetworkSystem _deviceNetwork = default!;
    [Dependency] private DeviceLinkSystem _signal = default!;
    [Dependency] private DeviceListSystem _deviceList = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AirlockControllerComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<AirlockControllerComponent, DeviceNetworkPacketEvent>(OnPacketRecv);
        SubscribeLocalEvent<AirlockControllerComponent, DeviceListUpdateEvent>(OnDeviceListUpdate);
        SubscribeLocalEvent<AirlockControllerComponent, SignalReceivedEvent>(OnSignalReceived);

        InitializeUi();
    }

    private void OnInit(Entity<AirlockControllerComponent> ent, ref ComponentInit args)
    {
        var comp = ent.Comp;

        _signal.EnsureSinkPorts(ent, comp.CycleToAPort, comp.CycleToBPort);
        _signal.EnsureSourcePorts(ent, comp.StateAPort, comp.StateBPort, comp.CyclingPort);
    }

    #region Device network

    private void OnDeviceListUpdate(Entity<AirlockControllerComponent> ent, ref DeviceListUpdateEvent args)
    {
        var comp = ent.Comp;

        foreach (var device in args.OldDevices)
        {
            if (args.Devices.Contains(device))
                continue;

            if (!TryComp<DeviceNetworkComponent>(device, out var net))
                continue;

            comp.VentData.Remove(net.Address);
            comp.ScrubberData.Remove(net.Address);
            comp.SensorData.Remove(net.Address);
            comp.DoorReports.Remove(net.Address);
        }

        _atmosDevNet.Register(ent, null);
        _atmosDevNet.Sync(ent, null);
    }

    private void OnPacketRecv(Entity<AirlockControllerComponent> ent, ref DeviceNetworkPacketEvent args)
    {
        var comp = ent.Comp;

        if (!args.Data.TryGetValue(DeviceNetworkConstants.Command, out string? cmd))
            return;

        if (!_deviceList.ExistsInDeviceList(ent, args.SenderAddress))
            return;

        if (cmd == DoorNetworkCommands.Status)
        {
            var report = new AirlockDoorReport();

            if (args.Data.TryGetValue(DoorNetworkCommands.StatusOpen, out bool open))
                report.Open = open;

            if (args.Data.TryGetValue(DoorNetworkCommands.StatusBolted, out bool bolted))
                report.Bolted = bolted;

            if (args.Data.TryGetValue(DoorNetworkCommands.StatusBoltable, out bool boltable))
                report.Boltable = boltable;

            comp.DoorReports[args.SenderAddress] = report;
            return;
        }

        if (cmd != AtmosDeviceNetworkSystem.SyncData
            || !args.Data.TryGetValue(AtmosDeviceNetworkSystem.SyncData, out IAtmosDeviceData? data))
        {
            return;
        }

        switch (data)
        {
            case GasVentPumpData ventData:
                comp.VentData[args.SenderAddress] = ventData;
                break;
            case GasVentScrubberData scrubberData:
                comp.ScrubberData[args.SenderAddress] = scrubberData;
                break;
            case AtmosSensorData sensorData:
                comp.SensorData[args.SenderAddress] = sensorData;
                break;
            default:
                return;
        }
    }

    #endregion

    #region Door assignment

    /// <summary>
    ///     Assign door to side A or B, only if you have access
    /// </summary>
    public bool TryAssignDoor(Entity<AirlockControllerComponent> ent, EntityUid door, AirlockSide side, EntityUid? user)
    {
        if (!InDeviceList(ent, door))
            return false;

        if (user != null && !IsMapping(ent) && !_access.IsAllowed(user.Value, door))
        {
            DenyAccess(ent, user.Value);
            return false;
        }

        ent.Comp.DoorRoles[door] = side;

        if (TryComp<DeviceNetworkComponent>(door, out var net) && !string.IsNullOrEmpty(net.Address))
            SendDoorCommand(ent, net.Address, DoorNetworkCommands.Sync);

        return true;
    }

    public void UnassignDoor(Entity<AirlockControllerComponent> ent, EntityUid door)
    {
        if (!ent.Comp.DoorRoles.Remove(door))
            return;

        if (TryComp<DeviceNetworkComponent>(door, out var net))
            ent.Comp.DoorReports.Remove(net.Address);
    }

    private bool InDeviceList(Entity<AirlockControllerComponent> ent, EntityUid device)
    {
        return _deviceList.GetDeviceList(ent.Owner).ContainsValue(device);
    }

    /// <summary>
    ///     Mapping runs before map init, used to skip to the config menu directly
    /// </summary>
    private bool IsMapping(EntityUid uid)
    {
        return LifeStage(uid) < EntityLifeStage.MapInitialized;
    }

    /// <summary>
    ///     Drops cached entries for devices that are gone.
    /// </summary>
    private void PruneCaches(Entity<AirlockControllerComponent> ent)
    {
        var comp = ent.Comp;
        var devices = _deviceList.GetDeviceList(ent.Owner);

        Prune(comp.VentData, devices);
        Prune(comp.ScrubberData, devices);
        Prune(comp.SensorData, devices);
        Prune(comp.DoorReports, devices);

        PruneDeleted(comp.VentRoles);
        PruneDeletedValues(comp.TargetSensors);
        PruneDeleted(comp.DoorRoles);
    }

    private void PruneDeletedValues<TKey>(Dictionary<TKey, EntityUid> config) where TKey : notnull
    {
        List<TKey>? gone = null;

        foreach (var (key, device) in config)
        {
            if (!TerminatingOrDeleted(device))
                continue;

            gone ??= new List<TKey>();
            gone.Add(key);
        }

        if (gone == null)
            return;

        foreach (var key in gone)
        {
            config.Remove(key);
        }
    }

    private void PruneDeleted<T>(Dictionary<EntityUid, T> config)
    {
        List<EntityUid>? gone = null;

        foreach (var device in config.Keys)
        {
            if (!TerminatingOrDeleted(device))
                continue;

            gone ??= new List<EntityUid>();
            gone.Add(device);
        }

        if (gone == null)
            return;

        foreach (var device in gone)
        {
            config.Remove(device);
        }
    }

    private static void Prune<T>(Dictionary<string, T> cache, Dictionary<string, EntityUid> devices)
    {
        List<string>? stale = null;

        foreach (var address in cache.Keys)
        {
            if (devices.ContainsKey(address))
                continue;

            stale ??= new List<string>();
            stale.Add(address);
        }

        if (stale == null)
            return;

        foreach (var address in stale)
        {
            cache.Remove(address);
        }
    }

    /// <summary>
    ///     Devices that are ok to use right now
    /// </summary>
    private Dictionary<string, EntityUid> LiveDevices(Entity<AirlockControllerComponent> ent)
    {
        var live = new Dictionary<string, EntityUid>();

        foreach (var (address, device) in _deviceList.GetDeviceList(ent.Owner))
        {
            if (_power.IsPowered(device))
                live.Add(address, device);
        }

        return live;
    }

    /// <summary>
    ///     Assigned doors that are still bound to us
    /// </summary>
    private List<(string Address, AirlockSide Side)> BoundDoors(Entity<AirlockControllerComponent> ent)
    {
        var result = new List<(string, AirlockSide)>();

        foreach (var (address, device) in _deviceList.GetDeviceList(ent.Owner))
        {
            if (ent.Comp.DoorRoles.TryGetValue(device, out var side))
                result.Add((address, side));
        }

        return result;
    }

    /// <summary>
    ///     Check if we got both side doors to cycle
    /// </summary>
    private bool CanCycle(Entity<AirlockControllerComponent> ent, out AirlockStallReason missing)
    {
        var hasA = false;
        var hasB = false;

        foreach (var (_, side) in BoundDoors(ent))
        {
            if (side == AirlockSide.A)
                hasA = true;
            else
                hasB = true;
        }

        if (!hasA || !hasB)
        {
            missing = AirlockStallReason.MissingDoors;
            return false;
        }

        missing = default;
        return true;
    }

    private void SendDoorCommand(Entity<AirlockControllerComponent> ent, string address, string command)
    {
        if (!TryComp<DeviceNetworkComponent>(ent, out var net) || net.ReceiveFrequency == null)
            return;

        if (!_deviceList.GetDeviceList(ent).TryGetValue(address, out var door)
            || !TryComp<DeviceNetworkComponent>(door, out var doorNet)
            || doorNet.ReceiveFrequency == null)
        {
            return;
        }

        var payload = new NetworkPayload
        {
            [DeviceNetworkConstants.Command] = command,
            [DoorNetworkCommands.ReplyNetId] = net.DeviceNetId,
            [DoorNetworkCommands.ReplyFrequency] = net.ReceiveFrequency.Value,
        };

        _deviceNetwork.QueuePacket(ent, address, payload, doorNet.ReceiveFrequency.Value, doorNet.DeviceNetId);
    }

    #endregion

    #region Signals

    private void OnSignalReceived(Entity<AirlockControllerComponent> ent, ref SignalReceivedEvent args)
    {
        var comp = ent.Comp;

        if (args.Port == comp.CycleToAPort)
            TryRequestCycle(ent, AirlockSide.A);
        else if (args.Port == comp.CycleToBPort)
            TryRequestCycle(ent, AirlockSide.B);
    }

    private bool TryRequestCycle(Entity<AirlockControllerComponent> ent, AirlockSide side, EntityUid? user = null)
    {
        var comp = ent.Comp;

        if (comp.MaintenanceMode)
            return false;

        if (comp.State != AirlockCycleState.Idle)
            return false;

        if (comp.CurrentSide == side)
            return false;

        // Check access on the side
        if (user != null && !CanUseSide(ent, side, user.Value))
        {
            DenyAccess(ent, user.Value);
            return false;
        }

        // Error out if doors are missing
        if (!CanCycle(ent, out var missing))
        {
            comp.StallReason = missing;
            UpdateUi(ent);
            return false;
        }

        comp.TargetSide = side;
        comp.CancelRequested = false;
        comp.StallReason = null;

        // Sealing shuts and bolts everything already
        comp.RestoreDoors = false;

        SetState(ent, AirlockCycleState.Sealing);
        return true;
    }

    private void DenyAccess(EntityUid uid, EntityUid user)
    {
        _popup.PopupEntity(Loc.GetString("airlock-controller-access-denied"), uid, user);
        _audio.PlayPvs(DenySound, uid);
    }

    private static readonly SoundSpecifier DenySound = new SoundPathSpecifier("/Audio/Machines/custom_deny.ogg");

    private bool CanUseSide(Entity<AirlockControllerComponent> ent, AirlockSide side, EntityUid user)
    {
        var devices = _deviceList.GetDeviceList(ent);

        foreach (var (address, doorSide) in BoundDoors(ent))
        {
            if (doorSide != side || !devices.TryGetValue(address, out var door))
                continue;

            if (!_access.IsAllowed(user, door))
                return false;
        }

        return true;
    }

    /// <summary>
    ///     First press goes to the side the chamber already matches.
    ///     Second press unbolts immediately.
    /// </summary>
    private void RequestCancel(Entity<AirlockControllerComponent> ent)
    {
        var comp = ent.Comp;

        if (comp.MaintenanceMode)
            return;

        // Unsealing has already released target side at this point
        if (comp.State is AirlockCycleState.Idle or AirlockCycleState.Unsealing)
            return;

        // Whichever mix the chamber holds, aka which side we can go to safely without mixing atmoses
        var safe = comp.State == AirlockCycleState.Filling
            ? comp.TargetSide
            : comp.CurrentSide;

        if (comp.CancelRequested)
        {
            comp.TargetSide = safe;
            SetState(ent, AirlockCycleState.Unsealing);
            return;
        }

        comp.CancelRequested = true;
        comp.TargetSide = safe;

        // Straight unseal if pumps aren't started yet
        if (comp.State == AirlockCycleState.Sealing)
            SetState(ent, AirlockCycleState.Unsealing);
    }

    #endregion

    #region State machine

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<AirlockControllerComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (now < comp.NextUpdate)
                continue;

            comp.NextUpdate = now + comp.UpdateInterval;

            PruneCaches((uid, comp));

            var cycling = !comp.MaintenanceMode && comp.State != AirlockCycleState.Idle;
            var uiOpen = _ui.IsUiOpen(uid, AirlockControllerUiKey.Key)
                         || _ui.IsUiOpen(uid, AirlockControllerUiKey.Config);

            if (cycling || uiOpen)
                SyncAtmosDevices((uid, comp));

            if (uiOpen)
                UpdateUi((uid, comp));

            var restoring = !comp.MaintenanceMode && comp.RestoreDoors;

            if (cycling || restoring)
                PollDoors((uid, comp));

            if (restoring)
                UpdateDoorRestore((uid, comp));

            if (!cycling)
                continue;

            UpdateCycle((uid, comp), now);
        }
    }

    private void SyncAtmosDevices(Entity<AirlockControllerComponent> ent)
    {
        foreach (var address in _deviceList.GetDeviceList(ent.Owner).Keys)
        {
            _atmosDevNet.Sync(ent, address);
        }
    }

    /// <summary>
    ///     Doors won't sent us updates so just ask.
    /// </summary>
    private void PollDoors(Entity<AirlockControllerComponent> ent)
    {
        foreach (var (address, _) in BoundDoors(ent))
        {
            SendDoorCommand(ent, address, DoorNetworkCommands.Sync);
        }
    }

    /// <summary>
    ///     Goes back to Idle state: This side unbolted, other side closed and bolted.
    /// </summary>
    private void UpdateDoorRestore(Entity<AirlockControllerComponent> ent)
    {
        var comp = ent.Comp;
        var far = comp.CurrentSide == AirlockSide.A ? AirlockSide.B : AirlockSide.A;

        var farDoors = 0;
        var farClosed = 0;
        var farBolted = 0;
        var nearDoors = 0;
        var nearReleased = 0;

        foreach (var (address, side) in BoundDoors(ent))
        {
            var known = comp.DoorReports.TryGetValue(address, out var report);

            if (side == far)
            {
                farDoors++;
                if (!known)
                    continue;

                if (!report.Open)
                    farClosed++;

                if (report.Bolted || !report.Boltable)
                    farBolted++;
            }
            else
            {
                nearDoors++;
                if (known && (!report.Bolted || !report.Boltable))
                    nearReleased++;
            }
        }

        if (nearReleased < nearDoors)
        {
            if (comp.BoltingEnabled)
                CommandSide(ent, comp.CurrentSide, DoorNetworkCommands.Unbolt);

            return;
        }

        if (farClosed < farDoors)
        {
            CommandSide(ent, far, DoorNetworkCommands.Close);
            return;
        }

        if (comp.BoltingEnabled && farBolted < farDoors)
        {
            CommandSide(ent, far, DoorNetworkCommands.Bolt);
            return;
        }

        comp.RestoreDoors = false;
        UpdateUi(ent);
    }

    private void UpdateCycle(Entity<AirlockControllerComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;

        if (!CanCycle(ent, out var missing))
        {
            comp.StallReason = missing;
            return;
        }

        // Re-checked doors for the whole cycle, error if broken
        if (comp.State is AirlockCycleState.Evacuating or AirlockCycleState.Filling
            && !IsSealed(ent))
        {
            comp.StallReason = AirlockStallReason.SealLost;
            return;
        }

        switch (comp.State)
        {
            case AirlockCycleState.Sealing:
                UpdateSealing(ent, now);
                break;
            case AirlockCycleState.Evacuating:
                UpdateEvacuating(ent, now);
                break;
            case AirlockCycleState.Filling:
                UpdateFilling(ent, now);
                break;
            case AirlockCycleState.Unsealing:
                UpdateUnsealing(ent, now);
                break;
        }
    }

    /// <summary>
    ///     Shuts every door, then bolts. Doors bolted open can't close, unbolt first.
    /// </summary>
    private void UpdateSealing(Entity<AirlockControllerComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        var doors = BoundDoors(ent);

        if (doors.Count == 0)
            return;

        // Doors that are where we want them, stall only when nothing moves
        var progress = 0;
        var allSealed = true;

        foreach (var (address, _) in doors)
        {
            // No report yet, PollDoors will ask again
            if (!comp.DoorReports.TryGetValue(address, out var report))
            {
                allSealed = false;
                continue;
            }

            if (report.Open)
            {
                SendDoorCommand(ent, address, report.Bolted
                    ? DoorNetworkCommands.Unbolt
                    : DoorNetworkCommands.Close);

                allSealed = false;
                continue;
            }

            progress++;

            // Shut. Bolt unless it has no bolts or the wire is cut
            if (!comp.BoltingEnabled || !report.Boltable || report.Bolted)
            {
                progress++;
                continue;
            }

            SendDoorCommand(ent, address, DoorNetworkCommands.Bolt);
            allSealed = false;
        }

        if (!allSealed)
        {
            CheckProgress(ent, progress, now, AirlockStallReason.NotSealing);
            return;
        }

        SetState(ent, AirlockCycleState.Evacuating);
    }

    private void UpdateEvacuating(Entity<AirlockControllerComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;

        // Re-sent every tick or vents can get status stuck
        ApplyVents(ent, siphon: true);

        if (!TryGetChamberPressure(ent, out var average))
        {
            comp.StallReason = AirlockStallReason.NoSensors;
            return;
        }

        // Wait for atmos to catch up
        if (average <= comp.EvacuatedPressure)
        {
            if (++comp.StableTicks >= comp.RequiredStableTicks)
                SetState(ent, AirlockCycleState.Filling);

            return;
        }

        comp.StableTicks = 0;
        CheckProgress(ent, average, now, AirlockStallReason.NotProgressing);
    }

    private void UpdateFilling(Entity<AirlockControllerComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;

        ApplyVents(ent, siphon: false);

        if (!TryGetChamberPressure(ent, out var average))
        {
            comp.StallReason = AirlockStallReason.NoSensors;
            return;
        }

        var target = GetTargetPressure(ent, comp.TargetSide);

        if (MathF.Abs(average - target) <= comp.PressureTolerance)
        {
            if (++comp.StableTicks >= comp.RequiredStableTicks)
                SetState(ent, AirlockCycleState.Unsealing);

            return;
        }

        comp.StableTicks = 0;
        CheckProgress(ent, average, now, AirlockStallReason.NotProgressing);
    }

    /// <summary>
    ///     Unbolts the target side then opens it.
    /// </summary>
    private void UpdateUnsealing(Entity<AirlockControllerComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        var progress = 0;
        var total = 0;

        foreach (var (address, side) in BoundDoors(ent))
        {
            if (side != comp.TargetSide)
                continue;

            total++;

            if (!comp.DoorReports.TryGetValue(address, out var report))
                continue;

            // A bolted door ignores Open
            if (report.Boltable && report.Bolted)
            {
                if (comp.BoltingEnabled)
                    SendDoorCommand(ent, address, DoorNetworkCommands.Unbolt);

                continue;
            }

            progress++;

            if (!report.Open)
            {
                SendDoorCommand(ent, address, DoorNetworkCommands.Open);
                continue;
            }

            progress++;
        }

        if (total == 0 || progress < total * 2)
        {
            CheckProgress(ent, progress, now, AirlockStallReason.NotOpening);
            return;
        }

        // Update to new side state
        comp.CurrentSide = comp.TargetSide;
        comp.CancelRequested = false;
        comp.StallReason = null;
        SetState(ent, AirlockCycleState.Idle);
    }

    /// <summary>
    ///     Checks for stalling during progress, e.g. spacing, broken pipes..
    /// </summary>
    private void CheckProgress(Entity<AirlockControllerComponent> ent, float value, TimeSpan now, AirlockStallReason reason)
    {
        var comp = ent.Comp;

        if (float.IsNaN(comp.LastProgressValue)
            || MathF.Abs(value - comp.LastProgressValue) > comp.PressureTolerance * 0.1f)
        {
            comp.LastProgressValue = value;
            comp.LastProgressTime = now;
            comp.StallReason = null;
            return;
        }

        if (now - comp.LastProgressTime >= comp.StallTimeout)
            comp.StallReason = reason;
    }

    private void SetState(Entity<AirlockControllerComponent> ent, AirlockCycleState state)
    {
        var comp = ent.Comp;

        comp.State = state;
        comp.LastProgressValue = float.NaN;
        comp.LastProgressTime = _timing.CurTime;
        comp.StableTicks = 0;

        switch (state)
        {
            case AirlockCycleState.Sealing:
                // Need fresh reports for doors
                comp.DoorReports.Clear();
                CommandAllDoors(ent, DoorNetworkCommands.Close);
                StopVents(ent);
                break;

            case AirlockCycleState.Evacuating:
                ApplyVents(ent, siphon: true);
                break;

            case AirlockCycleState.Filling:
                ApplyVents(ent, siphon: false);
                break;

            case AirlockCycleState.Unsealing:
                StopVents(ent);
                // Don't unbolt if cut wire
                if (comp.BoltingEnabled)
                    CommandSide(ent, comp.TargetSide, DoorNetworkCommands.Unbolt);
                break;

            case AirlockCycleState.Idle:
                StopVents(ent);
                break;
        }

        UpdateOutputs(ent);
        UpdateUi(ent);
    }

    #endregion

    #region Doors

    /// <summary>
    ///     True when every assigned door reports shut, and bolted where it can be.
    /// </summary>
    private bool IsSealed(Entity<AirlockControllerComponent> ent)
    {
        var comp = ent.Comp;

        var doors = BoundDoors(ent);

        if (doors.Count == 0)
            return false;

        foreach (var (address, _) in doors)
        {
            if (!comp.DoorReports.TryGetValue(address, out var report) || report.Open)
                return false;

            // Boltless doors can't be waited on for bolting
            if (comp.BoltingEnabled && report.Boltable && !report.Bolted)
                return false;
        }

        return true;
    }


    private void CommandAllDoors(Entity<AirlockControllerComponent> ent, string command)
    {
        foreach (var (address, _) in BoundDoors(ent))
        {
            SendDoorCommand(ent, address, command);
        }
    }

    private void CommandSide(Entity<AirlockControllerComponent> ent, AirlockSide side, string command)
    {
        foreach (var (address, doorSide) in BoundDoors(ent))
        {
            if (doorSide == side)
                SendDoorCommand(ent, address, command);
        }
    }

    #endregion

    #region Atmos devices

    /// <summary>
    ///     Averages out all chamber sensors
    /// </summary>
    private bool TryGetChamberPressure(Entity<AirlockControllerComponent> ent, out float average)
    {
        var comp = ent.Comp;

        average = 0f;
        var count = 0;

        foreach (var (address, device) in LiveDevices(ent))
        {
            // Exclude outside sensors
            if (comp.TargetSensors.ContainsValue(device))
                continue;

            if (!comp.SensorData.TryGetValue(address, out var sensor))
                continue;

            average += sensor.Pressure;
            count++;
        }

        if (count == 0)
            return false;

        average /= count;
        return true;
    }

    private float GetTargetPressure(Entity<AirlockControllerComponent> ent, AirlockSide side)
    {
        var comp = ent.Comp;

        // Preset or target sensor mode? Use preset if sensor died
        if (comp.TargetSensors.TryGetValue(side, out var sensor)
            && _power.IsPowered(sensor)
            && TryComp<DeviceNetworkComponent>(sensor, out var net)
            && comp.SensorData.TryGetValue(net.Address, out var data))
        {
            return data.Pressure;
        }

        return side == AirlockSide.A ? comp.PresetPressureA : comp.PresetPressureB;
    }

    /// <summary>
    ///     Check which vents we can use for our current mode and uses them accordingly
    /// </summary>
    private void ApplyVents(Entity<AirlockControllerComponent> ent, bool siphon)
    {
        var comp = ent.Comp;

        // We want the other side air: Evacuating siphons towards current side, filling comes from target side
        var side = siphon ? comp.CurrentSide : comp.TargetSide;

        var wanted = (siphon, side) switch
        {
            (true, AirlockSide.A) => AirlockVentRole.SiphonA,
            (true, AirlockSide.B) => AirlockVentRole.SiphonB,
            (false, AirlockSide.A) => AirlockVentRole.VentA,
            _ => AirlockVentRole.VentB,
        };

        var target = siphon ? 0f : GetTargetPressure(ent, side);
        var used = false;

        // Everything registered gets a command, so an unassigned vent can't keep running on
        // whatever an air alarm last told it
        foreach (var (address, device) in LiveDevices(ent))
        {
            comp.VentRoles.TryGetValue(device, out var roles);

            var wants = (roles & wanted) != 0;

            if (comp.VentData.ContainsKey(address))
            {
                SetVent(ent, address, wants ? (siphon ? Siphoning() : Releasing(target)) : DisabledVent());
                used |= wants;
            }
            else if (comp.ScrubberData.ContainsKey(address))
            {
                // Scrubbers can only siphon
                var useScrubber = wants && siphon;
                SetScrubber(ent, address, useScrubber ? SiphoningScrubber() : DisabledScrubber());
                used |= useScrubber;
            }
        }

        comp.StallReason = used ? null : AirlockStallReason.NoUsableVent;
    }

    private void StopVents(Entity<AirlockControllerComponent> ent)
    {
        foreach (var (address, _) in _deviceList.GetDeviceList(ent.Owner))
        {
            if (ent.Comp.VentData.ContainsKey(address))
                SetVent(ent, address, DisabledVent());
            else if (ent.Comp.ScrubberData.ContainsKey(address))
                SetScrubber(ent, address, DisabledScrubber());
        }
    }

    private void SetScrubber(Entity<AirlockControllerComponent> ent, string address, GasVentScrubberData data)
    {
        ent.Comp.ScrubberData[address] = data;
        _atmosDevNet.SetDeviceState(ent, address, data);
        _atmosDevNet.Sync(ent, address);
    }

    private void SetVent(Entity<AirlockControllerComponent> ent, string address, GasVentPumpData data)
    {
        ent.Comp.VentData[address] = data;
        _atmosDevNet.SetDeviceState(ent, address, data);
        _atmosDevNet.Sync(ent, address);
    }

    // PressureLockoutOverride needed otherwise stuff won't work in vacuum
    private static GasVentPumpData Siphoning() => new()
    {
        Enabled = true,
        Dirty = true,
        IgnoreAlarms = true,
        PumpDirection = VentPumpDirection.Siphoning,
        PressureChecks = VentPressureBound.NoBound,
        PressureLockoutOverride = true,
    };

    private static GasVentPumpData Releasing(float target) => new()
    {
        Enabled = true,
        Dirty = true,
        IgnoreAlarms = true,
        PumpDirection = VentPumpDirection.Releasing,
        PressureChecks = VentPressureBound.ExternalBound,
        ExternalPressureBound = target,
        PressureLockoutOverride = true,
    };

    private static GasVentPumpData DisabledVent() => new()
    {
        Enabled = false,
        Dirty = true,
        IgnoreAlarms = true,
        PumpDirection = VentPumpDirection.Siphoning,
        PressureChecks = VentPressureBound.NoBound,
        PressureLockoutOverride = true,
    };

    /// <summary>
    ///     Siphons everything, wide net cause airlocks are boring to wait in
    /// </summary>
    private static GasVentScrubberData SiphoningScrubber() => new()
    {
        Enabled = true,
        Dirty = true,
        IgnoreAlarms = true,
        PumpDirection = ScrubberPumpDirection.Siphoning,
        VolumeRate = 200f,
        WideNet = true,
    };

    private static GasVentScrubberData DisabledScrubber() => new()
    {
        Enabled = false,
        Dirty = true,
        IgnoreAlarms = true,
        PumpDirection = ScrubberPumpDirection.Siphoning,
    };

    #endregion

    #region Outputs

    private void UpdateOutputs(Entity<AirlockControllerComponent> ent)
    {
        var comp = ent.Comp;
        var idle = comp.State == AirlockCycleState.Idle;

        // Flashy light signal wire can be cut
        _signal.SendSignal(ent, comp.CyclingPort, !idle && comp.EmergencyLightsEnabled);
        _signal.SendSignal(ent, comp.StateAPort, idle && comp.CurrentSide == AirlockSide.A);
        _signal.SendSignal(ent, comp.StateBPort, idle && comp.CurrentSide == AirlockSide.B);
    }

    #endregion

    #region Maintenance

    /// <summary>
    ///     Force side for atmos techs who know what they're doing
    /// </summary>
    public void ForceSide(Entity<AirlockControllerComponent> ent, AirlockSide side)
    {
        var comp = ent.Comp;

        comp.CurrentSide = side;
        comp.TargetSide = side;
        comp.CancelRequested = false;
        comp.StallReason = null;
        SetState(ent, AirlockCycleState.Idle);

        if (!comp.MaintenanceMode)
            StartDoorRestore(ent);
    }

    /// <summary>
    ///     Drop door reports to get fresh ones to work with
    /// </summary>
    private void StartDoorRestore(Entity<AirlockControllerComponent> ent)
    {
        ent.Comp.DoorReports.Clear();
        ent.Comp.RestoreDoors = true;
    }

    /// <summary>
    ///     Cutting/pulsing the bolting wire
    /// </summary>
    public void SetBoltingEnabled(Entity<AirlockControllerComponent> ent, bool enabled)
    {
        ent.Comp.BoltingEnabled = enabled;
    }

    public void SetEmergencyLightsEnabled(Entity<AirlockControllerComponent> ent, bool enabled)
    {
        ent.Comp.EmergencyLightsEnabled = enabled;
        UpdateOutputs(ent);
    }

    public void SetMaintenanceMode(Entity<AirlockControllerComponent> ent, bool enabled)
    {
        var comp = ent.Comp;
        comp.MaintenanceMode = enabled;

        if (enabled)
        {
            StopVents(ent);
            comp.CancelRequested = false;
            comp.StallReason = null;
            comp.RestoreDoors = false;
            SetState(ent, AirlockCycleState.Idle);
            CommandAllDoors(ent, DoorNetworkCommands.Unbolt);
            return;
        }

        // Will restore all doors to the current side
        StartDoorRestore(ent);
    }

    #endregion
}
