using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BaseLib.Extensions;
using HarmonyLib;

namespace BaseLib.Utils.Patching;

public static class AsyncMethodCall
{
    private enum ResultType
    {
        None,
        Named,
        Return,
        ReturnIf
    }

    private static readonly Dictionary<MethodBase, HashSet<string>> AddedNames = [];
    [ThreadStatic] private static Dictionary<string, object>? AsyncFields;
    private static Dictionary<string, object> GetAsyncFields {
        get
        {
            AsyncFields ??= new Dictionary<string, object>();

            return AsyncFields;
        }
    }

    private static readonly MethodInfo GetAwaiterMethod = typeof(AsyncMethodCall).Method("GetAwaiter");
    private static readonly MethodInfo StoreAwaiterMethod = typeof(AsyncMethodCall).Method("StoreAwaiter");
    private static readonly MethodInfo GetNamedMethod = typeof(AsyncMethodCall).Method("GetNamed");
    private static readonly MethodInfo StoreNamedMethod = typeof(AsyncMethodCall).Method("StoreNamed");

    private static object GetAwaiter(object keyObj, int stateIndex)
    {
        var stringKey = $"{keyObj}_{stateIndex}";
        //BaseLibMain.Logger.Info($"StringKey {stringKey}");
        GetAsyncFields.Remove(stringKey, out var result);
        //BaseLibMain.Logger.Info($"Load awaiter state {stateIndex}: {result}");
        return result!;
    }
    private static void StoreAwaiter(object awaiter, object keyObj, int stateIndex)
    {
        var stringKey = $"{keyObj}_{stateIndex}";
        //BaseLibMain.Logger.Info($"StringKey {stringKey}");
        GetAsyncFields[stringKey] = awaiter;
        //BaseLibMain.Logger.Info($"Store awaiter state {stateIndex}: {awaiter}");
    }
    private static object GetNamed(object keyObj, string name)
    {
        var stringKey = keyObj + name;
        var result = GetAsyncFields[stringKey];
        //BaseLibMain.Logger.Info($"Load awaiter val {name}: {result}");
        //TODO - clean out named fields at some point? Add removal to end of state machine
        return result;
    }
    private static void StoreNamed(object val, object keyObj, string name)
    {
        var stringKey = keyObj + name;
        GetAsyncFields[stringKey] = val;
        //BaseLibMain.Logger.Info($"Store awaiter val {name}: {val}");
    }
    
    
    /// <summary>
    /// Given the CodeInstructions of an async state machine's MoveNext method,
    /// insert an async method call into it, creating another state.
    /// beforeState or afterState must be provided, being an async method awaited by the original method (and so having its own state).
    /// The original method itself can be passed to beforeState or afterState. If so, the target position will be the first or last state.
    ///
    /// Parameters of the method will attempt to be found by name. If a parameter cannot be determined an exception will be thrown.
    /// 
    /// If resultName is provided and the method to call returns a value, it will be stored in a variable of this name using
    /// an external dictionary, and can be passed into subsequent calls by defining a parameter with this name.
    ///
    /// If resultName is "return" the result of the method will attempt to be returned immediately.
    /// If resultName is "returnIf" and the method called has a boolean return value, the state machine will return early.
    /// This will not work if the state machine method has a non-void return type.
    /// 
    /// If resultName is the same as one of the method's parameters, it will be attempted to store the result in the variable
    /// passed to that parameter.
    /// If this does not match the correct return type, an exception will be thrown when patching.
    ///
    /// </summary>
    /// <param name="generator"></param>
    /// <param name="code"></param>
    /// <param name="original">The method being patched, provided to transpiler patches.</param>
    /// <param name="callMethod">A method that returns a Task that will be called.</param>
    /// <param name="beforeState"></param>
    /// <param name="afterState"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static List<CodeInstruction> Create(ILGenerator generator, IEnumerable<CodeInstruction> code, MethodBase original, MethodInfo callMethod, MethodBase? beforeState = null, MethodBase? afterState = null, string? resultName = null)
    {
        if (beforeState == null && afterState == null)
        {
            throw new ArgumentException(
                "Only one of beforeState or afterState should be provided to determine where to insert the async method call.");
        }

        var targetMethod = beforeState ?? afterState ?? throw new ArgumentException(
            "Either beforeState or afterState must be provided to determine where to insert the async method call.");
        var before = beforeState != null;

        if (!original.Name.Equals("MoveNext"))
        {
            throw new ArgumentException("Target method of AsyncMethodCall should be MoveNext of async state machine");
        }
        
        // Check if method to call is even an async method.
        if (!callMethod.ReturnType.IsAssignableTo(typeof(Task)))
        {
            throw new ArgumentException("Method to call must return a Task");
        }

        if (!callMethod.IsStatic)
        {
            throw new ArgumentException("Method to call must be static");
        }

        var stateMachineType = original.DeclaringType ??
                               throw new ArgumentException(
                                   $"Failed to get state machine type from method '{original.FullDescription()}'");
        
        BaseLibMain.Logger.Info($"Patching StateMachineType: {stateMachineType.FullName}");

        var stateField = stateMachineType.FindStateMachineField("state");
        var builderField = stateMachineType.FindStateMachineField("t__builder");

        var codeList = code.ToList();
        
        int index = 0;
        
        List<CodeInstruction> loadStateSection = new();
        while (index < codeList.Count)
        {
            if (codeList[index].HasBlock(ExceptionBlockType.BeginExceptionBlock))
            {
                break;
            }
            loadStateSection.Add(codeList[index]);
            ++index;
        }
        
        List<CodeInstruction> branchSection = new();
        
        //First, analyze initial branching to determine starts of each state branch.
        //If branch is conditional, determine required state. If conditional branch destination is an unconditional branch, update that state's position.
        Dictionary<int, Label> stateLabels = [];
        int? checkState = null;
        
        bool exitLoop = false;
        
        while (index < codeList.Count && !exitLoop)
        {
            var instruction = codeList[index];
            if (instruction.opcode == OpCodes.Ldloc_0)
            {
                branchSection.Add(instruction);
            }
            else if (instruction.opcode == OpCodes.Switch)
            {
                var labelArr = (Label[]) instruction.operand;
                for (int i = 0; i < labelArr.Length; ++i)
                {
                    stateLabels[i] = labelArr[i];
                }
                branchSection.Add(instruction);
                ++index;
                break;
            }
            else if (instruction.TryGetIntValue(out var loadedConst))
            {
                checkState = loadedConst;
                branchSection.Add(instruction);
            }
            else
            {
                switch (instruction.opcode.Value)
                {
                    case (int) OpCodeValues.Brfalse_S:
                    case (int) OpCodeValues.Brfalse: //Branch if current state == 0
                        stateLabels[0] = (Label)instruction.operand;
                        //BaseLibMain.Logger.Info($"State 0 dest {stateLabels[0].Id}");
                        break;
                    case (int) OpCodeValues.Brtrue_S:
                    case (int) OpCodeValues.Brtrue: //What
                        BaseLibMain.Logger.Warn("Unexpected Brtrue in jump section of state machine");
                        break;
                    case (int) OpCodeValues.Beq_S:
                    case (int) OpCodeValues.Beq: //Branch if current state == ?
                        if (checkState == null)
                        {
                            BaseLibMain.Logger.Warn("Failed to evaluate beq, checkState null");
                            break;
                        }
                        stateLabels[checkState.Value] = (Label)instruction.operand;
                        //BaseLibMain.Logger.Info($"State {checkState.Value} dest {stateLabels[checkState.Value].Id}");
                        break;
                    case (int) OpCodeValues.Br_S:
                    case (int) OpCodeValues.Br: //Unconditional branch. State -1 or intermediate jump.
                        var opLabel = (Label)instruction.operand;
                        foreach (var entry in stateLabels)
                        {
                            foreach (var label in instruction.labels)
                            {
                                if (entry.Value == label)
                                {
                                    stateLabels[entry.Key] = opLabel;
                                    //BaseLibMain.Logger.Info($"State {entry.Key} dest {stateLabels[entry.Key].Id}");
                                    break;
                                }
                            }
                        }
                        break;
                    default:
                        //BaseLibMain.Logger.Info($"Found end of branching section");
                        exitLoop = true;

                        if (instruction.opcode == OpCodes.Nop)
                        {
                            branchSection.Add(instruction);
                            ++index;
                        }
                        
                        break;
                }

                if (!exitLoop)
                    branchSection.Add(instruction);
                else
                    break;
            }

            ++index;
        }

        Dictionary<Label, int> labelStates = [];
        foreach (var entry in stateLabels)
        {
            labelStates[entry.Value] = entry.Key;
            //BaseLibMain.Logger.Info($"State {entry.Key} dest label {entry.Value.Id}");
        }
        
        //Finished checking branching section; check states
        List<CodeInstruction>? betweenSection = null;
        List<StateInfo> states = [];
        HashSet<Label> leaveLabels = [];
        int newStateIndex = states.Count;
        
        int currentState = -3;
        List<CodeInstruction> stateSection = [];
        bool endingState = false; //Set to true upon reaching GetResult. Next instruction is included in state if it is stloc, nop, or pop
        
        while (index < codeList.Count)
        {
            var instruction = codeList[index];
            foreach (var label in instruction.labels)
            {
                if (labelStates.TryGetValue(label, out var state))
                {
                    //BaseLibMain.Logger.Info($"Found state resume point label {label.Id} for state {state}");
                    currentState = state;
                }
            }

            if (instruction.opcode == OpCodes.Leave || instruction.opcode == OpCodes.Leave_S)
            {
                if (instruction.operand is Label label)
                {
                    leaveLabels.Add(label);
                }
            }
            
            //Check for ending of state
            if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo { Name: "GetResult" } methodInfo && (methodInfo.DeclaringType?.Name.StartsWith("TaskAwaiter") ?? false))
            {
                endingState = true;
                stateSection.Add(instruction);
            }
            else if (endingState)
            {
                endingState = false;
                switch (currentState)
                {
                    case -3:
                        BaseLibMain.Logger.Warn("Found code between branching section and states");
                        betweenSection = stateSection;
                        break;
                    case -2:
                        throw new Exception("Failed to find index of state");
                    default:
                        if (instruction.IsStloc() || instruction.opcode == OpCodes.Nop || instruction.opcode == OpCodes.Pop)
                        {
                            stateSection.Add(instruction);
                        }
                        else
                        {
                            --index; //Use this instruction as start of next state.
                        }
                        var nextState = new StateInfo(currentState, stateSection, stateField);
                        states.Add(nextState);
                        if (nextState.Index >= newStateIndex)
                            newStateIndex = nextState.Index + 1;
                        break;
                }

                stateSection = [];
                currentState = -2;
            }
            else
            {
                stateSection.Add(instruction);
            }

            ++index;
        }
        
