using Content.Server.Doors.Components;
using Content.Server.DeviceNetwork.Systems;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Events;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;

namespace Content.Server.Doors.Systems;

/// <summary>
///     Serves the DoorNetworkCommands protocol so an airlock controller can
///     use a door over the device network and ask what it's doing.
///     Stateless, the reply goes to whoever asked
/// </summary>
public sealed class DoorDeviceControlSystem : EntitySystem
{
    [Dependency] private readonly DeviceNetworkSystem _deviceNetwork = default!;
    [Dependency] private readonly DoorSystem _doors = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DoorDeviceControlComponent, DeviceNetworkPacketEvent>(OnPacketReceived);
    }

    private void OnPacketReceived(Entity<DoorDeviceControlComponent> ent, ref DeviceNetworkPacketEvent args)
    {
        // Device-link traffic doesn't have commands so we ignore those
        if (!args.Data.TryGetValue(DeviceNetworkConstants.Command, out string? command))
            return;

        switch (command)
        {
            case DoorNetworkCommands.Sync:
                Reply(ent, args);
                break;

            case DoorNetworkCommands.Open:
                if (TryComp<DoorComponent>(ent, out var toOpen) && toOpen.State == DoorState.Closed)
                    _doors.TryOpen(ent, toOpen);
                break;

            case DoorNetworkCommands.Close:
                if (TryComp<DoorComponent>(ent, out var toClose) && toClose.State == DoorState.Open)
                    _doors.TryClose(ent, toClose);
                break;

            case DoorNetworkCommands.Bolt:
                if (TryComp<DoorBoltComponent>(ent, out var toBolt))
                    _doors.SetBoltsDown((ent, toBolt), true);
                break;

            case DoorNetworkCommands.Unbolt:
                if (TryComp<DoorBoltComponent>(ent, out var toUnbolt))
                    _doors.SetBoltsDown((ent, toUnbolt), false);
                break;
        }
    }


    private void Reply(EntityUid uid, DeviceNetworkPacketEvent args)
    {
        if (!args.Data.TryGetValue(DoorNetworkCommands.ReplyNetId, out int netId)
            || !args.Data.TryGetValue(DoorNetworkCommands.ReplyFrequency, out uint frequency))
        {
            return;
        }

        var boltable = TryComp<DoorBoltComponent>(uid, out var bolts);

        var payload = new NetworkPayload
        {
            [DeviceNetworkConstants.Command] = DoorNetworkCommands.Status,
            [DoorNetworkCommands.StatusOpen] = !TryComp<DoorComponent>(uid, out var door) || door.State != DoorState.Closed,
            [DoorNetworkCommands.StatusBolted] = boltable && bolts!.BoltsDown,
            [DoorNetworkCommands.StatusBoltable] = boltable,
        };

        _deviceNetwork.QueuePacket(uid, args.SenderAddress, payload, frequency, netId);
    }

}
