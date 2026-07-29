using Content.Shared.Access.Systems;
using Content.Shared.Atmos.AirlockController;
using Robust.Client.Player;
using Robust.Client.UserInterface;

namespace Content.Client.Atmos.AirlockController.UI;

public sealed partial class AirlockControllerBoundUserInterface : BoundUserInterface
{
    [Dependency] private IPlayerManager _player = default!;

    private readonly AccessReaderSystem _access;

    private AirlockControllerWindow? _window;

    public AirlockControllerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _access = EntMan.System<AccessReaderSystem>();
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<AirlockControllerWindow>();
        _window.SetEntity(Owner);

        _window.CycleRequested += side => SendMessage(new AirlockControllerCycleMessage(side));
        _window.CancelRequested += () => SendMessage(new AirlockControllerCancelMessage());

        // Access locked config (Atmos)
        _window.ConfigRequested += () => SendMessage(new AirlockControllerOpenConfigMessage());
        if (_player.LocalEntity is { } player)
            _window.SetConfigAllowed(_access.IsAllowed(player, Owner));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is AirlockControllerUiState cast)
            _window?.UpdateState(cast);
    }
}

public sealed class AirlockControllerConfigBoundUserInterface : BoundUserInterface
{
    private AirlockControllerConfigWindow? _window;

    public AirlockControllerConfigBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<AirlockControllerConfigWindow>();

        _window.VentRolesChanged += (addr, roles) => SendMessage(new AirlockControllerSetVentRolesMessage(addr, roles));
        _window.DoorSideChanged += (addr, side) => SendMessage(new AirlockControllerSetDoorSideMessage(addr, side));
        _window.TargetSensorChanged += (side, addr) => SendMessage(new AirlockControllerSetTargetSensorMessage(side, addr));
        _window.PresetChanged += (side, pressure) => SendMessage(new AirlockControllerSetPresetMessage(side, pressure));
        _window.MaintenanceChanged += enabled => SendMessage(new AirlockControllerSetMaintenanceMessage(enabled));
        _window.ForceSideRequested += side => SendMessage(new AirlockControllerForceSideMessage(side));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is AirlockControllerConfigUiState cast)
            _window?.UpdateState(cast);
    }
}
