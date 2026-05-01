using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BaseLib.Abstracts;
using BaseLib.Extensions;
using BaseLib.Patches.Localization;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;

namespace BaseLib.Patches.Content;

/// <summary>
/// Marks a field as intended to contain a new generated enum value.
/// Certain types of enums have additional functionality. Currently: CardKeyword, PileType
/// </summary>
/// <param name="name">This is relevant only if the field is intended to be a keyword. If not supplied, field name will be used.</param>
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class CustomEnumAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}

/// <summary>
/// Marks a CardKeyword field as having additional keyword properties. This is not required to create a keyword,
/// only if you want to use the additional features added by this.
/// </summary>
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class KeywordPropertiesAttribute : Attribute
{
    /// <summary>
    /// Marks a CardKeyword field as having additional keyword properties. This is not required to create a keyword,
    /// only if you want to use the additional features added by this.
    /// </summary>
    /// <param name="position">The keyword's localized title will automatically be added to the specified position in the card's description for cards with the keyword.</param>
    public KeywordPropertiesAttribute(AutoKeywordPosition position) : this(position, true)
    {
    }
    
    /// <summary>
    /// Marks a CardKeyword field as having additional keyword properties. This is not required to create a keyword,
    /// only if you want to use the additional features added by this.
    /// </summary>
    /// <param name="position">The keyword's localized title will automatically be added to the specified position in the card's description for cards with the keyword.</param>
    /// <param name="richKeyword">Enables energy icons, ?, and ? in the keyword's tooltip.</param>
    public KeywordPropertiesAttribute(AutoKeywordPosition position, bool richKeyword)
    {
        Position = position;
        RichKeyword = richKeyword;
    }

    public AutoKeywordPosition Position { get; }
    public bool RichKeyword { get; }
}

public enum AutoKeywordPosition
{
    None,
    Before,
    After
}

public static class CustomKeywords
{
    public static readonly Dictionary<int, KeywordInfo> KeywordIDs = [];

    public readonly struct KeywordInfo(string key)
    {
        public readonly string Key = key;
        public required AutoKeywordPosition AutoPosition { get; init; }
        public required bool RichKeyword { get; init; }
        
        public static implicit operator string(KeywordInfo info) => info.Key;
    }

    //Support auto-text application through a patch in CardModel.GetDescriptionForPile, CardKeywordOrder
}

public static class CustomEnums
{
    private static readonly HashAlgorithm MD5 = System.Security.Cryptography.MD5.Create(); //Not for security, just for comparison.
    private static readonly Dictionary<string, int> HashDict = [];
    private static readonly HashSet<int> ExistingHashes = [];
    private static readonly Dictionary<Type, KeyGenerator> KeyGenerators = [];
    
    public static T GenerateKey<T>(string @namespace, string name) where T : Enum
    {
        return (T)GenerateKey(typeof(T), @namespace, name);
    }
    
    public static object GenerateKey(FieldInfo field)
    {
        return GenerateKey(field.FieldType, field.DeclaringType!.GetRootNamespace(), field.Name);
    }
    
    public static object GenerateKey(Type enumType, string @namespace, string name)
    {
        if (!KeyGenerators.TryGetValue(enumType, out var generator))
        {
            KeyGenerators.Add(enumType, generator = new(enumType));
        }
    
        return generator.GetKey(ComputeBasicHash(@namespace), ComputeBasicHash(name));
    }

