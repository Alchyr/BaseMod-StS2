using BaseLib.Extensions;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;

namespace BaseLib.Utils.Patching;

/// <summary>
/// Matches a consecutive list of instructions, requiring a matching opcode and operand.
/// If the operand to check for is null, it will be ignored and only the opcode will need to match.
/// </summary>
public class InstructionMatcher() : IMatcher
{
    private readonly List<InstructionMatch> _target = [];
    private readonly Dictionary<string, object?> _operandDict = [];

    public bool Match(List<string> log, List<CodeInstruction> code, int startIndex, out int matchStart, out int matchEnd)
    {
        log.Add("Starting InstructionMatcher");
        matchStart = startIndex;
        matchEnd = matchStart;
        int matchIndex = 0;
        for (int i = startIndex; i < code.Count; ++i)
        {
            InstructionMatch matchTarget = _target[matchIndex];
            CodeInstruction matchTest = code[i];
            if (matchTarget.OpcodeMatch(matchTest))
            {
                if (matchTarget.OperandMatch(matchTest))
                {
                    log.Add($"Instruction match {matchTest}");

                    if (matchTarget.StoreOperandKey != null)
                    {
                        log.Add($"Stored operand {matchTarget.StoreOperandKey}:{matchTest.operand}");
                        _operandDict[matchTarget.StoreOperandKey] = matchTest.operand;
                    }
                    
                    ++matchIndex;
                    if (matchIndex >= _target.Count)
                    {
                        matchEnd = i + 1;
                        matchStart = matchEnd - _target.Count;
                        return true;
                    }
                    continue;
                }

                log.Add($"Opcode match but operand mismatch {code[i].opcode} | [{code[i].operand?.GetType() ?? null}]{code[i].operand} vs {_target[matchIndex].Operand}");
            }

            if (matchIndex <= 0) continue;
            
            log.Add($"Match ended, opcodes do not match ({code[i].opcode}, {_target[matchIndex].Opcodes})");
            matchIndex = 0;
        }
        return false;
    }

    public override string ToString()
    {
        return "InstructionMatcher:\n" + _target.AsReadable("\n");
    }


    private class InstructionMatch
    {
        public Func<object?>? OperandFunc { get; set; } = null;
        public Predicate<object?>? OperandMatchPredicate { get; set; } = null;
        public string? StoreOperandKey { get; set; } = null;
        private readonly object? _operand;

        public InstructionMatch(OpCode opcode, object? operand = null)
        {
            Opcodes = [ opcode ];
            Operand = operand;
        }
        
        public InstructionMatch(OpCode[] opcodes)
        {
            Opcodes = opcodes;
            Operand = null;
        }

        public OpCode[] Opcodes { get; }

        public object? Operand
        {
            get => OperandFunc?.Invoke() ?? _operand;
            private init => _operand = value;
        }

        public bool OperandMatch(CodeInstruction matchTest)
        {
            return (OperandMatchPredicate?.Invoke(matchTest.operand) == true) || (Operand == null) || (Equals(ComparisonOperand(matchTest), Operand)) || Equals(matchTest.operand, Operand);
        }
        
        private object ComparisonOperand(CodeInstruction codeInstruction)
        {
            if (codeInstruction.operand is LocalBuilder localBuilder)
            {
                return localBuilder.LocalIndex;
            }
            return codeInstruction.operand;
        }

        public bool OpcodeMatch(CodeInstruction matchTest)
        {
            return Opcodes.Contains(matchTest.opcode);
        }

        public override string ToString()
        {
            return $"[{Opcodes.AsReadable()}] {(OperandMatchPredicate == null ? Operand?.ToString() : "Operand Predicate")}";
        }
    }

    public InstructionMatcher OperandFromStore(string storeKey)
    {
        if (_target.Count == 0)
            throw new InvalidOperationException("Cannot apply stored operand without adding any instructions");
        _target[^1].OperandFunc = () => _operandDict[storeKey];
        return this;
    }
    public InstructionMatcher StoreOperand(string storeKey)
    {
        if (_target.Count == 0)
            throw new InvalidOperationException("Cannot store operand without adding any instructions");
        _target[^1].StoreOperandKey = storeKey;
        return this;
    }

