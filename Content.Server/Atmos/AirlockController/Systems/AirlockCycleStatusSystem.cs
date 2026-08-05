using Content.Server.Atmos.AirlockController.Components;
using Content.Shared.Atmos.AirlockController;
using Content.Shared.Examine;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server.Atmos.AirlockController.Systems;

/// <summary>
///     Sprite, light, alarm and examine for anything showing an airlock cycle.
///     Shared by controller and its panels.
/// </summary>
public sealed class AirlockCycleStatusSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPointLightSystem _pointLight = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<AirlockCycleAlarmComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Active)
            {
                comp.NextSound = TimeSpan.Zero;
                continue;
            }

            if (now < comp.NextSound)
                continue;

            comp.NextSound = now + comp.Interval;
            _audio.PlayPvs(comp.Sound, uid, comp.Sound.Params.AddVolume(comp.Volume));
        }
    }

    public void Apply(EntityUid uid, AirlockCycleStatus status)
    {
        var display = status.Maintenance
            ? AirlockControllerDisplay.Maintenance
            : status.Side == AirlockSide.A
                ? AirlockControllerDisplay.SideA
                : AirlockControllerDisplay.SideB;

        _appearance.SetData(uid, AirlockControllerVisuals.State, status.State);
        _appearance.SetData(uid, AirlockControllerVisuals.Display, display);
        _appearance.SetData(uid, AirlockControllerVisuals.Error, status.Stall != null);
        _appearance.SetData(uid, AirlockControllerVisuals.Cycling, status.Warning);

        _pointLight.SetEnabled(uid, status.Warning);

        if (TryComp<AirlockCycleAlarmComponent>(uid, out var alarm))
            alarm.Active = status.Warning;
    }

    public void Examine(AirlockCycleStatus status, ExaminedEvent args)
    {
        if (status.Maintenance)
        {
            args.PushMarkup(Loc.GetString("airlock-controller-examine-maintenance"));
            return;
        }

        args.PushMarkup(Loc.GetString("airlock-controller-examine-state",
            ("state", Loc.GetString(AirlockControllerLocale.StateKey(status.State)))));

        args.PushMarkup(Loc.GetString("airlock-controller-examine-side",
            ("side", Loc.GetString(AirlockControllerLocale.SideKey(status.Side)))));

        if (status.Stall is { } reason)
        {
            args.PushMarkup(Loc.GetString("airlock-controller-examine-error",
                ("reason", Loc.GetString(AirlockControllerLocale.StallKey(reason)))));
        }
    }
}