        //Remaining code is "ending" of MoveNext
        var endingSection = stateSection;
        //BaseLibMain.Logger.Info($"Found {leaveLabels.Count} labels for leave instructions [{leaveLabels.Join(label => label.Id.ToString())}]");
        
        if (states.Count == 0)
            throw new Exception($"Failed to find any states for async method {original.Name}");

        //Find target state
        StateInfo? targetState = null;
        if (targetMethod == original)
        {
            targetState = before ? states[0] : states[^1];
            targetMethod = targetState.StateMethod;
        }
        else
        {
            foreach (var state in states)
            {
                if (state.StateMethod == targetMethod)
                {
                    targetState = state;
                    break;
                }
            }
        }

        if (targetState == null)
            throw new ArgumentException($"Unable to find state for target method {targetMethod?.Name}");
        
        //Analyze ending section
        Type? returnType = null;
        Label retLabel = default, retValLabel = default;
        foreach (var ci in endingSection)
        {
            foreach (var label in ci.labels)
            {
                if (leaveLabels.Remove(label))
                {
                    if (ci.opcode == OpCodes.Ret)
                    {
                        retLabel = label;
                    }
                    else
                    {
                        retValLabel = label;
                    }
                }
            }
            
            if (returnType != null || ci.opcode != OpCodes.Call || ci.operand is not MethodInfo { Name: "SetResult" } info) continue;
            
            var declaringType = info.DeclaringType;
            if (declaringType == null) continue;

            if (declaringType == typeof(AsyncTaskMethodBuilder)
                || declaringType == typeof(AsyncValueTaskMethodBuilder))
            {
                continue;
            }
            if (declaringType.IsConstructedGenericType 
                && (declaringType.GetGenericTypeDefinition() == typeof(AsyncTaskMethodBuilder<>)
                    || declaringType.GetGenericTypeDefinition() == typeof(AsyncValueTaskMethodBuilder<>)))
            {
                returnType = declaringType.GenericTypeArguments[0];
            }
        }
        
