using System;
using System.Reflection;
using BaseLib.Abstracts;
using BaseLib.Extensions;
using Godot;
using Godot.Collections;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.addons.mega_text;

namespace BaseLib.Utils;

public static class CustomEnergyCounterFactory
{
    private const string DefaultLabelFontPath = "res://themes/kreon_bold_shared.tres";

    private static readonly FieldInfo? PlayerField = AccessTools.Field(typeof(NEnergyCounter), "_player");
    private static readonly FieldInfo? ParticlesField = AccessTools.Field(typeof(NParticlesContainer), "_particles");
    private static readonly StringName ShadowOffsetX = "shadow_offset_x";
    private static readonly StringName ShadowOffsetY = "shadow_offset_y";
    private static readonly StringName ShadowOutlineSize = "shadow_outline_size";

    public static NEnergyCounter FromScene(string path, Player player)
    {
        Node scene = PreloadManager.Cache.GetScene(path).Instantiate();
        return FromScene(scene, player);
    }

    public static NEnergyCounter FromScene(Node sceneRoot, Player player)
    {
        if (sceneRoot is NEnergyCounter counter)
        {
            SetPlayer(counter, player);
            return counter;
        }

        NEnergyCounter energyCounter = new();
        energyCounter.Name = sceneRoot.Name;

        if (sceneRoot is Control sceneRootControl)
        {
            CopyControlProperties(energyCounter, sceneRootControl);
        }
        else
        {
            energyCounter.Size = new Vector2(128f, 128f);
            energyCounter.PivotOffset = energyCounter.Size * 0.5f;
        }

        NParticlesContainer backVfx = BuildParticlesContainer(TakeDirectChild(sceneRoot, "EnergyVfxBack"), "EnergyVfxBack");
        energyCounter.AddUnique(backVfx, "EnergyVfxBack");

        Control layers = BuildLayers(sceneRoot);
        energyCounter.AddUnique(layers, "Layers");
        EnsureUniqueNames(energyCounter, layers);

        NParticlesContainer frontVfx = BuildParticlesContainer(TakeDirectChild(sceneRoot, "EnergyVfxFront"), "EnergyVfxFront");
        energyCounter.AddUnique(frontVfx, "EnergyVfxFront");

        MegaLabel label = BuildLabel(TakeDirectChild(sceneRoot, "Label"));
        energyCounter.AddChild(label);
        label.Owner = energyCounter;

        Node? starAnchor = TakeDirectChild(sceneRoot, "StarAnchor");
        if (starAnchor != null)
        {
            starAnchor.Name = "StarAnchor";
            starAnchor.UniqueNameInOwner = true;
            energyCounter.AddChild(starAnchor);
            starAnchor.Owner = energyCounter;
            SetChildrenOwner(energyCounter, starAnchor);
        }

        sceneRoot.Free();
        SetPlayer(energyCounter, player);
        return energyCounter;
    }

