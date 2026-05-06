using System.Reflection;
using HarmonyLib;

namespace BaseLib.SpireMethod;

/// <summary>
/// Attaches new behavior to a virtual method on an existing type without requiring
/// every mod author to write their own Harmony patch with runtime type checks.
///
/// <para>Register handlers via the static <c>Register</c> methods:</para>
/// <code>
/// // Async
/// static SpireMethod&lt;MyRelic&gt; hook = SpireMethod&lt;MyRelic&gt;.Register(
///     nameof(AbstractModel.AfterCardPlayed),
///     async (MyRelic instance, object[] args) =&gt; { /* ... */ }
/// );
///
/// // Value-returning
/// static SpireMethod&lt;MyRelic&gt; hook = SpireMethod&lt;MyRelic&gt;.Register(
///     nameof(AbstractModel.ModifyDamageAdditive),
///     (MyRelic instance, decimal current, object[] args) =&gt; current + 5m
/// );
///
/// // Void
/// static SpireMethod&lt;MyRelic&gt; hook = SpireMethod&lt;MyRelic&gt;.Register(
///     nameof(AbstractModel.SomeMethod),
///     (MyRelic instance, object[] args) =&gt; { /* ... */ }
/// );
/// </code>
///
/// <para>Handlers are invoked after the base implementation, in registration order.</para>
///
/// <para><typeparamref name="T"/> must <b>not</b> declare its own override of the target
/// method. If it does, patch the override directly with Harmony instead.</para>
/// </summary>
/// <typeparam name="T">The concrete type whose instances should receive the handler.</typeparam>
public sealed class SpireMethod<T> where T : class
{
    private SpireMethod()
    {
    }

    /// <summary>Register a handler for an async (Task-returning) virtual method.</summary>
    public static SpireMethod<T> Register(string methodName, AsyncSpireMethodHandler<T> handler)
    {
        var declaring = ResolveAndValidate(methodName);
        SpireMethodRegistry.Register(declaring, new AsyncHandler<T>(handler));
        return new SpireMethod<T>();
    }

    /// <summary>Register a handler for a void virtual method.</summary>
    public static SpireMethod<T> Register(string methodName, VoidSpireMethodHandler<T> handler)
    {
        var declaring = ResolveAndValidate(methodName);
        SpireMethodRegistry.Register(declaring, new VoidHandler<T>(handler));
        return new SpireMethod<T>();
    }

    /// <summary>
    /// Register a handler for a value-returning virtual method.
    /// Each handler receives the current return value and may return a modified one.
    /// </summary>
    public static SpireMethod<T> Register<TReturn>(string methodName, ValueSpireMethodHandler<T, TReturn> handler)
    {
        var declaring = ResolveAndValidate(methodName);
        SpireMethodRegistry.Register(declaring, new ValueHandler<T, TReturn>(handler));
        return new SpireMethod<T>();
    }

    private static MethodInfo ResolveAndValidate(string methodName)
    {
        var resolved = AccessTools.Method(typeof(T), methodName)
                       ?? throw new ArgumentException(
                           $"SpireMethod<{typeof(T).Name}>: method '{methodName}' not found on " +
                           $"'{typeof(T).FullName}' or any base class.");

        var declaredOnT = AccessTools.DeclaredMethod(typeof(T), methodName);
        if (declaredOnT != null)
            throw new InvalidOperationException(
                $"SpireMethod<{typeof(T).Name}>: '{typeof(T).Name}' already declares an override " +
                $"of '{methodName}'. Use a direct Harmony patch instead.");

        return resolved.GetBaseDefinition();
    }
}

/// <summary>Handler delegate for an async (Task-returning) virtual method.</summary>
public delegate Task AsyncSpireMethodHandler<T>(T instance, object[] args) where T : class;

/// <summary>Handler delegate for a void virtual method.</summary>
public delegate void VoidSpireMethodHandler<T>(T instance, object[] args) where T : class;

/// <summary>
/// Handler delegate for a value-returning virtual method.
/// Receives the current return value and the original method arguments;
/// returns a value that replaces the method's return value.
/// </summary>
public delegate TReturn ValueSpireMethodHandler<T, TReturn>(T instance, TReturn current, object[] args) where T : class;