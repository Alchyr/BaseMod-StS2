namespace BaseLib.SpireMethod;

internal abstract class SpireMethodHandlerBase
{
    public abstract Type TargetType { get; }

    public virtual void InvokeVoid(object instance, object[] args) =>
        throw new InvalidOperationException("This handler does not support void invocation.");

    public virtual Task InvokeAsync(object instance, object[] args) =>
        throw new InvalidOperationException("This handler does not support async invocation.");

    public virtual object? InvokeValue(object instance, object? current, object[] args) =>
        throw new InvalidOperationException("This handler does not support value invocation.");
}

internal sealed class AsyncHandler<T>(AsyncSpireMethodHandler<T> handler) : SpireMethodHandlerBase where T : class
{
    public override Type TargetType => typeof(T);

    public override Task InvokeAsync(object instance, object[] args) =>
        handler((T)instance, args);
}

internal sealed class VoidHandler<T>(VoidSpireMethodHandler<T> handler) : SpireMethodHandlerBase where T : class
{
    public override Type TargetType => typeof(T);

    public override void InvokeVoid(object instance, object[] args) =>
        handler((T)instance, args);
}

internal sealed class ValueHandler<T, TReturn>(ValueSpireMethodHandler<T, TReturn> handler) : SpireMethodHandlerBase where T : class
{
    public override Type TargetType => typeof(T);

    public override object? InvokeValue(object instance, object? current, object[] args) =>
        handler((T)instance, (TReturn)current!, args);
}