        //BaseLibMain.Logger.Info($"Return label: {retLabel.Id}");
        //BaseLibMain.Logger.Info($"Return value label: {retValLabel.Id}");
        
        //Look for fields that match target method parameter names
        var methodCallParams = callMethod.GetParameters().Select(param => MakeStateParameter(original, stateMachineType, param)).ToList();
        var resultType = resultName?.ToLowerInvariant() == "return" ? ResultType.Return : 
            resultName?.ToLowerInvariant() == "returnif" ? ResultType.ReturnIf : 
            resultName != null ? ResultType.Named : ResultType.None;

        switch (resultType)
        {
            case ResultType.Return:
                if (returnType != null)
                {
                    if (!callMethod.ReturnType.IsGenericType)
                    {
                        throw new ArgumentException($"resultName set to return patching method with return type {returnType} but method to call does not return a value; return type {callMethod.ReturnType}");
                    }
                
                    if (!callMethod.ReturnType.GenericTypeArguments[0].IsAssignableTo(returnType))
                    {
                        throw new ArgumentException(
                            $"Cannot assign result of type {callMethod.ReturnType.GenericTypeArguments[0]} to return type {returnType}");
                    }
                }
                break;
            case ResultType.ReturnIf:
                if (!callMethod.ReturnType.IsGenericType)
                {
                    throw new ArgumentException("resultName set to returnIf but method to call does not return a value; requires bool");
                }
                
                if (!callMethod.ReturnType.GenericTypeArguments[0].IsAssignableTo(typeof(bool)))
                {
                    throw new ArgumentException(
                        $"Result  {callMethod.ReturnType.GenericTypeArguments[0]} to return type {returnType}");
                }
                break;
            case ResultType.Named:
                if (!callMethod.ReturnType.IsGenericType)
                {
                    throw new ArgumentException($"resultName set but method to call does not return a value");
                }

                bool storeExisting = false;
                foreach (var info in methodCallParams)
                {
                    if (info.Parameter.Name == resultName)
                    {
                        storeExisting = true;
                        break;
                    }
                }

                if (!storeExisting) //New named result
                {
                    if (!AddedNames.TryGetValue(original, out var dict))
                    {
                        dict = [];
                        AddedNames[original] = dict;
                    }

                    dict.Add(resultName!);
                }
                break;
        }
        
