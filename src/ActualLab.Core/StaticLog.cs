using ActualLab.OS;

namespace ActualLab;

public static class StaticLog
{
#if NET9_0_OR_GREATER
    private static readonly Lock Lock = new();
#else
    private static readonly object Lock = new();
#endif
    private static readonly ConcurrentDictionary<object, ILogger> Cache = new(HardwareInfo.ProcessorCountPo2, 131);
    private static volatile ILoggerFactory _factory = NullLoggerFactory.Instance;

    public static ILoggerFactory Factory {
        get => _factory;
        set {
            lock (Lock) {
                if (ReferenceEquals(Factory, value))
                    return;

                _factory = value;
                Cache.Clear();
            }
        }
    }

    public static ILogger<T> For<T>()
        => (ILogger<T>)Cache.GetOrAdd(typeof(T),
            static key => (ILogger)typeof(Logger<>).MakeGenericType((Type)key).CreateInstance(Factory));

    public static ILogger For(Type type)
        => Cache.GetOrAdd(type.NonProxyType(),
            static key => Factory.CreateLogger((Type)key));

    public static ILogger For(string category)
        => Cache.GetOrAdd(category,
            static key => Factory.CreateLogger((string)key));
}
