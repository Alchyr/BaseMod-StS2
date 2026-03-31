using System.Reflection;
using BaseLib.Abstracts;
using BaseLib.Extensions;
using BaseLib.Patches.Localization;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;

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
/// <param name="position">The keyword's localized title will automatically be added to the specified position in the card's description for cards with the keyword.</param>
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class KeywordPropertiesAttribute(AutoKeywordPosition position) : Attribute
{
    public AutoKeywordPosition Position { get; } = position;
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

    public readonly struct KeywordInfo(string key, AutoKeywordPosition autoPosition)
    {
        public readonly string Key = key;
        public readonly AutoKeywordPosition AutoPosition = autoPosition;

        public static implicit operator string(KeywordInfo info) => info.Key;
    }

    //Support auto-text application through a patch in CardModel.GetDescriptionForPile, CardKeywordOrder
}

public static class CustomEnums
{
    private static readonly Dictionary<Type, KeyGenerator> KeyGenerators = [];

    public static object GenerateKey(Type enumType)
    {
        if (!KeyGenerators.TryGetValue(enumType, out var generator))
        {
            KeyGenerators.Add(enumType, generator = new(enumType));
        }
        return generator.GetKey();
    }
    private class KeyGenerator //will break an enum used like bitflags
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
        private object _nextKey;
        private readonly Func<object, object> _increment;

        public KeyGenerator(Type t)
        {
            if (!t.IsEnum)
            {
                _increment = o => o;
                throw new ArgumentException("Attempted to construct KeyGenerator with non-enum type " + t.FullName);
            }

            var values = t.GetEnumValuesAsUnderlyingType();
            var underlyingType = Enum.GetUnderlyingType(t);

            _nextKey = Convert.ChangeType(0, underlyingType);
            _increment = Incrementers[underlyingType];

            if (values.Length > 0)
            {
                foreach (var v in values)
                {
                    if (((IComparable)v).CompareTo(_nextKey) >= 0)
                    {
                        _nextKey = _increment(v);
                    }
                }
            }
            
            BaseLibMain.Logger.Info($"Generated KeyGenerator for enum {t.FullName} with starting value {_nextKey}");
        }

        public object GetKey()
        {
            var returnVal = _nextKey;
            _nextKey = _increment(_nextKey);
            return returnVal;
        }
    }
}



class GetCustomLocKey
{
    internal static void Patch(Harmony harmony)
    {
        Type t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Entities.Cards.CardKeywordExtensions");
        var originalMethod = AccessTools.Method(t, "GetLocKeyPrefix");
        var prefix = AccessTools.Method(typeof(GetCustomLocKey), nameof(UseCustomKeywordMap));
        harmony.Patch(originalMethod, prefix: new HarmonyMethod(prefix));
    }

    private static bool UseCustomKeywordMap(CardKeyword keyword, ref string? __result)
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
            var key = CustomEnums.GenerateKey(field.FieldType);
            var t = field.DeclaringType;
            if (t == null) continue;
            
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
                    
                CustomKeywords.KeywordIDs.Add((int) key, new(keywordId, autoPosition));
                continue;
            }
            
            //Following code is exclusively for CustomPile
            if (field.FieldType != typeof(PileType)) continue;
            if (!t.IsAssignableTo(typeof(CustomPile))) continue; 
                
            var constructor = t.GetConstructor(BindingFlags.Instance | BindingFlags.Public, []) ?? throw new Exception($"CustomPile {t.FullName} with custom PileType does not have an accessible no-parameter constructor");
                
            var pileType = (PileType?)field.GetValue(null);
            if (pileType == null) throw new Exception($"Failed to be set up custom PileType in {t.FullName}");
                
            CustomPiles.RegisterCustomPile((PileType) pileType, () => (CustomPile) constructor.Invoke(null));
        }
    }
}