    private static int ComputeBasicHash(string s)
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
                        BaseLibMain.Logger.Warn($"Duplicate mod hash for {entry.Key} and {s}: {hash}");
                    }
                }
                return hash;
            }
        }
        return hash;
    }
    
    private class KeyGenerator
    {
        private static readonly Dictionary<Type, Func<object, object>> Incrementers = new()
        {
            { typeof(byte), (val) => ((byte)val) + 1 },
            { typeof(sbyte), (val) => ((sbyte)val) + 1 },
            { typeof(short), (val) => ((short)val) + 1 },
            { typeof(ushort), (val) => ((ushort)val) + 1 },
            { typeof(int), (val) => ((int)val) + 1 },
            { typeof(uint), (val) => ((uint)val) + 1 },
            { typeof(long), (val) => ((long)val) + 1 },
            { typeof(ulong), (val) => ((ulong)val) + 1 }
        };

        private static readonly Dictionary<Type, Func<object, object>> FlagIncrementers = new()
        {
            { typeof(byte), FlagIncrementer<byte>() },
            { typeof(sbyte), FlagIncrementer<sbyte>() },
            { typeof(short), FlagIncrementer<short>() },
            { typeof(ushort), FlagIncrementer<ushort>() },
            { typeof(int), FlagIncrementer<int>() },
            { typeof(uint), FlagIncrementer<uint>() },
            { typeof(long), FlagIncrementer<long>() },
            { typeof(ulong), FlagIncrementer<ulong>() },
        };

        private static readonly Dictionary<Type, int> TypeHalfSizes = new()
        {
            { typeof(byte), sizeof(byte) * 4 },
            { typeof(sbyte), sizeof(sbyte) * 4 },
            { typeof(short), sizeof(short) * 4 },
            { typeof(ushort), sizeof(ushort) * 4 },
            { typeof(int), sizeof(int) * 4 },
            { typeof(uint), sizeof(uint) * 4 },
            { typeof(long), sizeof(long) * 4 },
            { typeof(ulong), sizeof(ulong) * 4 },
        };

        private static Func<object, object> FlagIncrementer<T>() where T : struct, IBinaryInteger<T>
        {
            return val =>
            {
                var v = (T)val;
                var r = T.One;
                while (r <= v && r != T.Zero) r <<= 1;
                return r;
            };
        }

        private Type _underlyingType;
        private object _nextKey;
        private bool _isFlag;
        private int _halfBits;
        private readonly Func<object, object> _increment;

        private HashSet<object> _values = [];

        public KeyGenerator(Type t)
        {
            if (!t.IsEnum)
            {
                _increment = o => o;
                throw new ArgumentException("Attempted to construct KeyGenerator with non-enum type " + t.FullName);
            }
            
            _isFlag = t.GetCustomAttribute<FlagsAttribute>() != null;
            var values = t.GetEnumValuesAsUnderlyingType();
            _underlyingType = Enum.GetUnderlyingType(t);

            _nextKey = Convert.ChangeType(0, _underlyingType);
            
            _increment = _isFlag ? FlagIncrementers[_underlyingType] : Incrementers[_underlyingType];
            _halfBits = TypeHalfSizes[_underlyingType];

            if (values.Length > 0)
            {
                foreach (var v in values)
                {
                    _values.Add(v);
                    if (((IComparable)v).CompareTo(_nextKey) >= 0)
                    {
                        _nextKey = _increment(v);
                    }
                }
            }
            
            BaseLibMain.Logger.Info($"Generated KeyGenerator for enum {t.FullName} with starting value {_nextKey} | IsFlag: {_isFlag} | Half-Size: {_halfBits}");
        }

        public object GetKey(int namespaceHash, int nameHash)
        {
            if (_isFlag)
            {
                var returnVal = _nextKey;
                _nextKey = _increment(_nextKey);
                return returnVal;
            }

            int upper = namespaceHash & ((1 << _halfBits) - 1),
                lower = nameHash & ((1 << _halfBits) - 1);
            
            /*BaseLibMain.Logger.Info($"{Convert.ToString(namespaceHash, 2)} | {Convert.ToString(nameHash, 2)}");
            BaseLibMain.Logger.Info($"{Convert.ToString(upper, 2)} | {Convert.ToString(lower, 2)}");*/

            var result = (upper << _halfBits) | lower;
            _nextKey = Convert.ChangeType(result, _underlyingType);
            while (_values.Contains(_nextKey)) _nextKey = _increment(_nextKey);
            _values.Add(_nextKey);
            return _nextKey;
        }
    }
}


[HarmonyPatch(typeof(CardKeywordExtensions), nameof(CardKeywordExtensions.GetLocKeyPrefix))]
class GetCustomLocKey
{
    [HarmonyPrefix]
    static bool UseCustomKeywordMap(CardKeyword keyword, ref string? __result)
    {
        if (!CustomKeywords.KeywordIDs.TryGetValue((int)keyword, out var keywordInfo)) return true;
        
        __result = keywordInfo.Key;
        return false;
    }
}

