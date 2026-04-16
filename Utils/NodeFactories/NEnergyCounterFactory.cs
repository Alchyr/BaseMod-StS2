using System.Reflection;
using BaseLib.Abstracts;
using BaseLib.Extensions;
using Godot;
using Godot.Collections;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;

namespace BaseLib.Utils.NodeFactories;

internal class NEnergyCounterFactory : NodeFactory<NEnergyCounter>
{
    private const string DefaultLabelFontPath = "res://themes/kreon_bold_shared.tres";
    
    private static readonly FieldInfo? ParticlesField = AccessTools.Field(typeof(NParticlesContainer), "_particles");
    private static readonly StringName ShadowOffsetX = "shadow_offset_x";
    private static readonly StringName ShadowOffsetY = "shadow_offset_y";
    private static readonly StringName ShadowOutlineSize = "shadow_outline_size";
    
    public NEnergyCounterFactory() : base([
        new NodeInfo<MegaLabel>("Label"),
        new NodeInfo<Control>("%Layers"),
        new NodeInfo<Control>("%RotationLayers"),
        new NodeInfo<NParticlesContainer>("%EnergyVfxBack"),
        new NodeInfo<NParticlesContainer>("%EnergyVfxFront"),
        new NodeInfo<NParticlesContainer>("%StarAnchor") //Custom
    ])
    { }

    protected override NEnergyCounter CreateBareFromResource(object resource)
    {
        switch (resource)
        {
            case CustomEnergyCounter counter:
                return FromLegacy(counter);
        }
        
        return base.CreateBareFromResource(resource);
    }

    protected override void ConvertScene(NEnergyCounter target, Node? source)
    {
        if (source != null)
        {
            target.Name = source.Name;
            
            switch (target)
            {
                case Control targetControl when source is Control sourceControl:
                    CopyControlProperties(targetControl, sourceControl);
                    break;
                case CanvasItem targetItem when source is CanvasItem sourceItem:
                    CopyCanvasItemProperties(targetItem, sourceItem);
                    break;
                case Control energyCounter:
                    energyCounter.Size = new Vector2(128f, 128f);
                    energyCounter.PivotOffset = energyCounter.Size * 0.5f;
                    break;
            }
        }
        TransferAndCreateNodes(target, source);
    }

    protected override void GenerateNode(Node target, INodeInfo required)
    {
        switch (required.Path)
        {
            case "Label":
                var label = CreateDefaultLabel();
                target.AddChild(label);
                break;
            case "%RotationLayers":
                var control = CreateFullRectControl(null);
                target.AddUnique(control, "RotationLayers");
                break;
            case "%EnergyVfxBack":
                var backVfx = CreateParticlesContainer(null, "EnergyVfxBack");
                target.AddUnique(backVfx);
                break;
            case "%EnergyVfxFront":
                var frontVfx = CreateParticlesContainer(null, "EnergyVfxFront");
                target.AddUnique(frontVfx);
                break;
        }
    }

    protected override Node ConvertNodeType(Node node, Type targetType)
    {
        if (targetType == typeof(NParticlesContainer))
        {
            return CreateParticlesContainer(node, node.Name);
        }

        if (targetType == typeof(Control))
        {
            return CreateFullRectControl(node);
        }

        if (targetType == typeof(MegaLabel))
        {
            return CreateLabel(node) ?? base.ConvertNodeType(node, targetType);
        }
        
        return base.ConvertNodeType(node, targetType);
    }
    
