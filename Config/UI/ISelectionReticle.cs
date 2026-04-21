using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace BaseLib.Config.UI;

public interface ISelectionReticle
{
    NSelectionReticle? Reticle { get; set; }

    void SetupSelectionReticle(Control targetControl, int margin = -12)
    {
        if (Reticle == null)
        {
            var reticleScene = PreloadManager.Cache.GetScene(SceneHelper.GetScenePath("ui/selection_reticle"));
            Reticle = reticleScene.Instantiate<NSelectionReticle>();
            Reticle.Name = "SelectionReticle";

            var reticleParent = targetControl;

            if (targetControl is Container)
            {
                // Add a wrapper to prevent the container from overriding offsets, etc. on the Reticle itself
                var dummyWrapper = new Control
                {
                    Name = "ReticleWrapper",
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    SizeFlagsVertical = Control.SizeFlags.ExpandFill
                };

                targetControl.AddChild(dummyWrapper);
                dummyWrapper.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                reticleParent = dummyWrapper;
            }

            reticleParent.AddChild(Reticle);
            Reticle.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, margin: margin);
        }

        targetControl.FocusEntered += () =>
        {
            if (NControllerManager.Instance?.IsUsingController == true)
                Reticle.OnSelect();
        };

        targetControl.FocusExited += () => Reticle.OnDeselect();
    }
}