using Newtonsoft.Json.Serialization;

namespace ActualLab.Serialization.Internal;

public class PreferSerializableContractResolver : DefaultContractResolver
{
    protected override JsonContract CreateContract(Type objectType)
    {
        if (typeof(ISerializable).IsAssignableFrom(objectType))
            return CreateISerializableContract(objectType);

        return base.CreateContract(objectType);
    }
}
