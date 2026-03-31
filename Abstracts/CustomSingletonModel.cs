using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Exceptions;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Abstracts;

public abstract class CustomSingletonModel : SingletonModel {

    public override bool ShouldReceiveCombatHooks => registerSettings.SubscribeToCombatStateHooks;

    public abstract SingletonSettings registerSettings { get; }
    public abstract string modId { get; }
    
    

    public static void RegisterSingletonType(string modIdToRegisterWith, Type type) {
        CustomSingletonModel? singletonInstance = System.Activator.CreateInstance(type) as CustomSingletonModel;
        if (singletonInstance == null) {
            throw new TypeLoadException();
        }
        var modId = singletonInstance.modId;
        var Id = singletonInstance.Id;
        
        if(!modIdToRegisterWith.Equals(modId))return;
        BaseLibMain.Logger.Info($"Trying to register singleton:{type} with ModID:{modId}");
        
        if (ModelDb.Contains(type))
            throw new DuplicateModelException(type);
        if(modelsToRegister.Values.Any((i)=> i.Any((i2)=>i2.Equals(Id))))
            throw new DuplicateModelException(type);
        
        if (modelsToRegister.ContainsKey(modId)) {
            modelsToRegister[modId].Add(GetSingletonId(Id,type));
        } else {
            modelsToRegister.Add(modId,[GetSingletonId(Id,type)]);
        }
    }

    protected CustomSingletonModel() {
        
    }

    public static ModelId GetSingletonId(ModelId id, Type type) {
        return id;
    }
    
    private static Dictionary<string, List<ModelId>> modelsToRegister = []; 
    
    public struct SingletonSettings {
        public bool SubscribeToRunStateHooks;
        public bool SubscribeToCombatStateHooks;
    }
    public static void Subscribe(string ModID) {

        if (!modelsToRegister.ContainsKey(ModID)) {
            var classesToRegister = Assembly.GetExecutingAssembly().GetTypes().ToList()
                .FindAll((t) => t.IsSubclassOf(typeof(CustomSingletonModel)));
            foreach (var type in classesToRegister) {
                RegisterSingletonType(ModID,type);
            }
        }
        if (!modelsToRegister.ContainsKey(ModID)) return;
            
        var ourModels = modelsToRegister[ModID];
        ModHelper.SubscribeForRunStateHooks(ModID,(_) => IdToSubscription(_,ourModels,
            (m)=>m.registerSettings.SubscribeToRunStateHooks)
        );
        ModHelper.SubscribeForCombatStateHooks(ModID,(_) => IdToSubscription(_,ourModels,
            (m)=>m.registerSettings.SubscribeToCombatStateHooks)
        );
    }

    protected static IEnumerable<AbstractModel> IdToSubscription(object _, List<ModelId> ids, Func<CustomSingletonModel,bool> hook) {
        var models = ids.FindAll((id) => hook(ModelDb.GetById<CustomSingletonModel>(id)))
        .ConvertAll((id) => ModelDb.GetById<CustomSingletonModel>(id));
        return models;
    }
}