    public InstructionMatcher PredicateMatch(Predicate<object?> operandCondition)
    {
        if (_target.Count == 0)
            throw new InvalidOperationException("Cannot use predicate for operand without adding any instructions");
        _target[^1].OperandMatchPredicate = operandCondition;
        return this;
    }


    //Building
    //https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes.add?view=net-10.0
    public InstructionMatcher opcode(OpCode opCode)
    {
        _target.Add(new(opCode));
        return this;
    }
    public InstructionMatcher nop()
    {
        _target.Add(new(OpCodes.Nop));
        return this;
    }
    public InstructionMatcher Break()
    {
        _target.Add(new(OpCodes.Break));
        return this;
    }
    public InstructionMatcher ldarg_0()
    {
        _target.Add(new(OpCodes.Ldarg_0));
        return this;
    }
    public InstructionMatcher ldarg_1()
    {
        _target.Add(new(OpCodes.Ldarg_1));
        return this;
    }
    public InstructionMatcher ldarg_2()
    {
        _target.Add(new(OpCodes.Ldarg_2));
        return this;
    }
    public InstructionMatcher ldarg_3()
    {
        _target.Add(new(OpCodes.Ldarg_3));
        return this;
    }
    public InstructionMatcher ldloc_0()
    {
        _target.Add(new(OpCodes.Ldloc_0));
        return this;
    }
    public InstructionMatcher ldloc_1()
    {
        _target.Add(new(OpCodes.Ldloc_1));
        return this;
    }
    public InstructionMatcher ldloc_2()
    {
        _target.Add(new(OpCodes.Ldloc_2));
        return this;
    }
    public InstructionMatcher ldloc_3()
    {
        _target.Add(new(OpCodes.Ldloc_3));
        return this;
    }
    public InstructionMatcher stloc_any()
    {
        _target.Add(new InstructionMatch([
            OpCodes.Stloc,
            OpCodes.Stloc_0,
            OpCodes.Stloc_1,
            OpCodes.Stloc_2,
            OpCodes.Stloc_3,
            OpCodes.Stloc_S,
        ]));
        return this;
    }
    public InstructionMatcher stloc_0()
    {
        _target.Add(new(OpCodes.Stloc_0));
        return this;
    }
    public InstructionMatcher stloc_1()
    {
        _target.Add(new(OpCodes.Stloc_1));
        return this;
    }
    public InstructionMatcher stloc_2()
    {
        _target.Add(new(OpCodes.Stloc_2));
        return this;
    }
    public InstructionMatcher stloc_3()
    {
        _target.Add(new(OpCodes.Stloc_3));
        return this;
    }
    /*  
        Ldarg_S = 0x0e,
        Ldarga_S = 0x0f,
        Starg_S = 0x10,*/
    public InstructionMatcher ldloc_s(int index) //0x11
    {
        _target.Add(new(OpCodes.Ldloc_S, index));
        return this;
    }
    public InstructionMatcher ldloc_s()
    {
        _target.Add(new(OpCodes.Ldloc_S));
        return this;
    }
    public InstructionMatcher ldloca_s(int index) //0x12
    {
        _target.Add(new(OpCodes.Ldloca_S, index));
        return this;
    }
    public InstructionMatcher stloc_s(int index)
    {
        _target.Add(new(OpCodes.Stloc_S, index));
        return this;
    }
    public InstructionMatcher stloc_s()
    {
        _target.Add(new(OpCodes.Stloc_S));
        return this;
    }
    public InstructionMatcher ldnull()
    {
        _target.Add(new(OpCodes.Ldnull));
        return this;
    }
    public InstructionMatcher ldc_i4_m1() //-1
    {
        _target.Add(new(OpCodes.Ldc_I4_M1));
        return this;
    }
    public InstructionMatcher ldc_i4_0()
    {
        _target.Add(new(OpCodes.Ldc_I4_0));
        return this;
    }
    public InstructionMatcher ldc_i4_1()
    {
        _target.Add(new(OpCodes.Ldc_I4_1));
        return this;
    }
    public InstructionMatcher ldc_i4_2()
    {
        _target.Add(new(OpCodes.Ldc_I4_2));
        return this;
    }
    public InstructionMatcher ldc_i4_3()
    {
        _target.Add(new(OpCodes.Ldc_I4_3));
        return this;
    }
    public InstructionMatcher ldc_i4_4()
    {
        _target.Add(new(OpCodes.Ldc_I4_4));
        return this;
    }
    public InstructionMatcher ldc_i4_5()
    {
        _target.Add(new(OpCodes.Ldc_I4_5));
        return this;
    }
    public InstructionMatcher ldc_i4_6()
    {
        _target.Add(new(OpCodes.Ldc_I4_6));
        return this;
    }
    public InstructionMatcher ldc_i4_7()
    {
        _target.Add(new(OpCodes.Ldc_I4_7));
        return this;
    }
    public InstructionMatcher ldc_i4_8()
    {
        _target.Add(new(OpCodes.Ldc_I4_8));
        return this;
    }
    /*public InstructionMatcher ldc_i4_s()
    {
        target.Add(new(opcode: OpCodes.Ldc_I4_S));
        return this;
    }*/
    /*
    Ldc_I4 = 0x20,
    Ldc_I8 = 0x21,
    Ldc_R4 = 0x22,
    Ldc_R8 = 0x23,*/
    public InstructionMatcher dup()
    {
        _target.Add(new(OpCodes.Dup));
        return this;
    }
    public InstructionMatcher pop()
    {
        _target.Add(new(OpCodes.Pop));
        return this;
    }
    //Jmp = 0x27,
    public InstructionMatcher call(Type declaringType, string methodName, Type[]? parameters = null, Type[]? generics = null)
    {
        return call(AccessTools.Method(declaringType, methodName, parameters, generics));
    }
    public InstructionMatcher call(MethodInfo? method)
    {
        _target.Add(new(OpCodes.Call, method));
        return this;
    }
    /*Calli = 0x29,*/
    public InstructionMatcher ret()
    {
        _target.Add(new(OpCodes.Ret));
        return this;
    }
    public InstructionMatcher br_s(Label label)
    {
        _target.Add(new(OpCodes.Br_S, label));
        return this;
    }
    public InstructionMatcher br_s()
    {
        _target.Add(new(OpCodes.Br_S));
        return this;
    }
    public InstructionMatcher brfalse_s(Label label)
    {
        _target.Add(new(OpCodes.Brfalse_S, label));
        return this;
    }
    public InstructionMatcher brfalse_s()
    {
        _target.Add(new(OpCodes.Brfalse_S));
        return this;
    }
    public InstructionMatcher brtrue_s(Label label)
    {
        _target.Add(new(OpCodes.Brtrue_S, label));
        return this;
    }
    public InstructionMatcher brtrue_s()
    {
        _target.Add(new(OpCodes.Brtrue_S));
        return this;
    }
    public InstructionMatcher beq_s(Label label)
    {
        _target.Add(new(OpCodes.Beq_S, label));
        return this;
    }
    public InstructionMatcher beq_s()
    {
        _target.Add(new(OpCodes.Beq_S));
        return this;
    }
    /*Bge_S = 0x2f,
    Bgt_S = 0x30,
    Ble_S = 0x31,
    Blt_S = 0x32,
    Bne_Un_S = 0x33,
    Bge_Un_S = 0x34,
    Bgt_Un_S = 0x35,*/
    public InstructionMatcher ble_un_s(Label label)
    {
        _target.Add(new(OpCodes.Ble_Un_S, label));
        return this;
    }
    public InstructionMatcher ble_un_s()
    {
        _target.Add(new(OpCodes.Ble_Un_S));
        return this;
    }
    //Blt_Un_S = 0x37,
    public InstructionMatcher br(Label label) //0x38
    {
        _target.Add(new(OpCodes.Br, label));
        return this;
    }
    public InstructionMatcher br()
    {
        _target.Add(new(OpCodes.Br));
        return this;
    }
    /*Brfalse = 0x39,
    Brtrue = 0x3a,
    Beq = 0x3b,
    Bge = 0x3c,
    Bgt = 0x3d,
    Ble = 0x3e,
    Blt = 0x3f,
    Bne_Un = 0x40,
    Bge_Un = 0x41,
    Bgt_Un = 0x42,
    Ble_Un = 0x43,
    Blt_Un = 0x44,*/
    public InstructionMatcher switch_()
    {
        _target.Add(new(OpCodes.Switch));
        return this;
    }
    /*Ldind_I1 = 0x46,
    Ldind_U1 = 0x47,
    Ldind_I2 = 0x48,
    Ldind_U2 = 0x49,
    Ldind_I4 = 0x4a,
    Ldind_U4 = 0x4b,
    Ldind_I8 = 0x4c,
    Ldind_I = 0x4d,
    Ldind_R4 = 0x4e,
    Ldind_R8 = 0x4f,
    Ldind_Ref = 0x50,
    Stind_Ref = 0x51,
    Stind_I1 = 0x52,
    Stind_I2 = 0x53,
    Stind_I4 = 0x54,
    Stind_I8 = 0x55,
    Stind_R4 = 0x56,
    Stind_R8 = 0x57,*/
    public InstructionMatcher add()
    {
        _target.Add(new(OpCodes.Add));
        return this;
    }
    public InstructionMatcher sub()
    {
        _target.Add(new(OpCodes.Sub));
        return this;
    }
    public InstructionMatcher mul()
    {
        _target.Add(new(OpCodes.Mul));
        return this;
    }
    public InstructionMatcher div()
    {
        _target.Add(new(OpCodes.Div));
        return this;
    }
    /*Div_Un = 0x5c,
    Rem = 0x5d,
    Rem_Un = 0x5e,
    And = 0x5f,
    Or = 0x60,
    Xor = 0x61,
    Shl = 0x62,
    Shr = 0x63,
    Shr_Un = 0x64,
    Neg = 0x65,
    Not = 0x66,
    Conv_I1 = 0x67,
    Conv_I2 = 0x68,
    Conv_I4 = 0x69,
    Conv_I8 = 0x6a,
    Conv_R4 = 0x6b,
    Conv_R8 = 0x6c,
    Conv_U4 = 0x6d,
    Conv_U8 = 0x6e,*/
    public InstructionMatcher callvirt(Type declaringType, string methodName, Type[]? parameters = null, Type[]? generics = null)
    {
        return callvirt(AccessTools.Method(declaringType, methodName, parameters, generics));
    }
    public InstructionMatcher callvirt(MethodInfo? method)
    {
        _target.Add(new(OpCodes.Callvirt, method));
        return this;
    }
    /*Cpobj = 0x70,
    Ldobj = 0x71,
    Ldstr = 0x72,*/
    public InstructionMatcher newobj(ConstructorInfo? constructor) //0x73
    {
        _target.Add(new(OpCodes.Newobj, constructor));
        return this;
    }
    /*Castclass = 0x74,
    Isinst = 0x75,
    Conv_R_Un = 0x76,
    Unbox = 0x79,
    Throw = 0x7a,*/
    public InstructionMatcher ldfld(Type declaringType, string fieldName)
    {
        return ldfld(AccessTools.Field(declaringType, fieldName));
    }
    public InstructionMatcher ldfld(FieldInfo? field)
    {
        _target.Add(new(OpCodes.Ldfld, field));
        return this;
    }

