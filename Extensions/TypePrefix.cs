namespace BaseLib.Extensions;

public static class TypePrefix
{
    public const char PrefixSplitChar = '-';
    public static string GetPrefix(this Type t)
    {
        return t.Namespace == null ? "" : 
            $"{t.Namespace[..t.Namespace.IndexOf('.')].ToUpperInvariant()}{PrefixSplitChar}";
    }
    
    //return $"{t.GetRootNamespace().ToUpperInvariant()}{PrefixSplitChar}";
    public static string GetRootNamespace(this Type t)
    {
        if (t.Namespace == null)
            return "";

        var dotIndex = t.Namespace.IndexOf('.');

        if (dotIndex == -1)
            dotIndex = t.Namespace.Length;
        return t.Namespace[..dotIndex];
    }
}