        BaseLibMain.Logger.Info($"Adding new state {newStateIndex} for method {callMethod.DeclaringType?.Name ?? "???"}.{callMethod.Name} {(before ? "before" : "after")} {targetMethod?.Name ?? targetState.Index.ToString()} with result type {resultType} ({resultName})");
        
        //Generate label and branch instruction
        var loadStateLabel = generator.DefineLabel();
        
        branchSection = [
            // .."Start of branch section".MakeWriteLog(),
            CodeInstruction.LoadLocal(0).WithBlocks(branchSection[0].ExtractBlocks()),
            newStateIndex.LoadConstant(),
            new CodeInstruction(OpCodes.Beq, loadStateLabel),
            ..branchSection
        ];

        //Insert new state
        targetState.Insert(before, newStateIndex, callMethod, methodCallParams, 
            stateMachineType, stateField, builderField, 
            loadStateLabel, retLabel, retValLabel, resultType, resultName, generator);
        
        //Generate combined result
        var instructions = new List<CodeInstruction>();
        instructions.AddRange(loadStateSection);
        instructions.AddRange(branchSection);
        if (betweenSection != null) instructions.AddRange(betweenSection);
        foreach (var state in states)
        {
            instructions.AddRange(state.Code);
        }
        instructions.AddRange(endingSection);
        
