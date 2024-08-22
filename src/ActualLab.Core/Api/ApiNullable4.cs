using System.Diagnostics;
using ActualLab.Internal;

namespace ActualLab.Api;

#pragma warning disable CA1036
#pragma warning disable CS0618 // Type or member is obsolete

public static class ApiNullable4
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ApiNullable4<T> None<T>() where T : struct => default;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ApiNullable4<T> Some<T>(T value) where T : struct => new(value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ApiNullable4<T> From<T>(T? value) where T : struct => new(value);
}

/// <summary>
/// Like <see cref="Nullable&lt;T&gt;"/>, but 1-byte aligned.
/// </summary>
/// <typeparam name="T">The type of <see cref="Value"/>.</typeparam>
[StructLayout(LayoutKind.Sequential, Pack = 1)] // Important!
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.OptOut)]
[DebuggerDisplay("{" + nameof(DebugValue) + "}")]
public readonly partial struct ApiNullable4<T>
    : IOption, ICanBeNone<ApiNullable4<T>>, IEquatable<ApiNullable4<T>>, IComparable<ApiNullable4<T>>
    where T : struct
{
    public static ApiNullable4<T> None => default;

    [Obsolete("This member exists solely to make serialization work. Don't use it!")]
    [DataMember(Order = 0), MemoryPackOrder(0), JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public int RawHasValue { get; }

    [DataMember(Order = 1), MemoryPackOrder(1), JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public T ValueOrDefault { get; }

    [JsonInclude, Newtonsoft.Json.JsonProperty, MemoryPackIgnore]
    public T? Nullable => HasValue ? (T?)ValueOrDefault : default;

    // Computed properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool HasValue {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => RawHasValue != 0;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public T Value {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get { AssertHasValue(); return ValueOrDefault; }
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !HasValue;
    }

    // ReSharper disable once HeapView.BoxingAllocation
    object IOption.Value => Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    private string DebugValue => ToString();

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ApiNullable4(T? nullable)
    {
        RawHasValue = nullable.HasValue ? 1 : 0;
        ValueOrDefault = nullable.GetValueOrDefault();
    }

    [MemoryPackConstructor]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    // ReSharper disable once ConvertToPrimaryConstructor
    public ApiNullable4(int rawHasValue, T valueOrDefault)
    {
        RawHasValue = rawHasValue;
        ValueOrDefault = valueOrDefault;
    }

#pragma warning disable CS8603 // Possible null reference return.
    public override string ToString()
        => HasValue ? ValueOrDefault.ToString() : "";
#pragma warning restore CS8603 // Possible null reference return.

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Deconstruct(out bool hasValue, out T value)
    {
        hasValue = HasValue;
        value = ValueOrDefault;
    }

    // Useful methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsSome(out T value)
    {
        value = ValueOrDefault;
        return HasValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Option<T> ToOption()
        => HasValue ? new Option<T>(true, ValueOrDefault) : default;

    // Equality

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ApiNullable4<T> other)
        => HasValue
            ? other.HasValue && EqualityComparer<T>.Default.Equals(ValueOrDefault, other.ValueOrDefault)
            : !other.HasValue;
    public override bool Equals(object? obj)
        => obj is ApiNullable4<T> other && Equals(other);
    public override int GetHashCode()
        => HasValue ? ValueOrDefault.GetHashCode() : int.MinValue;

    // Comparison

    public int CompareTo(ApiNullable4<T> other)
        => HasValue
            ? other.HasValue ? Comparer<T>.Default.Compare(ValueOrDefault, other.ValueOrDefault) : 1
            : other.HasValue ? -1 : 0;

    // Operators

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ApiNullable4<T>(T source) => new(source);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ApiNullable4<T>(T? source) => new(source);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator T?(ApiNullable4<T> source) => source.Nullable;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ApiNullable4<T> left, ApiNullable4<T> right) => left.Equals(right);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ApiNullable4<T> left, ApiNullable4<T> right) => !left.Equals(right);

    // Private helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AssertHasValue()
    {
        if (!HasValue)
            throw Errors.IsNone<ApiNullable4<T>>();
    }
}
