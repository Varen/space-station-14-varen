using Content.Shared.Atmos.AirlockController;

namespace Content.Client.Atmos.AirlockController;

[RegisterComponent]
public sealed partial class AirlockControllerVisualsComponent : Component
{
    [DataField(required: true)]
    public Dictionary<AirlockCycleState, string> StateSprites = new();

    [DataField(required: true)]
    public Dictionary<AirlockControllerDisplay, string> DisplaySprites = new();

    [DataField]
    public List<AirlockControllerVisualLayers> HideOnDepowered = new()
    {
        AirlockControllerVisualLayers.State,
        AirlockControllerVisualLayers.Display,
        AirlockControllerVisualLayers.Error,
        AirlockControllerVisualLayers.Light,
    };
}
