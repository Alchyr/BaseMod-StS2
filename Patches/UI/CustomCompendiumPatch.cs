using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BaseLib.Abstracts;
using BaseLib.Utils;
using BaseLib.Utils.Patching;

namespace BaseLib.Patches.UI;

[HarmonyPatch(typeof(NCardLibrary), nameof(NCardLibrary._Ready))]
public class CustomPoolFilters
{
    [HarmonyTranspiler]
    static List<CodeInstruction> AddFilters(IEnumerable<CodeInstruction> instructions)
    {
        return new InstructionPatcher(instructions)
            .Match(new InstructionMatcher()
                .ldfld(AccessTools.DeclaredField(typeof(NCardLibrary), "_regentFilter"))
                .callvirt(null)
            )
            .Insert([
                CodeInstruction.LoadArgument(0),
                CodeInstruction.LoadArgument(0),
                new CodeInstruction(OpCodes.Ldfld, AccessTools.DeclaredField(typeof(NCardLibrary), "_poolFilters")),
                CodeInstruction.LoadArgument(0),
                new CodeInstruction(OpCodes.Ldfld, AccessTools.DeclaredField(typeof(NCardLibrary), "_cardPoolFilters")),
                CodeInstruction.LoadLocal(0),
                CodeInstruction.Call(typeof(CustomPoolFilters), nameof(GenerateCustomFilters))
            ]);
    }

    public static void GenerateCustomFilters(NCardLibrary library, Dictionary<NCardPoolFilter, Func<CardModel, bool>> filtering, Dictionary<CharacterModel, NCardPoolFilter> characterFilters, Callable updateFilter)
    {
        if (characterFilters.Count == 0) throw new Exception("Attempted to generate custom filters at wrong time");



        //change misc filter
        NCardPoolFilter? miscFilter = AccessTools.DeclaredField(typeof(NCardLibrary), "_miscPoolFilter").GetValue(library) as NCardPoolFilter;
        if (miscFilter == null) throw new Exception("Failed to get _miscPoolFilter");
        
        var oldFilter = filtering[miscFilter];
        filtering[miscFilter] = (c) =>
        {
            //Add an OR for existing in a card pool not tied to a character?
            bool newCondition = false;
            return newCondition || oldFilter(c);
        };

        Node filterParent = characterFilters[ModelDb.Character<Ironclad>()].GetParent();

        FieldInfo lastHovered = AccessTools.DeclaredField(typeof(NCardLibrary), "_lastHoveredControl");
        foreach (CustomCharacterModel model in ModelDbCustomCharacters.CustomCharacters)
        {
            NCardPoolFilter filter = GenerateFilter(model);

            //Add Filter to UI
            filterParent.AddChild(filter, forceReadableName: true);

            //Add filter to filter list
            characterFilters.Add(model, filter);

            //Add actual filtering
            var pool = model.CardPool;
            filtering.Add(filter, c => pool.AllCardIds.Contains(c.Id));

            //Connect signals
            filter.Connect(NCardPoolFilter.SignalName.Toggled, updateFilter);
            filter.Connect(Control.SignalName.FocusEntered, Callable.From(delegate
            {
                lastHovered.SetValue(library, filter);
            }));
        }
    }