    /*Ldflda = 0x7c,*/
    public InstructionMatcher stfld(Type declaringType, string fieldName)
    {
        return stfld(AccessTools.Field(declaringType, fieldName));
    }
    public InstructionMatcher stfld(FieldInfo? field)
    {
        _target.Add(new(OpCodes.Stfld, field));
        return this;
    }
/*Ldsfld = 0x7e,
Ldsflda = 0x7f,
Stsfld = 0x80,
Stobj = 0x81,
Conv_Ovf_I1_Un = 0x82,
Conv_Ovf_I2_Un = 0x83,
Conv_Ovf_I4_Un = 0x84,
Conv_Ovf_I8_Un = 0x85,
Conv_Ovf_U1_Un = 0x86,
Conv_Ovf_U2_Un = 0x87,
Conv_Ovf_U4_Un = 0x88,
Conv_Ovf_U8_Un = 0x89,
Conv_Ovf_I_Un = 0x8a,
Conv_Ovf_U_Un = 0x8b,
Box = 0x8c,*/
    public InstructionMatcher newarr(Type? type)
    {
        _target.Add(new(OpCodes.Newarr, type));
        return this;
    }
    /*Ldlen = 0x8e,
    Ldelema = 0x8f,
    Ldelem_I1 = 0x90,
    Ldelem_U1 = 0x91,
    Ldelem_I2 = 0x92,
    Ldelem_U2 = 0x93,
    Ldelem_I4 = 0x94,
    Ldelem_U4 = 0x95,
    Ldelem_I8 = 0x96,
    Ldelem_I = 0x97,
    Ldelem_R4 = 0x98,
    Ldelem_R8 = 0x99,
    Ldelem_Ref = 0x9a,
    Stelem_I = 0x9b,
    Stelem_I1 = 0x9c,
    Stelem_I2 = 0x9d,
    Stelem_I4 = 0x9e,
    Stelem_I8 = 0x9f,
    Stelem_R4 = 0xa0,
    Stelem_R8 = 0xa1,
    */
    public InstructionMatcher stelem_ref()
    {
        _target.Add(new(OpCodes.Stelem_Ref));
        return this;
    }
/*Ldelem = 0xa3,
Stelem = 0xa4,*/
    public InstructionMatcher unbox_any(object param) //0xa5
    {
        _target.Add(new(OpCodes.Unbox_Any, param));
        return this;
    }
    public InstructionMatcher unbox_any()
    {
        _target.Add(new(OpCodes.Unbox_Any));
        return this;
    }
/*Conv_Ovf_I1 = 0xb3,
Conv_Ovf_U1 = 0xb4,
Conv_Ovf_I2 = 0xb5,
Conv_Ovf_U2 = 0xb6,
Conv_Ovf_I4 = 0xb7,
Conv_Ovf_U4 = 0xb8,
Conv_Ovf_I8 = 0xb9,
Conv_Ovf_U8 = 0xba,
Refanyval = 0xc2,
Ckfinite = 0xc3,
Mkrefany = 0xc6,
Ldtoken = 0xd0,
Conv_U2 = 0xd1,
Conv_U1 = 0xd2,
Conv_I = 0xd3,
Conv_Ovf_I = 0xd4,
Conv_Ovf_U = 0xd5,
Add_Ovf = 0xd6,
Add_Ovf_Un = 0xd7,
Mul_Ovf = 0xd8,
Mul_Ovf_Un = 0xd9,
Sub_Ovf = 0xda,
Sub_Ovf_Un = 0xdb,
Endfinally = 0xdc,
Leave = 0xdd,
Leave_S = 0xde,
Stind_I = 0xdf,
Conv_U = 0xe0,
Prefix7 = 0xf8,
Prefix6 = 0xf9,
Prefix5 = 0xfa,
Prefix4 = 0xfb,
Prefix3 = 0xfc,
Prefix2 = 0xfd,
Prefix1 = 0xfe,
Prefixref = 0xff,
Arglist = 0xfe00,
Ceq = 0xfe01,
Cgt = 0xfe02,
Cgt_Un = 0xfe03,
Clt = 0xfe04,
Clt_Un = 0xfe05,
Ldftn = 0xfe06,
Ldvirtftn = 0xfe07,
Ldarg = 0xfe09,
Ldarga = 0xfe0a,
Starg = 0xfe0b,
Ldloc = 0xfe0c,
Ldloca = 0xfe0d,
Stloc = 0xfe0e,
Localloc = 0xfe0f,
Endfilter = 0xfe11,
Unaligned_ = 0xfe12,
Volatile_ = 0xfe13,
Tail_ = 0xfe14,
Initobj = 0xfe15,
Constrained_ = 0xfe16,
Cpblk = 0xfe17,
Initblk = 0xfe18,
Rethrow = 0xfe1a,
Sizeof = 0xfe1c,
Refanytype = 0xfe1d,
Readonly_ = 0xfe1e,*/
}
