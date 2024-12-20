using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using ActualLab.Internal;

namespace ActualLab.Serialization;

public interface IByteSerializer
{
    [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
    public object? Read(ReadOnlyMemory<byte> data, Type type, out int readLength);
    [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
    public void Write(IBufferWriter<byte> bufferWriter, object? value, Type type);
    public IByteSerializer<T> ToTyped<T>(Type? serializedType = null);
}

public interface IByteSerializer<T>
{
    [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
    public T Read(ReadOnlyMemory<byte> data, out int readLength);
    [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
    public void Write(IBufferWriter<byte> bufferWriter, T value);
}

/// <summary>
/// A serializer that allows projection of <seealso cref="ReadOnlyMemory{T}"/> parts
/// from source <seealso cref="ReadOnlyMemory{T}"/> on reads.
/// </summary>
/// <typeparam name="T">The serialized type.</typeparam>
public interface IProjectingByteSerializer<T> : IByteSerializer<T>
{
    public bool AllowProjection { get; }

    public T Read(ReadOnlyMemory<byte> data, out int readLength, out bool isProjection);
}
