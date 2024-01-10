using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Atmos.Piping.Trinary.Components;
using Content.Server.NodeContainer;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Piping;
using Content.Shared.Audio;
using Content.Shared.Whitelist;
using JetBrains.Annotations;
using System.Data;

namespace Content.Server.Atmos.Piping.Trinary.EntitySystems
{
    [UsedImplicitly]
    public sealed class AirlockControllerValveSystem : EntitySystem
    {
        [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;
        [Dependency] private readonly SharedAmbientSoundSystem _ambientSoundSystem = default!;
        [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
        [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<AirlockControllerValveComponent, ComponentInit>(OnInit);
            SubscribeLocalEvent<AirlockControllerValveComponent, AtmosDeviceUpdateEvent>(OnUpdate);
            SubscribeLocalEvent<AirlockControllerValveComponent, AtmosDeviceDisabledEvent>(OnFilterLeaveAtmosphere);
        }

        private void OnInit(EntityUid uid, AirlockControllerValveComponent comp, ComponentInit args)
        {
            UpdateAppearance(uid, comp);
        }

        private void OnUpdate(EntityUid uid, AirlockControllerValveComponent comp, ref AtmosDeviceUpdateEvent args)
        {
            if (!EntityManager.TryGetComponent(uid, out NodeContainerComponent? nodeContainer)
                || !EntityManager.TryGetComponent(uid, out AtmosDeviceComponent? device)
                || !_nodeContainer.TryGetNode(nodeContainer, comp.InletName, out PipeNode? inletPipe)
                || !_nodeContainer.TryGetNode(nodeContainer, comp.AirlockPipeName, out PipeNode? airlockPipe)
                || !_nodeContainer.TryGetNode(nodeContainer, comp.OutletName, out PipeNode? outletPipe))
            {
                _ambientSoundSystem.SetAmbience(uid, false);
                comp.Enabled = false;
                return;
            }

            if(comp.Status == AirlockControllerStatus.CyclingToA || comp.Status == AirlockControllerStatus.CyclingToB)
            {

                var airlockStartingPressure = airlockPipe.Air.Pressure;
                float targetPressure = (comp.Status == AirlockControllerStatus.CyclingToA) ? comp.TargetPressureA : comp.TargetPressureB;
                float pressureTreshold = (comp.Status == AirlockControllerStatus.CyclingToA) ? comp.TresholdPressureA : comp.TresholdPressureB;

                // Done cycling, do the opening.
                if(MathHelper.CloseTo(airlockStartingPressure, targetPressure, pressureTreshold))
                {
                    SetStatus(uid, comp, (comp.Status == AirlockControllerStatus.CyclingToA) ? AirlockControllerStatus.OpenA : AirlockControllerStatus.OpenB);
                    return;
                }


                PipeNode? gasSource;
                PipeNode? gasSink;

                // Pump gas in.
                if (airlockStartingPressure < targetPressure)
                {
                    gasSource = inletPipe;
                    gasSink = airlockPipe;
                }
                else // Pump gas out
                {
                    gasSource = airlockPipe;
                    gasSink = outletPipe;
                }

                if (gasSource.Air.TotalMoles > 0 && gasSource.Air.Temperature > 0)
                {
                    // We calculate the necessary moles to transfer using our good ol' friend PV=nRT.
                    var pressureDelta = targetPressure - airlockStartingPressure;
                    var transferMoles = (pressureDelta * gasSink.Air.Volume) / (gasSource.Air.Temperature * Atmospherics.R);

                    var removed = gasSource.Air.Remove(transferMoles);
                    _atmosphereSystem.Merge(gasSink.Air, removed);
                    _ambientSoundSystem.SetAmbience(uid, removed.TotalMoles > 0f);
                }
            }
        }

        private void SetStatus(EntityUid uid, AirlockControllerValveComponent comp, AirlockControllerStatus newStatus)
        {
            comp.Status = newStatus;

            if(newStatus != AirlockControllerStatus.CyclingToA && newStatus != AirlockControllerStatus.CyclingToB)
                _ambientSoundSystem.SetAmbience(uid, false);

            // TODO: Send events update signal stuff.
        }

        private void OnFilterLeaveAtmosphere(EntityUid uid, AirlockControllerValveComponent comp, ref AtmosDeviceDisabledEvent args)
        {
            comp.Enabled = false;
            UpdateAppearance(uid, comp);
            _ambientSoundSystem.SetAmbience(uid, false);
        }

        private void UpdateAppearance(EntityUid uid, AirlockControllerValveComponent? comp = null, AppearanceComponent? appearance = null)
        {
            if (!Resolve(uid, ref comp, ref appearance, false))
                return;

            _appearance.SetData(uid, FilterVisuals.Enabled, comp.Enabled, appearance);
        }
    }
}