/// <summary>
/// Generates and assigns values to fields marked with the CustomEnum attribute,
/// and also performs some special logic for certain types of enums, like keywords and piletypes.
/// </summary>
[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.Init))]
class GenEnumValues
{
    [HarmonyPrefix]
    static void FindAndGenerate()
    {
        List<FieldInfo> customEnumFields = [];
        foreach (var t in ReflectionHelper.ModTypes)
        {
            var fields = t.GetFields().Where(field => Attribute.IsDefined(field, typeof(CustomEnumAttribute)));

            foreach (var field in fields)
            {
                if (!field.FieldType.IsEnum)
                {
                    throw new Exception(
                        $"Field {field.DeclaringType?.FullName}.{field.Name} should be an enum type for CustomEnum");
                }

                if (!field.IsStatic)
                {
                    throw new Exception(
                        $"Field {field.DeclaringType?.FullName}.{field.Name} should be static for CustomEnum");
                }

                if (field.DeclaringType == null)
                {
                    continue;
                }

                customEnumFields.Add(field);
            }
        }
        
        customEnumFields.Sort((a, b) =>
        {
            var comparison = string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            return comparison != 0 ? comparison : string.Compare(a.DeclaringType?.Name, b.DeclaringType?.Name, StringComparison.Ordinal);
        });

        foreach (var field in customEnumFields)
        {
            var keywordInfo = field.GetCustomAttribute<CustomEnumAttribute>();
            var key = CustomEnums.GenerateKey(field);
            var t = field.DeclaringType;
            if (t == null) continue;
            
            //BaseLibMain.Logger.Info($"Generated value {Convert.ToString((long)Convert.ChangeType(key, TypeCode.Int64), 2)} for field {field.Name} of enum {t.FullName}");
            field.SetValue(null, key);

            if (field.FieldType == typeof(CardKeyword))
            {
                var keywordId = t.GetPrefix() + (keywordInfo?.Name ?? field.Name).ToUpperInvariant();
                var poolAttribute = field.GetCustomAttribute<KeywordPropertiesAttribute>();
                var autoPosition = poolAttribute?.Position ?? AutoKeywordPosition.None;

                switch (autoPosition)
                {
                    case AutoKeywordPosition.Before:
                        AutoKeywordText.AdditionalBeforeKeywords.Add((CardKeyword) key);
                        break;
                    case AutoKeywordPosition.After:
                        AutoKeywordText.AdditionalAfterKeywords.Add((CardKeyword) key);
                        break;
                }
                    
                CustomKeywords.KeywordIDs.Add((int) key, new(keywordId)
                {
                    AutoPosition = autoPosition,
                    RichKeyword = poolAttribute?.RichKeyword ?? true
                });
                continue;
            }
            
            //Following code is exclusively for CustomPile
            if (field.FieldType == typeof(PileType) && t.IsAssignableTo(typeof(CustomPile)))
            {
                var constructor = t.GetConstructor(BindingFlags.Instance | BindingFlags.Public, []) ?? throw new Exception($"CustomPile {t.FullName} with custom PileType does not have an accessible no-parameter constructor");

                var pileType = (PileType?)field.GetValue(null);
                if (pileType == null) throw new Exception($"Failed to be set up custom PileType in {t.FullName}");

                CustomPiles.RegisterCustomPile((PileType) pileType, () => (CustomPile) constructor.Invoke(null));
            }

            if (field.FieldType == typeof(RewardType) && t.IsAssignableTo(typeof(CustomReward)))
            {
                if (t.CreateInstance() is not CustomReward dummyReward)
                {
                    BaseLibMain.Logger.Error($"Reward instance creation for type {t.GetType()} from {t.Assembly} failed during Initialize");
                    continue;
                }
                BaseLibMain.Logger.Debug($"Initializing CustomReward inheriting class {dummyReward.GetType()}");
                dummyReward.Initialize();
            }
        }
    }
}
