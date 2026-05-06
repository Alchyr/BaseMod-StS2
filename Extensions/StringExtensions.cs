using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using BaseLib.Utils.NodeFactories;
using Godot;
using HarmonyLib;

namespace BaseLib.Extensions;

public static class StringExtensions
{
    public static string RemovePrefix(this string id)
    {
        return id[(id.IndexOf(TypePrefix.PrefixSplitChar) + 1)..];
    }

    /// <summary>
    /// Registers a scene to be automatically converted to the specified node type when instantiated.
    /// Requires a factory to exist in NodeFactory<seealso cref="NodeFactory"/> to perform the conversion to the specified type.
    /// </summary>
    public static void RegisterSceneForConversion<TNode>(this string scenePath, Action<TNode>? postConversion = null) where TNode : Node
    {
        NodeFactory.RegisterSceneType(scenePath, postConversion);
    }

    internal static IEnumerable<CodeInstruction> MakeWriteLog(this string s)
    {
        yield return new CodeInstruction(OpCodes.Ldstr, s);
        yield return CodeInstruction.Call(typeof(StringExtensions), nameof(WriteLog));
    }

    internal static void WriteLog(string s)
    {
        BaseLibMain.Logger.Info(s);
    }
    internal static void WriteLogInt(int i)
    {
        BaseLibMain.Logger.Info(i.ToString());
    }
    internal static void WriteLogObj(object? o)
    {
        BaseLibMain.Logger.Info(o?.ToString() ?? "NULL");
    }
    
    private static readonly HashAlgorithm MD5 = System.Security.Cryptography.MD5.Create(); //Not for security, just for comparison.
    private static readonly Dictionary<string, int> HashDict = [];
    private static readonly HashSet<int> ExistingHashes = [];
    
    public static int ComputeBasicHash(this string s)
    {
        if (!HashDict.TryGetValue(s, out var hash))
        {
            var data = MD5.ComputeHash(Encoding.UTF8.GetBytes(s));
            unchecked
            {
                const int p = 16777619;
            
                hash = (int)2166136261;
                for (int i = 0; i < data.Length; i++)
                    hash = (hash ^ data[i]) * p;
                HashDict[s] = hash;
                if (ExistingHashes.Add(hash)) return hash;
                
                foreach (var entry in HashDict)
                {
                    if (entry.Value.Equals(hash))
                    {
                        BaseLibMain.Logger.Warn($"Duplicate hashes for {entry.Key} and {s}: {hash}");
                    }
                }
                return hash;
            }
        }
        return hash;
    }
}
