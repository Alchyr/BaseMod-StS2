using System.Reflection;
using System.Reflection.Emit;
using BaseLib.Hooks;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;

namespace BaseLib.Patches.Features;

public static class VitalityPatch
{
    private static readonly Color VitalityOutlineColor = new Color("FFC800");
    private static readonly Color VitalityTextOutlineColor = new Color("998000");
    
    public static class VitalityField
    {
        /// <summary>
        /// !!IMPORTANT!! If intending to change this value, use SetVitality to avoid issues.
        /// </summary>
        private static readonly SpireField<Creature, int> TemporaryHp = new(() => 0);
        
        public static readonly SpireField<Creature, Action<int,int, Creature>?> VitalityChanged = new(() => null);
        public static readonly SpireField<Creature, Action<int,int>> VitalityChanged2 = new(() => null); // this exists only for CombatStateTracker.
        public static readonly SpireField<Creature, Tween?> VitalityTween = new(() => null);
        public static void SetVitality(Creature creature, int value)
        {
            if (value < 0)
                throw new ArgumentException("Block must be positive", nameof (value));
            if (TemporaryHp.Get(creature) == value)
                return;
            int tempHp = TemporaryHp.Get(creature);
            TemporaryHp.Set(creature, value);
            Action<int, int, Creature>? vitalityChanged = VitalityChanged.Get(creature);
            vitalityChanged?.Invoke(tempHp, TemporaryHp.Get(creature), creature);
            Action<int, int> vitalityChanged2 = VitalityChanged2.Get(creature);
            vitalityChanged?.Invoke(tempHp, TemporaryHp.Get(creature), creature);
        }

        public static int GetVitality(Creature creature)
        {
            return TemporaryHp.Get(creature);
        }
    }

    [HarmonyPatch(typeof(Creature))]
    [HarmonyPatch("LoseHpInternal")]
    public class HpInterceptPatch
    {
        // private static int temporaryHp;
        
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);
            MethodInfo getCurrentHpInfo = AccessTools.PropertyGetter(typeof(Creature), nameof(Creature.CurrentHp));

            MethodInfo tempHp = AccessTools.Method(typeof(HpInterceptPatch), nameof(TemporaryHpHandler));
            // MethodInfo unblockedOverride = AccessTools.Method(typeof(HpInterceptPatch), nameof(UnblockedDamageOverride));

            codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Call, getCurrentHpInfo)
                )
                .ThrowIfInvalid("Couldn't find getCurrentHp method for TemporaryHpHandler")
                .InsertAndAdvance(
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldloc_2),
                    new CodeInstruction(OpCodes.Call, tempHp),
                    new CodeInstruction(OpCodes.Stloc_2)
                );
            
            /*codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Ldloc_1),
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Call, getCurrentHpInfo),
                    new CodeMatch(OpCodes.Sub)
                )
                .ThrowIfInvalid("Couldn't find getCurrentHp method for TemporaryHpConfig")
                .InsertAfterAndAdvance(
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, unblockedOverride)
                );*/
            
            return codeMatcher.InstructionEnumeration();
        }

        private static int TemporaryHpHandler(Creature c, int num)
        {
            int tempHp = (int) VitalityField.GetVitality(c);
            if (num >= tempHp)
            {
                num -= tempHp;
                VitalityField.SetVitality(c, 0);
            }
            else
            {
                VitalityField.SetVitality(c, tempHp - num);
                num = 0;
            }
            return num;
        }
        
        /* Code for making Vitality trigger HP Loss effects. 
        private static int UnblockedDamageOverride(int unblockedDamage, Creature c)
        {
            if (TestModConfig.TriggerHpLoss)
            {
                temporaryHp -= (int) VitalityField.GetVitality(c);
                return unblockedDamage + temporaryHp;
            }
            return unblockedDamage;
        }*/
    }
    
    [HarmonyPatch(typeof(NHealthBar))]
    [HarmonyPatch("IsPoisonLethal")]
    public class TemporaryHpPoisonPatch
    {
        static bool Postfix(bool __result, int poisonDamage, Creature ____creature)
        {
            if (!__result)
            {
                return __result;
            }
            return ____creature.CurrentHp + VitalityField.GetVitality(____creature) <= poisonDamage;
        }
        
    }
    
    [HarmonyPatch(typeof(NHealthBar), "RefreshBlockUi")]
    public class TempHpOutline
    {
        
        [HarmonyPostfix]
        public static void SelfModulateOutline(Creature ____creature, Control ____blockOutline)
        {
            if (____creature.Block > 0 || VitalityField.GetVitality(____creature) <= 0) 
            {
                ____blockOutline.SelfModulate = Colors.White;
                return;
            }

            ____blockOutline.Visible = true;
            ____blockOutline.SelfModulate = VitalityOutlineColor;
        }
    }
    
    [HarmonyPatch(typeof(NHealthBar), "RefreshText")]
    public class VitalityText
    {
        
        [HarmonyPostfix]
        public static void SelfModulateOutline(Creature ____creature, MegaLabel ____hpLabel)
        {
            if (____creature.Block > 0 || VitalityField.GetVitality(____creature) <= 0)
                return;

            ____hpLabel.AddThemeColorOverride(ThemeConstants.Label.FontColor, NHealthBar._defaultFontColor);
            ____hpLabel.AddThemeColorOverride(ThemeConstants.Label.FontOutlineColor, VitalityTextOutlineColor);
        }
    }
    
    // Code courtesy of CanYou with alterations.
    [HarmonyPatch]
    public static class VitalityHealthBarPatch
    {
        public static readonly Dictionary<NHealthBar, (Control container, MegaLabel label)> VitalityUi = new();
        public static readonly Dictionary<Creature, NHealthBar> CreatureHealthBar = new();

        [HarmonyPatch(typeof(NHealthBar), nameof(NHealthBar.SetCreature))]
        [HarmonyPostfix]
        public static void CreateVitalityUi(NHealthBar __instance, Control ____blockContainer)
        {
            var vitalityContainer = (Control)____blockContainer.Duplicate();
            vitalityContainer.Visible = false;
            vitalityContainer.Name = "VitalityContainer";

            // Swap block icon for heart, tinted yellow
            var icon = vitalityContainer.GetNode<TextureRect>("BlockIcon");
            icon.Texture = GD.Load<Texture2D>("res://images/atlases/ui_atlas.sprites/top_bar/top_bar_heart.tres");
            icon.SelfModulate = VitalityOutlineColor;

            var shaderCode = @"
            shader_type canvas_item;
            uniform vec4 tint_color : source_color = vec4(0.6, 0.6, 0, 1.0);
            void fragment() {
                vec4 tex = texture(TEXTURE, UV);
                COLOR = vec4(tint_color.rgb, tex.a);
            }";
            var shader = new Shader();
            shader.Code = shaderCode;
            var material = new ShaderMaterial();
            material.Shader = shader;
            material.SetShaderParameter("tint_color", VitalityOutlineColor);
            icon.Material = material;

            var label = vitalityContainer.GetNode<MegaLabel>("BlockLabel");
            label.AddThemeColorOverride(ThemeConstants.Label.FontOutlineColor, VitalityTextOutlineColor);

            __instance.HpBarContainer.AddChild(vitalityContainer);
            vitalityContainer.SetAnchorsPreset(Control.LayoutPreset.CenterLeft, true);

            // Mirror to the right side
            vitalityContainer.Position = new Vector2(
                __instance.HpBarContainer.Size.X - vitalityContainer.Size.X,
                ____blockContainer.Position.Y);

            CreatureHealthBar[__instance._creature] = __instance;
            VitalityUi[__instance] = (vitalityContainer, label);
        }

        [HarmonyPatch(typeof(NHealthBar), "RefreshBlockUi")]
        [HarmonyPostfix]
        public static void RefreshVitalityUi(NHealthBar __instance, Creature ____creature)
        {
            if (!VitalityUi.TryGetValue(__instance, out var ui)) return;

            if (VitalityField.GetVitality(____creature) > 0)
            {
                ui.container.Visible = true;
                ui.label.SetTextAutoSize(((int)VitalityField.GetVitality(____creature)).ToString());
            }
            else
            {
                ui.container.Visible = false;
            }
        }

        [HarmonyPatch(typeof(NHealthBar), "SetHpBarContainerSizeWithOffsetsImmediately")]
        [HarmonyPostfix]
        public static void SetUpVitalityOffset(NHealthBar __instance)
        {
            if (!VitalityUi.TryGetValue(__instance, out var ui)) return;

            ui.container.Position = new Vector2(
                __instance.HpBarContainer.Size.X - ui.container.Size.X + 9f,
                __instance._blockContainer.Position.Y);
        }
    }

    // Enables a Vitality "Overflow" on the bar where if it loops over it changes colors. Subject to change.
    private static readonly Color[] HbColors = 
        [Colors.Gold, Colors.Green, Colors.MediumAquamarine, Colors.MediumVioletRed];

    private static Color HealthBarColors(int i) => i > HbColors.Length - 1 ? HbColors[^1] : HbColors[i];
    public class VitalityForecast : IHealthBarForecastSource 
    { 
        public IEnumerable<HealthBarForecastSegment> GetHealthBarForecastSegments(HealthBarForecastContext context) 
        { 
            var list = new List<HealthBarForecastSegment>(); 
            for (var i = 0; i <= VitalityField.GetVitality(context.Creature) / context.Creature.CurrentHp; i++) 
            { 
                list.Add(new HealthBarForecastSegment(
                    (int)VitalityField.GetVitality(context.Creature) - context.Creature.CurrentHp * i,
                    HealthBarColors(i), HealthBarForecastDirection.FromLeft, -i));
            }
            return list;
        }
    }
    public static void AnimateInVitality(int oldVitality, int vitalityGain, Creature creature) 
    { 
        AnimateInVitality(oldVitality, vitalityGain, VitalityHealthBarPatch.CreatureHealthBar[creature]);
    }
    
    public static void AnimateInVitality(int oldVitality, int vitalityGain, NHealthBar healthBar) 
    {
        if (oldVitality != 0 || vitalityGain == 0) 
            return;
        if (!VitalityHealthBarPatch.VitalityUi.TryGetValue(healthBar, out var ui)) return; 
        ui.container.Visible = true; 
        if (SaveManager.Instance.PrefsSave.FastMode == FastModeType.Instant) 
            return;
        var originalPosition = ui.container.Position = new Vector2(
            healthBar.HpBarContainer.Size.X - ui.container.Size.X + 9f,
            healthBar._blockContainer.Position.Y);
        ui.container.Modulate = StsColors.transparentWhite; 
        ui.container.Position = originalPosition - NHealthBar._blockAnimOffset; 
        VitalityField.VitalityTween.Get(healthBar._creature)?.Kill(); 
        VitalityField.VitalityTween.Set(healthBar._creature, healthBar.CreateTween().SetParallel()); 
         VitalityField.VitalityTween.Get(healthBar._creature)?
            .TweenProperty(ui.container, (NodePath)"modulate:a", 1f, 0.5).SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Sine);
        VitalityField.VitalityTween.Get(healthBar._creature)?
            .TweenProperty(ui.container, (NodePath)"position", originalPosition, 0.5)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        if (healthBar._creature.IsPlayer) healthBar.RefreshValues();
    }
    
    [HarmonyPatch] 
    public class NMultiplayerPlayerStatePatch 
    { 
        [HarmonyPatch(typeof(NMultiplayerPlayerState))] 
        [HarmonyPatch("_Ready")] 
        public class _ReadyPatch 
        { 
            static void Postfix(NMultiplayerPlayerState __instance) 
            { 
                VitalityField.VitalityChanged.Set(__instance.Player.Creature, 
                    VitalityField.VitalityChanged.Get(__instance.Player.Creature) + AnimateInVitality);
            }
        }
        
        [HarmonyPatch(typeof(NMultiplayerPlayerState))] 
        [HarmonyPatch("_ExitTree")] 
        public class _ExitTreePatch 
        { 
            static void Postfix(NMultiplayerPlayerState __instance) 
            { 
                VitalityField.VitalityChanged.Set(__instance.Player.Creature, 
                    VitalityField.VitalityChanged.Get(__instance.Player.Creature) - AnimateInVitality);
            }
        }
    }
    
    [HarmonyPatch]
    public class NCreatureStateDisplayPatch 
    { 
        [HarmonyPatch(typeof(NCreatureStateDisplay))] 
        [HarmonyPatch("SubscribeToCreatureEvents")] 
        public class SubscribeToCreatureEventsPatch 
        { 
            static void Postfix(NCreatureStateDisplay __instance) 
            { 
                if (__instance._creature == null) return; 
                VitalityField.VitalityChanged.Set(__instance._creature, 
                    VitalityField.VitalityChanged.Get(__instance._creature) + AnimateInVitality);
            }
        }
        
        [HarmonyPatch(typeof(NCreatureStateDisplay))] 
        [HarmonyPatch("_ExitTree")] 
        public class _ExitTreePatch 
        { 
            static void Postfix(NCreatureStateDisplay __instance) 
            { 
                if (__instance._creature == null) return; 
                VitalityField.VitalityChanged.Set(__instance._creature, 
                    VitalityField.VitalityChanged.Get(__instance._creature) - AnimateInVitality);
            }
        }
    }
    
    [HarmonyPatch]
    public class CombatStateTrackerPatch 
    { 
        [HarmonyPatch(typeof(CombatStateTracker))] 
        [HarmonyPatch("Subscribe")] 
        [HarmonyPatch([typeof(Creature)])]
        public class SubscribePatch 
        { 
            static void Postfix(Creature creature) 
            { 
                VitalityField.VitalityChanged.Set(creature, 
                    VitalityField.VitalityChanged.Get(creature) + AnimateInVitality);
            }
        }
        
        [HarmonyPatch(typeof(CombatStateTracker))] 
        [HarmonyPatch("Unsubscribe")] 
        [HarmonyPatch([typeof(Creature)])]
        public class UnsubscribePatch 
        { 
            static void Postfix(Creature creature) 
            { 
                VitalityField.VitalityChanged.Set(creature, 
                    VitalityField.VitalityChanged.Get(creature) - AnimateInVitality);
            }
        }
    }
}