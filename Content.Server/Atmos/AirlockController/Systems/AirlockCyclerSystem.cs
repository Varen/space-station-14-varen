using Content.Server.Atmos.AirlockController.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Atmos.AirlockController;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server.Atmos.AirlockController.Systems;

/// <summary>
///     Panels are dumb devices. The controller writes their state, and they send back requests to cycle
/// </summary>
public sealed class AirlockCyclerSystem : EntitySystem
{
    [Dependency] private AirlockControllerSystem _controller = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPointLightSystem _pointLight = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AirlockCyclerComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<AirlockCyclerComponent, ExaminedEvent>(OnExamine);

        Subs.BuiEvents<AirlockCyclerComponent>(AirlockCyclerUiKey.Key,
            subs =>
        {
            subs.Event<AirlockCyclerCycleMessage>(OnCycleMessage);
        });
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<AirlockCyclerComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            // A controller that blew up stops claiming its panels
            if (comp.Controller is { } controller && TerminatingOrDeleted(controller))
                Unbind((uid, comp));

            UpdateCycleSound((uid, comp), now);
        }
    }

    /// <summary>
    ///     Where the controller pushes its state, every tick it updates itself
    /// </summary>
    public void SetStatus(
        Entity<AirlockCyclerComponent> ent,
        Entity<AirlockControllerComponent> controller,
        AirlockSide side,
        float? chamberPressure,
        bool warning)
    {
        var comp = ent.Comp;
        var source = controller.Comp;

        comp.Controller = controller.Owner;
        comp.Side = side;
        comp.State = source.State;
        comp.CurrentSide = source.CurrentSide;
        comp.StallReason = source.StallReason;
        comp.MaintenanceMode = source.MaintenanceMode;
        comp.ChamberPressure = chamberPressure;
        comp.Warning = warning;

        UpdateAppearance(ent);
        UpdateUi(ent);
    }

    /// <summary>
    ///     Forget the controller
    /// </summary>
    public void Unbind(Entity<AirlockCyclerComponent> ent)
    {
        var comp = ent.Comp;

        if (comp.Controller == null)
            return;

        comp.Controller = null;
        comp.StallReason = null;
        comp.State = AirlockCycleState.Idle;
        comp.ChamberPressure = null;
        comp.Warning = false;

        // Reads like an uninstalled controller
        comp.MaintenanceMode = true;

        UpdateAppearance(ent);
        UpdateUi(ent);
    }

    private void OnActivate(Entity<AirlockCyclerComponent> ent, ref ActivateInWorldEvent args)
    {
        if (!args.Complex || args.Handled || !this.IsPowered(ent, EntityManager))
            return;

        _ui.OpenUi(ent.Owner, AirlockCyclerUiKey.Key, args.User);
        UpdateUi(ent);
        args.Handled = true;
    }

    private void OnCycleMessage(Entity<AirlockCyclerComponent> ent, ref AirlockCyclerCycleMessage args)
    {
        if (ent.Comp.Controller is { } controller && TryComp<AirlockControllerComponent>(controller, out var comp))
            _controller.TryRequestCycleFrom((controller, comp), ent.Owner, args.Actor);

        UpdateUi(ent);
    }

    private void OnExamine(Entity<AirlockCyclerComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var comp = ent.Comp;

        if (comp.Controller == null)
        {
            args.PushMarkup(Loc.GetString("airlock-cycler-examine-unbound"));
            return;
        }

        if (comp.MaintenanceMode)
        {
            args.PushMarkup(Loc.GetString("airlock-controller-examine-maintenance"));
            return;
        }

        args.PushMarkup(Loc.GetString("airlock-controller-examine-state",
            ("state", Loc.GetString(AirlockControllerLocale.StateKey(comp.State)))));

        args.PushMarkup(Loc.GetString("airlock-controller-examine-side",
            ("side", Loc.GetString(AirlockControllerLocale.SideKey(comp.CurrentSide)))));

        if (comp.StallReason is { } reason)
        {
            args.PushMarkup(Loc.GetString("airlock-controller-examine-error",
                ("reason", Loc.GetString(AirlockControllerLocale.StallKey(reason)))));
        }
    }

    private void UpdateUi(Entity<AirlockCyclerComponent> ent)
    {
        var comp = ent.Comp;

        if (!_ui.IsUiOpen(ent.Owner, AirlockCyclerUiKey.Key))
            return;

        _ui.SetUiState(ent.Owner, AirlockCyclerUiKey.Key, new AirlockCyclerUiState
        {
            Bound = comp.Controller != null,
            Side = comp.Side,
            State = comp.State,
            CurrentSide = comp.CurrentSide,
            StallReason = comp.StallReason,
            MaintenanceMode = comp.MaintenanceMode,
            ChamberPressure = comp.ChamberPressure,
        });
    }

    private void UpdateAppearance(Entity<AirlockCyclerComponent> ent)
    {
        var comp = ent.Comp;

        var display = comp.MaintenanceMode
            ? AirlockControllerDisplay.Maintenance
            : comp.CurrentSide == AirlockSide.A
                ? AirlockControllerDisplay.SideA
                : AirlockControllerDisplay.SideB;

        _appearance.SetData(ent, AirlockControllerVisuals.State, comp.State);
        _appearance.SetData(ent, AirlockControllerVisuals.Display, display);
        _appearance.SetData(ent, AirlockControllerVisuals.Error, comp.StallReason != null);
        _appearance.SetData(ent, AirlockControllerVisuals.Cycling, comp.Warning);

        _pointLight.SetEnabled(ent, comp.Warning);
    }

    private void UpdateCycleSound(Entity<AirlockCyclerComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;

        if (!comp.Warning)
        {
            comp.NextCycleSound = TimeSpan.Zero;
            return;
        }

        if (now < comp.NextCycleSound)
            return;

        comp.NextCycleSound = now + comp.CycleSoundInterval;
        _audio.PlayPvs(comp.CycleSound, ent, comp.CycleSound.Params.AddVolume(comp.CycleVolume));
    }
}
