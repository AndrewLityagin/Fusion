namespace ActualLab.Interception;

public interface IProxy : IRequiresAsyncProxy
{
    Interceptor Interceptor { get; }

    void SetInterceptor(Interceptor interceptor);
}