    public static NEnergyCounter FromLegacy(CustomEnergyCounter counter, Player player)
    {
        NEnergyCounter energyCounter = new()
        {
            Name = $"{player.Character.Id.Entry}EnergyCounter",
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

        Control layers = CreateFullRectControl("Layers");
        Control rotationLayers = CreateFullRectControl("RotationLayers");
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

        MegaLabel label = BuildDefaultLabel();
        energyCounter.AddChild(label);
        label.Owner = energyCounter;

        SetPlayer(energyCounter, player);
        return energyCounter;
    }

    private static Control BuildLayers(Node sceneRoot)
    {
        Node? maybeLayers = TakeDirectChild(sceneRoot, "Layers");
        Control layers;
        if (maybeLayers is Control existingLayers)
        {
            layers = existingLayers;
        }
        else
        {
            maybeLayers?.Free();
            layers = CreateFullRectControl("Layers");
        }

        layers.Name = "Layers";
        if (layers.GetNodeOrNull<Control>("%RotationLayers") == null)
        {
            Node? maybeRotationLayers = TakeDirectChild(sceneRoot, "RotationLayers");
            Control rotationLayers;
            if (maybeRotationLayers is Control existingRotationLayers)
            {
                rotationLayers = existingRotationLayers;
            }
            else
            {
                maybeRotationLayers?.Free();
                rotationLayers = CreateFullRectControl("RotationLayers");
            }

            rotationLayers.Name = "RotationLayers";
            rotationLayers.UniqueNameInOwner = true;
            layers.AddChild(rotationLayers);
            rotationLayers.Owner = layers;
        }

        return layers;
    }

    private static NParticlesContainer BuildParticlesContainer(Node? source, string name)
    {
        if (source is NParticlesContainer existingContainer)
        {
            existingContainer.Name = name;
            existingContainer.UniqueNameInOwner = true;
            return existingContainer;
        }

        NParticlesContainer container = new()
        {
            Name = name,
            UniqueNameInOwner = true
        };

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
        else if (source != null)
        {
            foreach (Node child in source.GetChildren())
            {
                source.RemoveChild(child);
                container.AddChild(child);
                child.Owner = container;
                SetChildrenOwner(container, child);
            }
        }

        SetParticles(container);
        source?.Free();
        return container;
    }

    private static MegaLabel BuildLabel(Node? source)
    {
        if (source is MegaLabel megaLabel)
        {
            EnsureLabelFont(megaLabel, megaLabel);
            megaLabel.Name = "Label";
            return megaLabel;
        }

        MegaLabel label = new()
        {
            Name = "Label"
        };

        if (source is Label sourceLabel)
        {
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
                label.MaxFontSize = Math.Max(36, sourceLabel.GetThemeFontSize(ThemeConstants.Label.fontSize, "Label"));
            }
        }
        else
        {
            MegaLabel defaultLabel = BuildDefaultLabel();
            CopyControlProperties(label, defaultLabel);
            EnsureLabelFont(label, null);
            CopyLabelThemeOverrides(label, defaultLabel);
            label.AutoSizeEnabled = true;
            label.MinFontSize = 32;
            label.MaxFontSize = 36;
            label.Text = "3/3";
        }

        source?.Free();
        return label;
    }