        //BaseLibMain.Logger.Info($"CODE:\n{instructions.Join(instruction => instruction.ToString(), "\n")}");
        return instructions;
    }

    private static StateParamInfo MakeStateParameter(MethodBase method, Type stateMachineType, ParameterInfo param)
    {
        if (param.Name == null) throw new Exception("Unable to determine parameter name for method to call for async method call");

        Action<List<CodeInstruction>>? addLoadInstructions;
        Action<List<CodeInstruction>>? addStoreInstructions;
        
        if (param.Name == "__instance")
        {
            if (method.IsStatic)
                throw new ArgumentException("Unable to use __instance parameter when patching static method");
            var thisField = stateMachineType.FindStateMachineField("__this");

            addLoadInstructions = list =>
            {
                list.Add(CodeInstruction.LoadArgument(0));
                list.Add(thisField.Ldfld());
            };
            addStoreInstructions = list =>
            {
                list.Add(new CodeInstruction(OpCodes.Pop));
            };
        }
        else if (AddedNames.TryGetValue(method, out var dict) && dict.Contains(param.Name))
        {
            BaseLibMain.Logger.Info($"Using named result {param.Name} in method {method.Name}");
            addLoadInstructions = list =>
            {
                list.AddRange(stateMachineType.BoxArg0());
                list.Add(new CodeInstruction(OpCodes.Ldstr, param.Name));
                list.Add(GetNamedMethod.Call());
                if (param.ParameterType.IsValueType)
                {
                    list.Add(new CodeInstruction(OpCodes.Unbox_Any, param.ParameterType));
                }
            };
            addStoreInstructions = list =>
            {
                if (param.ParameterType.IsValueType)
                {
                    list.Add(new CodeInstruction(OpCodes.Box, param.ParameterType));
                }
                list.AddRange(stateMachineType.BoxArg0());
                list.Add(new CodeInstruction(OpCodes.Ldstr, param.Name));
                list.Add(StoreNamedMethod.Call());
            };
        }
        else
        {
            var field = stateMachineType.FindStateMachineField(param.Name);
            if (!field.FieldType.IsAssignableTo(param.ParameterType))
            {
                throw new ArgumentException(
                    $"Unable to pass field {field.Name} of type {field.FieldType} to parameter {param.Name} of type {param.ParameterType}");
            }
            
            addLoadInstructions = list =>
            {
                list.Add(CodeInstruction.LoadArgument(0));
                list.Add(field.Ldfld());
            };
            addStoreInstructions = list =>
            {
                //Last two instructions of list should be get awaiter and get result
                list.Insert(list.Count - 2, CodeInstruction.LoadArgument(0));
                list.Add(field.Stfld());
            };
        }
        
        return new StateParamInfo(param, addLoadInstructions, addStoreInstructions);
    }
    

    private static readonly InstructionMatcher StateAwaitMatcher = new InstructionMatcher()
        .any().PredicateMatch(arg =>
        {
            switch (arg)
            {
                case MethodInfo method
                    when method.ReturnType.IsAssignableTo(typeof(Task)):
                case FieldInfo field
                    when field.FieldType.IsAssignableTo(typeof(Task)):
                    return true;
                default:
                    return false;
            }
        })
        .callvirt(null).PredicateMatch(arg => arg is MethodInfo { Name: "GetAwaiter" });

    private record StateParamInfo(
        ParameterInfo Parameter,
        Action<List<CodeInstruction>> AddLoadInstructions,
        Action<List<CodeInstruction>> AddStoreInstructions);

    private class StateInfo //, MethodBase stateMethod, int indexLoadIndex)
    {
        public int Index { get; }
        public List<CodeInstruction> Code { get; private set; }
        public MethodInfo? StateMethod { get; }
        
        public StateInfo(int index, List<CodeInstruction> code, FieldInfo stateField)
        {
            Index = index;
            Code = code;

            StateMethod = AnalyzeCode(stateField, Index, Code);
        }

        private static MethodInfo? AnalyzeCode(FieldInfo stateField, int stateIndex, List<CodeInstruction> code)
        {
            var storeStateMatcher = new InstructionMatcher().stfld(stateField);

            InstructionPatcher reader = new(code);
        
            bool[] matched = [true];
            reader.Match(_ => matched[0] = false, StateAwaitMatcher);
            if (!matched[0])
            {
                BaseLibMain.Logger.Info($"CODE:\n{code.Join(instruction => instruction.ToString(), "\n")}");
                throw new InvalidOperationException($"Failed to find state awaiter for state {stateIndex}");
            }

            reader
                .Step(-2).GetOperand(out var operand) //Get method called to get awaited task
                .Step(3)
                .Match(storeStateMatcher); //Move to next state store (occurs when awaited task does not end immediately)
            
            return operand as MethodInfo;
        }

        public void Insert(bool before, int newStateIndex, MethodInfo callMethod, IEnumerable<StateParamInfo> loadFields,
            Type stateMachineType, FieldInfo stateField, FieldInfo builderField,
            Label loadStateLabel, Label retLabel, Label retValLabel, ResultType resultType, string? resultName,
            ILGenerator generator)
        {
            var awaiterType = typeof(TaskAwaiter);
            var taskMethodBuilderType = builderField.FieldType;
            var valueTypeMachine = stateMachineType.IsValueType;

            var returnType = typeof(void);
            if (callMethod.ReturnType.IsGenericType)
            {
                returnType = callMethod.ReturnType.GetGenericArguments()[0];
                BaseLibMain.Logger.Info($"Method to call has return type; making generic awaiter type [{returnType}]");
                awaiterType = typeof(TaskAwaiter<>).MakeGenericType(returnType);
            }
            
            var taskGetAwaiter = callMethod.ReturnType.GetMethod("GetAwaiter");
            if (taskGetAwaiter == null)
                throw new Exception($"Failed to get GetAwaiter for type {callMethod.ReturnType}");

            var awaitUnsafe = taskMethodBuilderType.GetMethod("AwaitUnsafeOnCompleted");
            if (awaitUnsafe == null)
                throw new Exception($"Failed to get AwaitUnsafeOnCompleted for type {taskMethodBuilderType}");
            if (awaitUnsafe.IsGenericMethodDefinition)
            {
                awaitUnsafe = awaitUnsafe.MakeGenericMethod(awaiterType, stateMachineType);
            }
            
            var taskAwaiter = generator.DeclareLocal(awaiterType);
            var isCompleted = awaiterType.PropertyGetter("IsCompleted");
            
            var endingSectionLabel = generator.DefineLabel();
            
            //Initial method call
            List<CodeInstruction> newCode = [];
            StateParamInfo? resultParam = null;

            foreach (var loadField in loadFields) //Load fields as parameters for method
            {
                if (resultType == ResultType.Named && loadField.Parameter.Name == resultName)
                {
                    resultParam = loadField;
                }
                loadField.AddLoadInstructions(newCode);
            }

            if (resultParam != null)
            {
                //Validate result param type
                if (!returnType.IsAssignableTo(resultParam.Parameter.ParameterType))
                {
                    throw new ArgumentException(
                        $"Cannot store method result of type {returnType} to parameter {resultParam.Parameter.Name} of type {resultParam.Parameter.ParameterType}");
                }
            }
            
            //newCode.AddRange("Loaded Fields".MakeWriteLog());
            newCode.Add(new CodeInstruction(OpCodes.Call, callMethod));
            newCode.Add(taskGetAwaiter.CallVirt());
            //newCode.AddRange("Store awaiter".MakeWriteLog());
            newCode.Add(CodeInstruction.StoreLocal(taskAwaiter.LocalIndex));
            newCode.Add(CodeInstruction.LoadLocal(taskAwaiter.LocalIndex, true));
            newCode.Add(new CodeInstruction(OpCodes.Call, isCompleted));
            newCode.Add(new CodeInstruction(OpCodes.Brtrue, endingSectionLabel)); //if already complete, skip to end
            
            //await block
            //newCode.AddRange("Await Block".MakeWriteLog());
            newCode.Add(CodeInstruction.LoadArgument(0)); //load "this" for stfld
            newCode.Add(newStateIndex.LoadConstant());
            newCode.Add(new CodeInstruction(OpCodes.Dup));
            newCode.Add(CodeInstruction.StoreLocal(0)); //Store state in local 0
            newCode.Add(stateField.Stfld()); //and in state field
            
            newCode.Add(CodeInstruction.LoadLocal(taskAwaiter.LocalIndex)); //load awaiter and store in external dict
            newCode.Add(new CodeInstruction(OpCodes.Box, awaiterType));
            newCode.AddRange(stateMachineType.BoxArg0()); //Use state machine object as key
            newCode.Add(newStateIndex.LoadConstant()); //state index
            newCode.Add(StoreAwaiterMethod.Call());
            
            newCode.Add(CodeInstruction.LoadArgument(0)); //Get builder and AwaitUnsafeOnCompleted
            newCode.Add(new CodeInstruction(OpCodes.Ldflda, builderField));
            newCode.Add(CodeInstruction.LoadLocal(taskAwaiter.LocalIndex, true));
            newCode.Add(CodeInstruction.LoadArgument(0, !valueTypeMachine)); //Need to treat valuetype state machine differently or state is lost on await
            //newCode.AddRange("Awaiting".MakeWriteLog());
            newCode.Add(awaitUnsafe.Call());
            newCode.Add(new CodeInstruction(OpCodes.Leave, retLabel));
            
            //Section 3 - restore state
            newCode.Add(new CodeInstruction(OpCodes.Nop).WithLabels(loadStateLabel));
            //newCode.AddRange("Load State".MakeWriteLog());
            newCode.AddRange(stateMachineType.BoxArg0()); //Get awaiter from dict
            newCode.Add(newStateIndex.LoadConstant());
            newCode.Add(GetAwaiterMethod.Call());
            //newCode.AddRange("Got Awaiter".MakeWriteLog());
            newCode.Add(new CodeInstruction(OpCodes.Unbox_Any, awaiterType));
            newCode.Add(CodeInstruction.StoreLocal(taskAwaiter.LocalIndex)); //Store in local
            //Code to reset field, not necessary due to external store
            //newCode.Add(CodeInstruction.LoadLocal(taskAwaiter.LocalIndex, true)); //Load as address from local
            //newCode.Add(new CodeInstruction(OpCodes.Initobj, awaiterType)); //Init with address
            //newCode.AddRange("Initialized Awaiter".MakeWriteLog());
            
            newCode.Add(CodeInstruction.LoadArgument(0)); //Set state to -1
            newCode.Add((-1).LoadConstant());
            newCode.Add(new CodeInstruction(OpCodes.Dup));
            newCode.Add(CodeInstruction.StoreLocal(0));
            newCode.Add(stateField.Stfld());
            //newCode.AddRange("Set state to -1".MakeWriteLog());
            
            //Section 4 - get result
            newCode.Add(new CodeInstruction(OpCodes.Nop).WithLabels(endingSectionLabel));
            newCode.Add(CodeInstruction.LoadLocal(taskAwaiter.LocalIndex, true));
            //newCode.AddRange("Loaded awaiter local address".MakeWriteLog());
            newCode.Add(CodeInstruction.Call(awaiterType, "GetResult"));
            //newCode.AddRange("Got result".MakeWriteLog());

            //Can do 3 things with result:
            //Store (in resultParam or by name)
            //Return
            //Ignore
            switch (resultType)
            {
                case ResultType.Return:
                    newCode.Add(returnType == typeof(void)
                        ? new CodeInstruction(OpCodes.Nop)
                        : CodeInstruction.StoreLocal(1));
                    newCode.Add(new CodeInstruction(OpCodes.Leave, retValLabel));
                    break;
                case ResultType.ReturnIf:
                    //Currently a bool on stack.
                    Label skipLeaveLabel = generator.DefineLabel();
                    newCode.Add(new CodeInstruction(OpCodes.Brfalse_S, skipLeaveLabel));
                    newCode.Add(new CodeInstruction(OpCodes.Leave, retValLabel));
                    newCode.Add(new CodeInstruction(OpCodes.Nop).WithLabels(skipLeaveLabel));
                    break;
                case ResultType.Named:
                    if (resultParam != null)
                    {
                        resultParam.AddStoreInstructions(newCode);
                    }
                    else if (resultName != null)
                    {
                        if (returnType.IsValueType)
                        {
                            newCode.Add(new CodeInstruction(OpCodes.Box, returnType));
                        }
                        newCode.AddRange(stateMachineType.BoxArg0());
                        newCode.Add(new CodeInstruction(OpCodes.Ldstr, resultName));
                        newCode.Add(StoreNamedMethod.Call());
                    }
                    break;
                default:
                    //Don't need to keep result.
                    newCode.Add(new CodeInstruction(returnType == typeof(void) ? OpCodes.Nop : OpCodes.Pop));
                    break;
            }

            if (before)
            {
                Code = [
                    ..newCode,
                    ..Code
                ];
            }
            else
            {
                Code = [
                    ..Code,
                    ..newCode
                ];
            }
        }
    }
}