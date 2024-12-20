using System.Diagnostics.CodeAnalysis;
using ActualLab.OS;
using ActualLab.Plugins.Internal;

namespace ActualLab.Plugins;

// Must be used as the only argument for plugin constructor invoked when
// PluginInfo/PluginSetInfo queries for plugin capabilities and dependencies.
public interface IPluginInfoProvider
{
    /// <summary>
    /// Use this type as a plugin constructor parameter in
    /// "info query" constructors.
    /// </summary>
#pragma warning disable CA1052
    public class Query
#pragma warning restore CA1052
    {
        public static readonly Query Instance = new();
    }

    [RequiresUnreferencedCode(UnreferencedCode.Plugins)]
    ImmutableHashSet<TypeRef> GetDependencies(Type pluginType);
    [RequiresUnreferencedCode(UnreferencedCode.Plugins)]
    PropertyBag GetCapabilities(Type pluginType);
}

public class PluginInfoProvider : IPluginInfoProvider
{
    private readonly ConcurrentDictionary<Type, LazySlim<Type, object?>> _pluginCache
        = new(HardwareInfo.ProcessorCountPo2, 131);

    [RequiresUnreferencedCode(UnreferencedCode.Plugins)]
    public virtual ImmutableHashSet<TypeRef> GetDependencies(Type pluginType)
    {
        var plugin = GetPlugin(pluginType);
        if (plugin is not IHasDependencies hasDependencies)
            return ImmutableHashSet<TypeRef>.Empty;
        var dependencies = hasDependencies.Dependencies;
        return dependencies.Select(t => (TypeRef) t).ToImmutableHashSet();
    }

    [RequiresUnreferencedCode(UnreferencedCode.Plugins)]
    public virtual PropertyBag GetCapabilities(Type pluginType)
    {
        var plugin = GetPlugin(pluginType);
        if (plugin is not IHasCapabilities hasCapabilities)
            return PropertyBag.Empty;
        return hasCapabilities.Capabilities;
    }

    [RequiresUnreferencedCode(UnreferencedCode.Plugins)]
    protected virtual object? GetPlugin(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type pluginType)
        => _pluginCache.GetOrAdd(pluginType, static pluginType1 => {
#pragma warning disable IL2070
            var ctor = pluginType1.GetConstructor([typeof(IPluginInfoProvider.Query)]);
            if (ctor != null)
                return ctor.Invoke([IPluginInfoProvider.Query.Instance]);
            ctor = pluginType1.GetConstructor(Type.EmptyTypes);
            return ctor?.Invoke([]);
#pragma warning restore IL2070
        });
}
