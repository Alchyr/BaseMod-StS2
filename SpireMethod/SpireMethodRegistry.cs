using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;

namespace BaseLib.SpireMethod;

/// <summary>
/// Central registry for SpireMethod handlers.
///
/// Holds a <c>Dictionary&lt;MethodInfo, List&lt;SpireMethodHandlerBase&gt;&gt;</c> keyed by the
/// declaring base-class method, applies lazy Harmony postfixes, and dispatches at runtime
/// by matching <c>__instance.GetType()</c> against each handler's
/// <see cref="SpireMethodHandlerBase.TargetType"/>.
///
/// The postfix dispatchers receive the patched <c>MethodBase</c> via Harmony's
/// <c>__originalMethod</c> injection, so a single static postfix method handles all
/// registrations for a given return-type category.
/// </summary>
internal static class SpireMethodRegistry
{
    private static readonly Dictionary<MethodInfo, List<SpireMethodHandlerBase>> _handlers = [];

    // Tracks which methods have already had a Harmony postfix applied. I did have concerns on startup impact, but adapt to normal patching if lazy patching like this is not preferable.
    private static readonly HashSet<MethodInfo> _patched = [];

    internal static void Register(MethodInfo declaringMethod, SpireMethodHandlerBase handler)
    {
        ref var handlers = ref CollectionsMarshal.GetValueRefOrAddDefault(_handlers, declaringMethod, out _);
        handlers ??= [];
        handlers.Add(handler);

        // Apply the Harmony postfix lazily (once per method).
        LazyPatch(declaringMethod);
    }

    private static void LazyPatch(MethodInfo method)
    {
        if (!_patched.Add(method)) return;

        var returnType = method.ReturnType;

        var postfix = new HarmonyMethod(returnType switch
        {
            _ when returnType == typeof(Task) => AccessTools.Method(typeof(AsyncPostfixDispatcher),
                nameof(AsyncPostfixDispatcher.Postfix)),
            _ when returnType == typeof(void) => AccessTools.Method(typeof(VoidPostfixDispatcher),
                nameof(VoidPostfixDispatcher.Postfix)),
            _ => AccessTools.Method(typeof(ValuePostfixDispatcher<>).MakeGenericType(returnType),
                nameof(ValuePostfixDispatcher<object>.Postfix))
        });

        BaseLibMain.MainHarmony.Patch(method, postfix: postfix);
        BaseLibMain.Logger.Info(
            $"SpireMethod: patched {method.DeclaringType?.Name}.{method.Name} (return: {returnType.Name})");
    }

    private static List<SpireMethodHandlerBase>? GetHandlers(MethodBase originalMethod, Type instanceType)
    {
        var key = ((MethodInfo)originalMethod).GetBaseDefinition();
        if (!_handlers.TryGetValue(key, out var all)) return null;

        // Filter to handlers whose TargetType is assignable from the instance's runtime type
        // (so handlers for RunicPyramid only fire on RunicPyramid instances).
        List<SpireMethodHandlerBase>? result = null;
        foreach (var handler in all)
        {
            if (!handler.TargetType.IsAssignableFrom(instanceType)) continue;
            result ??= [];
            result.Add(handler);
        }

        return result;
    }

    private static class AsyncPostfixDispatcher
    {
        public static void Postfix(object __instance, ref Task __result, object[] __args, MethodBase __originalMethod)
        {
            var handlers = GetHandlers(__originalMethod, __instance.GetType());
            if (handlers == null) return;

            __result = ChainAsync(handlers, __instance, __args);
        }

        private static async Task ChainAsync(List<SpireMethodHandlerBase> handlers, object instance, object[] args)
        {
            foreach (var handler in handlers)
            {
                await handler.InvokeAsync(instance, args);
            }
        }
    }

    private static class VoidPostfixDispatcher
    {
        public static void Postfix(object __instance, object[] __args, MethodBase __originalMethod)
        {
            var handlers = GetHandlers(__originalMethod, __instance.GetType());
            if (handlers == null) return;

            foreach (var handler in handlers)
            {
                handler.InvokeVoid(__instance, __args);
            }
        }
    }

    private static class ValuePostfixDispatcher<TReturn>
    {
        public static void Postfix(object __instance, ref TReturn __result, object[] __args,
            MethodBase __originalMethod)
        {
            var handlers = GetHandlers(__originalMethod, __instance.GetType());
            if (handlers == null) return;

            foreach (var handler in handlers)
            {
                __result = (TReturn)handler.InvokeValue(__instance, __result, __args)!;
            }
        }
    }
}