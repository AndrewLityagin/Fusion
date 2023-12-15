namespace ActualLab.Versioning;

public interface IHasVersion<out TVersion>
    where TVersion : notnull
{
    TVersion Version { get; }
}
