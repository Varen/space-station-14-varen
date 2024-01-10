using Content.Shared.Atmos;

namespace Content.Server.Atmos.Piping.Trinary.Components
{
    [Serializable]
    public enum AirlockControllerStatus
    {
        OpenA,        // A is currently open, B is closed and bolted
        CyclingToA,   // Equalizing towards pressure target A, Both sides closed and bolted
        OpenB,        // B is currently open, A is closed and bolted.
        CyclingToB,   // Equalizing towards pressure target B, both sides closed and bolted.
        Overridden    // Both sides are unbolted.
    }

    [RegisterComponent]
    public sealed partial class AirlockControllerValveComponent : Component
    {
        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("inlet")]
        public string InletName { get; set; } = "inlet";

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("airlock")]
        public string AirlockPipeName { get; set; } = "airlockpipe";

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("outlet")]
        public string OutletName { get; set; } = "outlet";

        [ViewVariables(VVAccess.ReadOnly)]
        [DataField("enabled")]
        public bool Enabled { get; set; } = false;

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("gain")]
        public float Gain { get; set; } = 10;

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("targetpressurea")]
        public float TargetPressureA { get; set; } = 0.0f;

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("tresholdpressurea")]
        public float TresholdPressureA { get; set; } = 0.1f;

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("targetpressureb")]
        public float TargetPressureB { get; set; } = Atmospherics.OneAtmosphere;

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("tresholdpressureb")]
        public float TresholdPressureB { get; set; } = 0.1f;

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("airlockcontrollerstatus")]
        public AirlockControllerStatus Status { get; set; } = AirlockControllerStatus.OpenA;

        [DataField("maxTransferRate")]
        public float MaxTransferRate { get; set; } = Atmospherics.MaxTransferRate;
    }
}
