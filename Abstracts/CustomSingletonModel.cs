using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Exceptions;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Abstracts;

public abstract class CustomSingletonModel : SingletonModel {

    public override bool ShouldReceiveCombatHooks => registerSettings.SubscribeToCombatStateHooks;

    public abstract SingletonSettings registerSettings { get; }
    public abstract string modId { get; }

    protected CustomSingletonModel() {
        BaseLibMain.Logger.Info($"CustomSingletonModel:{GetType()} got Constructed");
        if (ModelDb.Contains(GetType()))
            throw new DuplicateModelException(GetType());
    }
    
    private static Dictionary<string, List<Type>> modelsToRegister = []; 
    private static Dictionary<string, Assembly> modAsseblies = []; 
    private static List<string> unregisteredModsCustomSingleton = []; 
    
    public struct SingletonSettings {
        public bool SubscribeToRunStateHooks;
        public bool SubscribeToCombatStateHooks;
    }
    public static void Subscribe(string mod, Assembly assembly) {
        if (unregisteredModsCustomSingleton.Contains(mod)) {
            BaseLibMain.Logger.Warn($"{mod} tried to Subscribe CustomSingletonModel multiple times, skipping.");
            return;
        }
        modAsseblies.Add(mod,assembly);
        unregisteredModsCustomSingleton.Add(mod);
    }

    public static void AddToModelList(string modId, Type type) {
        if(modelsToRegister.Values.Any((i)=> i.Any((i2)=>i2.Equals(type)))) throw new DuplicateModelException(type);
        if (modelsToRegister.ContainsKey(modId)) {
            modelsToRegister[modId].Add(type);
        } else {
            modelsToRegister.Add(modId,[type]);
        }
    }

    public static CustomSingletonModel RegisterSingletonType(Type type) {
        BaseLibMain.Logger.Info($"Trying to register {type}");
        CustomSingletonModel? singletonInstance = ModelDb.GetByIdOrNull<CustomSingletonModel>(ModelDb.GetId(type));
        if (singletonInstance == null) {
            ModelDb.Inject(type);
        }
        singletonInstance ??= ModelDb.GetByIdOrNull<CustomSingletonModel>(ModelDb.GetId(type));
        if (singletonInstance == null) {
            throw new TypeLoadException();
        }
        return singletonInstance;
    }
    
    private static T ModelDbGetByType<T>(Type type) where T : AbstractModel {
        return ModelDb.GetById<T>(ModelDb.GetId(type));
    }

    protected static IEnumerable<AbstractModel> TypeToSubscription(object _, List<Type> types, Func<CustomSingletonModel,bool> hook) {
        var models = types.FindAll((type) => hook(ModelDbGetByType<CustomSingletonModel>(type)))
        .ConvertAll((type) => ModelDbGetByType<CustomSingletonModel>(type));
        return models;
    }

    [HarmonyPatch(typeof(ModelDb), nameof(ModelDb.Init))]
    class RegisterCustomSingletonModel {
        public static List<string> registeredMods { get; private set; } = [];

        [HarmonyPostfix]
        static void Postfix() {
            List<Type> types = [];
            foreach (var keyValuePair in modAsseblies) {
                var mod = keyValuePair.Key;
                var assembly = keyValuePair.Value;
                List<Type> typesToAdd = assembly.GetTypes().ToList()
                    .FindAll((t) => t.IsSubclassOf(typeof(CustomSingletonModel)));
                if (!unregisteredModsCustomSingleton.Contains(mod) && typesToAdd.Count > 0) {
                    BaseLibMain.Logger.Error($"Mod:{mod} has CustomSingletonModel but didnt declare them to register.");
                }
                if(typesToAdd.Count == 0) continue;
                if(!unregisteredModsCustomSingleton.Contains(mod)) continue;
                registeredMods.Add(mod);
                types.AddRange(typesToAdd);
            }
            foreach (var typeToInit in types) {
                var singletonInstance = RegisterSingletonType(typeToInit);
                var mod = singletonInstance.modId;
                BaseLibMain.Logger.Info($"Registered CustomSingletonModel with Id:{singletonInstance.Id}");
                AddToModelList(mod, typeToInit);
            }
            foreach (var mod in registeredMods) {
                var ourModels = modelsToRegister[mod];
                ModHelper.SubscribeForRunStateHooks(mod,(_) => TypeToSubscription(_,ourModels,
                    (m)=>m.registerSettings.SubscribeToRunStateHooks)
                );
                ModHelper.SubscribeForCombatStateHooks(mod,(_) => TypeToSubscription(_,ourModels,
                    (m)=>m.registerSettings.SubscribeToCombatStateHooks)
                );
            }
            unregisteredModsCustomSingleton.RemoveAll((s)=>registeredMods.Contains(s));
            return;
        }
    }

    [HarmonyPatch(typeof(ModelDb), nameof(ModelDb.GetCategory))]
    class CustomSingletonModelUniqueID {
        [HarmonyPrefix]
        static bool Prefix(Type type, ref string __result) {
            if(!type.IsSubclassOf(typeof(CustomSingletonModel))) return true;
            var name = type.FullName;
            name = Regex.Replace(name, "\\.[A-Za-z0-9]+$", "").Replace(".", "_");
            name += "_CustomSingletonModel";
            name = name.ToUpper();
            __result = name;
            return false;
        }
    }

    [HarmonyPatch(typeof(ModelDb), nameof(ModelDb.AllAbstractModelSubtypes),MethodType.Getter)]
    class AllAbstractModelSubtypesIgnoreCustomSingletonModel {
        [HarmonyPostfix]
        static void Postfix(ref Type[] __result) {
            var result = __result.ToList();
            result.RemoveAll((t) => t.IsSubclassOf(typeof(CustomSingletonModel)));
            __result = result.ToArray();
        }
    }
}