    private static NCardPoolFilter GenerateFilter(CustomCharacterModel character)
    {
        //TextureRect named Image (56x56), position (4, 4), scale 0.9, pivot offset (28, 28)
        //  child TextureRect named Shadow (56x56), position (4, 3), scale 1.0, pivot offset (28, 28). Uses same image.
        //Control SelectionReticle (NSelectionReticle)
        //  Can probably just intantiate from scene scenes/ui/selection_reticle.tscn


        NCardPoolFilter filter = new()
        {
            Name = "FILTER-" + character.Id,
            Size = new(64, 64),
            CustomMinimumSize = new(64, 64)
        };

        //filter.Draw += filter.DrawDebug;

        Texture2D tex = character.IconTexture;
        TextureRect image = new()
        {
            Name = "Image",
            Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Size = new(56, 56),
            Position = new(4, 4),
            Scale = new(0.9f, 0.9f),
            PivotOffset = new(28, 28),
            Material = ShaderUtils.GenerateHsv(1, 1, 1)
        };

        TextureRect shadow = new()
        {
            Name = "Shadow", //not required
            Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Size = new(56, 56),
            Position = new(4, 3),
            PivotOffset = new(28, 28),
            ShowBehindParent = true,
            Modulate = Colors.Black with { A = 0.25f },
        };

        image.AddChild(shadow);
        NSelectionReticle reticle = PreloadManager.Cache.GetScene(SceneHelper.GetScenePath("ui/selection_reticle"))
            .Instantiate<NSelectionReticle>();
        reticle.Name = "SelectionReticle";
        reticle.UniqueNameInOwner = true;

        filter.AddChild(image);
        image.Owner = filter;
        filter.AddChild(reticle);
        reticle.Owner = filter; //I think this is necessary for UniqueNameInOwner to work properly with GetNode

        return filter;
    }

    private const float baseSize = 64f;
    [HarmonyPostfix]
    static void AdjustFilterScales(NCardLibrary __instance, Dictionary<NCardPoolFilter, Func<CardModel, bool>> ____poolFilters)
    {
        //if (__instance._poolFilters.First().Key.GetParentControl() is not GridContainer parent)
            //throw new Exception("Failed to find grid container for PoolFilters");
            
        if (____poolFilters.First().Key.GetParentControl() is not GridContainer parent)
            throw new Exception("Failed to find grid container for PoolFilters");

        //If too many filters, shrink them to fit properly
        int count = parent.GetChildCount();

        Vector2 scale = Vector2.One;
        int row = 4;
        float height = baseSize * scale.Y * MathF.Ceiling(count / (float) row);
        float heightLimit = baseSize * 3;

        while (height > heightLimit)
        {
            ++row;
            scale = Vector2.One * (4f / row);
            height = baseSize * scale.Y * MathF.Ceiling(count / (float) row);
        }

        //row = 6;
        
        FieldInfo imageField = AccessTools.Field(typeof(NCardPoolFilter), "_image");
        FieldInfo controllerSelectionReticleField = AccessTools.Field(typeof(NCardPoolFilter), "_controllerSelectionReticle");

        scale = Vector2.One * (4f / row);
        foreach (var child in parent.GetChildren())
        {
            if (child is not NCardPoolFilter filter) continue;

            //MainFile.Logger.Info($"Resizing {filter.Name}");
            filter.CustomMinimumSize *= scale;
            filter.Size *= scale;
            filter.PivotOffset *= scale;
            //MainFile.Logger.Info($"New sizes: {filter.CustomMinimumSize} | {filter.Size}");

            var image = imageField.GetValue(filter) as Control;
            image!.CustomMinimumSize *= scale;
            image!.Size *= scale;
            image!.PivotOffset *= scale;
            image!.Position = (filter.Size - image!.Size) * 0.5f;
            //MainFile.Logger.Info($"Image: {filter._image.CustomMinimumSize} | {filter._image.Size} | {filter._image.Position}");

            if (image!.GetChildCount() > 0 && image!.GetChild(0) is Control shadow)
            {
                shadow.CustomMinimumSize *= scale;
                shadow.Size *= scale;
                shadow.PivotOffset *= scale;
            }

            var controllerSelectionReticle = controllerSelectionReticleField.GetValue(filter) as NSelectionReticle;

            controllerSelectionReticle!.CustomMinimumSize *= scale;
            controllerSelectionReticle!.Size *= scale;
            controllerSelectionReticle!.PivotOffset *= scale;
            controllerSelectionReticle!.Position *= scale;
        }

        parent.Columns = row;
    }
}
