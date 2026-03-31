using System.Reflection;
using System.Runtime.Loader;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.HotReload;

/// <summary>
/// Helpers for type introspection during hot reload: signature hashing,
/// inheritance checks, model ID construction, injection priority.
/// </summary>
internal static class TypeSignatureHasher
{
    private const BindingFlags DeclaredMembers =
        BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.DeclaredOnly;

    /// <summary>
    /// Compute a hash of a type's member signatures for incremental reload comparison.
    /// If the hash matches between old and new assembly, the type is unchanged and can be skipped.
    /// </summary>
    public static int ComputeHash(Type type)
    {
        unchecked
        {
            var signatures = new List<string>
            {
                $"type:{type.FullName}",
                $"base:{type.BaseType?.FullName ?? ""}",
            };

            signatures.AddRange(type.GetInterfaces()
                .Select(i => $"iface:{i.FullName}")
                .OrderBy(s => s, StringComparer.Ordinal));

            foreach (var method in type.GetMethods(DeclaredMembers)
                .OrderBy(m => m.ToString(), StringComparer.Ordinal))
            {
                var sig = $"{method.Name}|{method.ReturnType.FullName}|{string.Join(",", method.GetParameters().Select(p => p.ParameterType.FullName))}";
                try
                {
                    var il = method.GetMethodBody()?.GetILAsByteArray();
                    if (il is { Length: > 0 })
                        sig += $"|il:{Convert.ToHexString(il)}";
                }
                catch { /* some methods do not expose IL */ }
                signatures.Add($"method:{sig}");
            }

            signatures.AddRange(type.GetFields(DeclaredMembers)
                .OrderBy(f => f.Name, StringComparer.Ordinal)
                .Select(f => $"field:{f.Name}|{f.FieldType.FullName}"));

            signatures.AddRange(type.GetProperties(DeclaredMembers)
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => $"prop:{p.Name}|{p.PropertyType.FullName}"));

            foreach (var attr in type.GetCustomAttributesData()
                .OrderBy(a => a.AttributeType.FullName, StringComparer.Ordinal))
            {
                var ctorArgs = string.Join(",", attr.ConstructorArguments.Select(a => a.Value?.ToString() ?? "null"));
                signatures.Add($"attr:{attr.AttributeType.FullName}|{ctorArgs}");
            }

            int hash = (int)2166136261;
            foreach (var signature in signatures)
            {
                foreach (var ch in signature)
                {
                    hash ^= ch;
                    hash *= 16777619;
                }
            }
            return hash;
        }
    }

    /// <summary>
    /// Check if a type IS or inherits from a base type by walking the name chain.
    /// Works across AssemblyLoadContexts where IsSubclassOf fails due to type identity.
    /// Checks the type itself first, then walks up the inheritance chain.
    /// </summary>
    public static bool InheritsFromByName(Type type, string baseTypeName)
    {
        var cursor = type;
        while (cursor != null)
        {
            if (cursor.Name == baseTypeName)
                return true;
            cursor = cursor.BaseType;
        }
        return false;
    }

    /// <summary>
    /// Safely enumerate types from an assembly, handling ReflectionTypeLoadException.
    /// If warnings and prefix are provided, load errors are recorded instead of silently swallowed.
    /// </summary>
    public static IEnumerable<Type> GetLoadableTypes(Assembly assembly, List<string>? warnings = null, string? warningPrefix = null)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            if (warnings != null)
            {
                var prefix = warningPrefix ?? "types";
                warnings.Add($"{prefix}: {ex.Message}");
                foreach (var loaderEx in ex.LoaderExceptions.Where(e => e != null).Take(3))
                    warnings.Add($"{prefix}_loader: {loaderEx!.Message}");
            }
            return ex.Types.Where(t => t != null).Cast<Type>();
        }
    }

    /// <summary>
    /// Returns injection priority for entity types. Lower = injected first.
    /// Powers before cards (cards reference powers via PowerVar),
    /// monsters before encounters (encounters reference monsters).
    /// </summary>
    public static int GetInjectionPriority(Type type)
    {
        if (InheritsFromByName(type, nameof(PowerModel))) return 0;
        if (InheritsFromByName(type, nameof(RelicModel))) return 1;
        if (InheritsFromByName(type, nameof(PotionModel))) return 2;
        if (InheritsFromByName(type, nameof(MonsterModel))) return 3;
        if (InheritsFromByName(type, nameof(EncounterModel))) return 4;
        if (InheritsFromByName(type, nameof(CardModel))) return 5;
        if (InheritsFromByName(type, "EventModel")) return 6;
        return 9;
    }

    /// <summary>
    /// Get category and entry strings for a type WITHOUT constructing a ModelId.
    /// Used to register in the serialization cache before ModelId construction.
    /// </summary>
    public static (string category, string entry) GetCategoryAndEntry(Type type)
    {
        var cursor = type;
        while (cursor.BaseType != null && cursor.BaseType.Name != nameof(AbstractModel))
            cursor = cursor.BaseType;
        string category = ModelId.SlugifyCategory(cursor.Name);
        string entry = StringHelper.Slugify(type.Name);
        return (category, entry);
    }

    /// <summary>
    /// Build a ModelId for a type using name-based base type detection.
    /// Must be called AFTER registering the entity in ModelIdSerializationCache.
    /// </summary>
    public static ModelId BuildModelId(Type type)
    {
        var (category, entry) = GetCategoryAndEntry(type);
        return new ModelId(category, entry);
    }

    /// <summary>
    /// Find all loaded assemblies that belong to a given mod. Used to locate old versions
    /// of a mod's assembly so we can snapshot their entities and clean up stale patches.
    /// The exclude parameter filters out the new assembly being loaded.
    /// </summary>
    public static IEnumerable<Assembly> GetAssembliesForMod(string modKey, Assembly? exclude = null)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a =>
                !string.IsNullOrEmpty(a.GetName().Name)
                && string.Equals(AssemblyStamper.NormalizeModKey(a.GetName().Name), modKey, StringComparison.OrdinalIgnoreCase)
                && a != exclude);
    }

    /// <summary>
    /// ALC resolving handler that redirects version-mismatched assembly references
    /// to whatever's already loaded. For example, if a mod was built against BaseLib 0.2.1
    /// but the game has BaseLib 0.1.0 loaded, this handler returns the loaded 0.1.0 instead
    /// of throwing FileNotFoundException. Matches by name, culture, and public key token.
    /// </summary>
    public static Assembly? DefaultAlcResolving(AssemblyLoadContext context, AssemblyName name)
    {
        var requestedToken = name.GetPublicKeyToken() ?? [];
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(loaded =>
            {
                var candidate = loaded.GetName();
                if (!string.Equals(candidate.Name, name.Name, StringComparison.Ordinal))
                    return false;
                var candidateToken = candidate.GetPublicKeyToken() ?? [];
                bool tokenMatches = requestedToken.Length == 0 || candidateToken.SequenceEqual(requestedToken);
                bool cultureMatches = string.Equals(candidate.CultureName ?? "", name.CultureName ?? "", StringComparison.OrdinalIgnoreCase);
                return tokenMatches && cultureMatches;
            });
    }

    /// <summary>
    /// How many bits the network serialization needs for this many entries.
    /// Called after registering new entity IDs in the serialization cache.
    /// </summary>
    public static int ComputeBitSize(int count)
    {
        return count <= 1 ? 0 : (int)Math.Ceiling(Math.Log2(count));
    }
}
