using System.Diagnostics.CodeAnalysis;
using ActualLab.Internal;

namespace ActualLab.Serialization;

public static class UniSerialized
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static UniSerialized<TValue> New<TValue>(TValue value = default!)
        => new(value);
}

#if !NET5_0
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
#endif
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.OptOut)]
public readonly partial struct UniSerialized<T>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public T Value { get; init; } = default!;

    [JsonInclude, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public string Json {
        [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
        get => SerializeText(Value, SerializerKind.SystemJson);
        [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
        init => Value = DeserializeText(value, SerializerKind.SystemJson);
    }

    [JsonIgnore, MemoryPackIgnore]
    public string NewtonsoftJson {
        [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
        get => SerializeText(Value, SerializerKind.NewtonsoftJson);
        [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
        init => Value = DeserializeText(value, SerializerKind.NewtonsoftJson);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackOrder(0)]
    public byte[] MemoryPack {
        [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
        get => SerializeBytes(Value, SerializerKind.MemoryPack);
        [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
        init => Value = DeserializeBytes(value, SerializerKind.MemoryPack);
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore, DataMember(Order = 0)]
    public byte[] MessagePack {
        [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
        get => SerializeBytes(Value, SerializerKind.MemoryPack);
        [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
        init => Value = DeserializeBytes(value, SerializerKind.MemoryPack);
    }

    public UniSerialized(T value)
        => Value = value;

    [MemoryPackConstructor]
    public UniSerialized(byte[] memoryPack)
        => MemoryPack = memoryPack;

    // ToString

    public override string ToString()
        => $"{GetType().GetName()}(...)";

    // Private methods

    [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
    private static string SerializeText(T value, SerializerKind serializerKind)
    {
        var serializer = (ITextSerializer)serializerKind.GetDefaultSerializer();
        return serializer.Write(value);
    }

    [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
    private static byte[] SerializeBytes(T value, SerializerKind serializerKind)
    {
        var serializer = serializerKind.GetDefaultSerializer();
        using var buffer = serializer.Write(value);
        return buffer.WrittenSpan.ToArray();
    }

    [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
    private static T DeserializeText(string text, SerializerKind serializerKind)
    {
        var serializer = (ITextSerializer)serializerKind.GetDefaultSerializer();
        return serializer.Read<T>(text);
    }

    [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
    private static T DeserializeBytes(byte[] bytes, SerializerKind serializerKind)
    {
        var serializer = serializerKind.GetDefaultSerializer();
        return serializer.Read<T>(bytes);
    }
}