    private static MegaLabel BuildDefaultLabel()
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
        label.AddThemeColorOverride(ThemeConstants.Label.fontColor, new Color(1f, 0.964706f, 0.886275f, 1f));
        label.AddThemeColorOverride(ThemeConstants.Label.fontShadowColor, new Color(0f, 0f, 0f, 0.188235f));
        label.AddThemeColorOverride(ThemeConstants.Label.fontOutlineColor, new Color(0.3f, 0.0759f, 0.051f, 1f));
        label.AddThemeConstantOverride(ShadowOffsetX, 3);
        label.AddThemeConstantOverride(ShadowOffsetY, 2);
        label.AddThemeConstantOverride(ThemeConstants.Label.outlineSize, 16);
        label.AddThemeConstantOverride(ShadowOutlineSize, 16);
        label.AddThemeFontSizeOverride(ThemeConstants.Label.fontSize, 36);
        return label;
    }

    private static void EnsureLabelFont(MegaLabel target, Label? source)
    {
        Font? font = source?.GetThemeFont(ThemeConstants.Label.font, "Label");
        font ??= PreloadManager.Cache.GetAsset<Font>(DefaultLabelFontPath);
        target.AddThemeFontOverride(ThemeConstants.Label.font, font);
    }

    private static void CopyLabelThemeOverrides(MegaLabel target, Label source)
    {
        target.AddThemeColorOverride(ThemeConstants.Label.fontColor, source.GetThemeColor(ThemeConstants.Label.fontColor, "Label"));
        target.AddThemeColorOverride(ThemeConstants.Label.fontShadowColor, source.GetThemeColor(ThemeConstants.Label.fontShadowColor, "Label"));
        target.AddThemeColorOverride(ThemeConstants.Label.fontOutlineColor, source.GetThemeColor(ThemeConstants.Label.fontOutlineColor, "Label"));
        target.AddThemeConstantOverride(ShadowOffsetX, source.GetThemeConstant(ShadowOffsetX, "Label"));
        target.AddThemeConstantOverride(ShadowOffsetY, source.GetThemeConstant(ShadowOffsetY, "Label"));
        target.AddThemeConstantOverride(ThemeConstants.Label.outlineSize, source.GetThemeConstant(ThemeConstants.Label.outlineSize, "Label"));
        target.AddThemeConstantOverride(ShadowOutlineSize, source.GetThemeConstant(ShadowOutlineSize, "Label"));
        target.AddThemeFontSizeOverride(ThemeConstants.Label.fontSize, source.GetThemeFontSize(ThemeConstants.Label.fontSize, "Label"));
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

    private static Control CreateFullRectControl(string name)
    {
        return new Control
        {
            Name = name,
            AnchorRight = 1f,
            AnchorBottom = 1f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
    }

    private static void SetPlayer(NEnergyCounter counter, Player player)
    {
        PlayerField?.SetValue(counter, player);
    }

    private static void SetParticles(NParticlesContainer container)
    {
        Array<GpuParticles2D> particles = [];
        CollectParticles(container, particles);
        ParticlesField?.SetValue(container, particles);
    }

    private static void CollectParticles(Node node, Array<GpuParticles2D> particles)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is GpuParticles2D particle)
            {
                particles.Add(particle);
            }
            CollectParticles(child, particles);
        }
    }

    private static Node? TakeDirectChild(Node parent, string name)
    {
        foreach (Node child in parent.GetChildren())
        {
            if (child.Name == name)
            {
                parent.RemoveChild(child);
                return child;
            }
        }
        return null;
    }

    private static void EnsureUniqueNames(Node owner, Node node)
    {
        if (node.Name == "RotationLayers" || node.Name == "StarAnchor")
        {
            node.UniqueNameInOwner = true;
        }
        node.Owner = owner;
        foreach (Node child in node.GetChildren())
        {
            EnsureUniqueNames(owner, child);
        }
    }

    private static void SetChildrenOwner(Node owner, Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            child.Owner = owner;
            SetChildrenOwner(owner, child);
        }
    }

    private static void CopyControlProperties(Control target, Control source)
    {
        CopyCanvasItemProperties(target, source);
        target.LayoutMode = source.LayoutMode;
        target.AnchorLeft = source.AnchorLeft;
        target.AnchorTop = source.AnchorTop;
        target.AnchorRight = source.AnchorRight;
        target.AnchorBottom = source.AnchorBottom;
        target.OffsetLeft = source.OffsetLeft;
        target.OffsetTop = source.OffsetTop;
        target.OffsetRight = source.OffsetRight;
        target.OffsetBottom = source.OffsetBottom;
        target.GrowHorizontal = source.GrowHorizontal;
        target.GrowVertical = source.GrowVertical;
        target.Size = source.Size;
        target.CustomMinimumSize = source.CustomMinimumSize;
        target.PivotOffset = source.PivotOffset;
        target.MouseFilter = source.MouseFilter;
        target.FocusMode = source.FocusMode;
        target.ClipContents = source.ClipContents;
    }

    private static void CopyCanvasItemProperties(CanvasItem target, CanvasItem source)
    {
        target.Visible = source.Visible;
        target.Modulate = source.Modulate;
        target.SelfModulate = source.SelfModulate;
        target.ShowBehindParent = source.ShowBehindParent;
        target.TopLevel = source.TopLevel;
        target.ZIndex = source.ZIndex;
        target.ZAsRelative = source.ZAsRelative;
        target.YSortEnabled = source.YSortEnabled;
        target.TextureFilter = source.TextureFilter;
        target.TextureRepeat = source.TextureRepeat;
        target.Material = source.Material;
        target.UseParentMaterial = source.UseParentMaterial;

        if (target is Node2D targetNode2D && source is Node2D sourceNode2D)
        {
            targetNode2D.Position = sourceNode2D.Position;
            targetNode2D.Rotation = sourceNode2D.Rotation;
            targetNode2D.Scale = sourceNode2D.Scale;
            targetNode2D.Skew = sourceNode2D.Skew;
        }
    }
}