    private static Control CreateFullRectControl(Node? n)
    {
        var rectControl = new Control
        {
            AnchorRight = 1f,
            AnchorBottom = 1f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        if (n == null) return rectControl;

        rectControl.Name = n.Name;
        
        n.GetParent()?.RemoveChild(n);
        n.Name = "_" + n.Name;
        rectControl.AddChild(n);

        return rectControl;
    }
    
    private static NParticlesContainer CreateParticlesContainer(Node? source, StringName name)
    {
        NParticlesContainer container = new()
        {
            Name = name,
            UniqueNameInOwner = true
        };
        
        if (source != null)
        {
            source.Name = "_" + source.Name;
        }

        if (source is CanvasItem sourceCanvas)
        {
            CopyCanvasItemProperties(container, sourceCanvas);
        }

        if (source is GpuParticles2D singleParticle)
        {
            container.AddChild(singleParticle);
            singleParticle.Owner = container;
            SetParticles(container);
            return container;
        }

        if (source != null)
        {
            source.GetParent()?.RemoveChild(source);
            container.AddChild(source);
        }

        SetParticles(container);
        return container;
    }

    private static void SetParticles(NParticlesContainer container)
    {
        Array<GpuParticles2D> particles = [];
        CollectParticles(container, particles);
        ParticlesField?.SetValue(container, particles);
    }

    private static void CollectParticles(Node node, Array<GpuParticles2D> particles)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is GpuParticles2D particle)
            {
                particles.Add(particle);
            }
            CollectParticles(child, particles);
        }
    }
    
    //-- Legacy --
    public static NEnergyCounter FromLegacy(CustomEnergyCounter counter)
    {
        NEnergyCounter energyCounter = new()
        {
            Name = "LegacyEnergyCounter",
            Size = new Vector2(128f, 128f),
            PivotOffset = new Vector2(64f, 64f)
        };

        NParticlesContainer backVfx = new()
        {
            Name = "EnergyVfxBack",
            Position = new Vector2(64f, 64f),
            Modulate = counter.BurstColor
        };
        energyCounter.AddUnique(backVfx, "EnergyVfxBack");
        SetParticles(backVfx);

        var layers = CreateFullRectControl(null);
        var rotationLayers = CreateFullRectControl(null);
        rotationLayers.PivotOffset = new Vector2(64f, 64f);
        layers.AddUnique(rotationLayers, "RotationLayers");

        AddLayer(layers, "Layer1", counter.LayerImagePath(1));
        AddLayer(rotationLayers, "Layer2", counter.LayerImagePath(2), rotates: true);
        AddLayer(rotationLayers, "Layer3", counter.LayerImagePath(3), rotates: true);
        AddLayer(layers, "Layer4", counter.LayerImagePath(4));
        AddLayer(layers, "Layer5", counter.LayerImagePath(5));

        energyCounter.AddUnique(layers, "Layers");

        NParticlesContainer frontVfx = new()
        {
            Name = "EnergyVfxFront",
            Position = new Vector2(64f, 64f),
            Modulate = counter.BurstColor
        };
        energyCounter.AddUnique(frontVfx, "EnergyVfxFront");
        SetParticles(frontVfx);

        var label = CreateDefaultLabel();
        energyCounter.AddChild(label);
        label.Owner = energyCounter;

        return energyCounter;
    }
    
    private static void AddLayer(Control parent, string name, string texturePath, bool rotates = false)
    {
        TextureRect layer = new()
        {
            Name = name,
            AnchorRight = 1f,
            AnchorBottom = 1f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Texture = ResourceLoader.Load<Texture2D>(texturePath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
        if (rotates)
        {
            layer.PivotOffset = new Vector2(64f, 64f);
        }
        parent.AddChild(layer);
        layer.Owner = parent;
    }
    
    //-- MegaLabel chunk --
    private static MegaLabel? CreateLabel(Node? source)
    {
        if (source is Label sourceLabel)
        {
            MegaLabel label = new()
            {
                Name = source.Name
            };
            
            CopyControlProperties(label, sourceLabel);
            label.Text = sourceLabel.Text;
            label.HorizontalAlignment = sourceLabel.HorizontalAlignment;
            label.VerticalAlignment = sourceLabel.VerticalAlignment;
            label.AutowrapMode = sourceLabel.AutowrapMode;
            label.ClipText = sourceLabel.ClipText;
            label.Uppercase = sourceLabel.Uppercase;
            label.VisibleCharactersBehavior = sourceLabel.VisibleCharactersBehavior;

            EnsureLabelFont(label, sourceLabel);
            CopyLabelThemeOverrides(label, sourceLabel);

            if (sourceLabel is MegaLabel sourceMegaLabel)
            {
                label.AutoSizeEnabled = sourceMegaLabel.AutoSizeEnabled;
                label.MinFontSize = sourceMegaLabel.MinFontSize;
                label.MaxFontSize = sourceMegaLabel.MaxFontSize;
            }
            else
            {
                label.AutoSizeEnabled = true;
                label.MinFontSize = 32;
                label.MaxFontSize = Math.Max(36, sourceLabel.GetThemeFontSize(BetaMainCompatibility.Renamed.FontSize, "Label"));
            }
            
            source?.Free();
            return label;
        }

        return null;
    }

    private static MegaLabel CreateDefaultLabel()
    {
        MegaLabel label = new()
        {
            Name = "Label",
            AnchorRight = 1f,
            AnchorBottom = 1f,
            OffsetLeft = 16f,
            OffsetTop = -29f,
            OffsetRight = -16f,
            OffsetBottom = 29f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Text = "3/3",
            AutoSizeEnabled = true,
            MinFontSize = 32,
            MaxFontSize = 36
        };
        EnsureLabelFont(label, null);
        label.AddThemeColorOverride(BetaMainCompatibility.Renamed.FontColor, new Color(1f, 0.964706f, 0.886275f, 1f));
        label.AddThemeColorOverride(BetaMainCompatibility.Renamed.FontShadowColor, new Color(0f, 0f, 0f, 0.188235f));
        label.AddThemeColorOverride(BetaMainCompatibility.Renamed.FontOutlineColor, new Color(0.3f, 0.0759f, 0.051f, 1f));
        label.AddThemeConstantOverride(ShadowOffsetX, 3);
        label.AddThemeConstantOverride(ShadowOffsetY, 2);
        label.AddThemeConstantOverride(BetaMainCompatibility.Renamed.OutlineSize, 16);
        label.AddThemeConstantOverride(ShadowOutlineSize, 16);
        label.AddThemeFontSizeOverride(BetaMainCompatibility.Renamed.FontSize, 36);
        return label;
    }

    private static void EnsureLabelFont(MegaLabel target, Label? source)
    {
        Font? font = source?.GetThemeFont(BetaMainCompatibility.Renamed.Font, "Label");
        font ??= PreloadManager.Cache.GetAsset<Font>(DefaultLabelFontPath);
        target.AddThemeFontOverride(BetaMainCompatibility.Renamed.Font, font);
    }

    private static void CopyLabelThemeOverrides(MegaLabel target, Label source)
    {
        target.AddThemeColorOverride(BetaMainCompatibility.Renamed.FontColor, source.GetThemeColor(BetaMainCompatibility.Renamed.FontColor, "Label"));
        target.AddThemeColorOverride(BetaMainCompatibility.Renamed.FontShadowColor, source.GetThemeColor(BetaMainCompatibility.Renamed.FontShadowColor, "Label"));
        target.AddThemeColorOverride(BetaMainCompatibility.Renamed.FontOutlineColor, source.GetThemeColor(BetaMainCompatibility.Renamed.FontOutlineColor, "Label"));
        target.AddThemeConstantOverride(ShadowOffsetX, source.GetThemeConstant(ShadowOffsetX, "Label"));
        target.AddThemeConstantOverride(ShadowOffsetY, source.GetThemeConstant(ShadowOffsetY, "Label"));
        target.AddThemeConstantOverride(BetaMainCompatibility.Renamed.OutlineSize, source.GetThemeConstant(BetaMainCompatibility.Renamed.OutlineSize, "Label"));
        target.AddThemeConstantOverride(ShadowOutlineSize, source.GetThemeConstant(ShadowOutlineSize, "Label"));
        target.AddThemeFontSizeOverride(BetaMainCompatibility.Renamed.FontSize, source.GetThemeFontSize(BetaMainCompatibility.Renamed.FontSize, "Label"));
    }
}