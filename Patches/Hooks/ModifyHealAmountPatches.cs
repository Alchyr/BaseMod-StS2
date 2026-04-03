using System.Reflection;
using System.Reflection.Emit;
using BaseLib.Hooks;
using BaseLib.Utils.Patching;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Patches.Hooks;

/// <summary>
/// IHealAmountModifier.ModifyHealAdditive() -> AbstractModel.ModifyHealAmount() -> IHealAmountModifier.ModifyHealMultiplicative()
/// reserve AbstractModel.ModifyHealAmount() in the process for compatibility
/// </summary>
[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Heal), MethodType.Async)]
public static class ModifyHealAmountPatches
{
//amount = Hook.ModifyHealAmount(creature.Player?.RunState ?? creature.CombatState?.RunState ?? NullRunState.Instance, creature.CombatState, creature, amount);
    [HarmonyTranspiler]
    static List<CodeInstruction> Patch(IEnumerable<CodeInstruction> code)
    {
        return new InstructionPatcher(code)
            .Match(new InstructionMatcher()
                .ldarg_0()
                .ldfld(null)
                .ldfld(null).PredicateMatch(op => op is FieldInfo field && field.Name.Contains("creature"))
            )
            .CopyMatch(out var loadCreature)
            .Match(new InstructionMatcher()
                .ldfld(null).PredicateMatch(op => op is FieldInfo field && field.Name.Equals("amount"))
            )
            .Step(-1)
            .GetOperand(out var amountField)
            .Step(1)
            .Insert(loadCreature)
            .Insert([
                CodeInstruction.Call(typeof(ModifyHealAmountPatches), nameof(ModifyHealAmountPatches.ModifyHeal)),
                new CodeInstruction(OpCodes.Dup),
                CodeInstruction.LoadArgument(0),
                new CodeInstruction(OpCodes.Stfld, amountField)
            ]);
    }

    public static decimal ModifyHeal(decimal amount, Creature creature)
    {
        var runState = creature.Player?.RunState ?? creature.CombatState?.RunState ?? NullRunState.Instance;
        var combatState = creature.CombatState;

        ModifyAdditive(runState, combatState, creature, ref amount);
        ModifyMultiplicative(runState, combatState, creature, ref amount);
        
        return amount;
    }
    
    static void ModifyAdditive(IRunState runState, CombatState? combatState, Creature creature, ref decimal amount)
    {
        decimal num = amount;

        foreach (var item in runState.IterateHookListeners(combatState))
        {
            if (item is IHealAmountModifier mod)
                num += mod.ModifyHealAdditive(creature, amount); // pass the amount before any addition
        }

        amount = num;
    }

    static void ModifyMultiplicative(IRunState runState, CombatState? combatState, Creature creature, ref decimal __result)
    {
        decimal num = __result;

        foreach (var item in runState.IterateHookListeners(combatState))
        {
            if (item is IHealAmountModifier mod)
                num *= mod.ModifyHealMultiplicative(creature, __result); // pass the amount before any multiplication
        }

        __result = Math.Max(0m, num);
    }
}
