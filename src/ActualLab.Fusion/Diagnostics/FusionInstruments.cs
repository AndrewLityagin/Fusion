using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ActualLab.Fusion.Diagnostics;

public static class FusionInstruments
{
    public static readonly ActivitySource ActivitySource = new(ThisAssembly.AssemblyName, ThisAssembly.AssemblyVersion);
    public static readonly Meter Meter = new(ThisAssembly.AssemblyName, ThisAssembly.AssemblyVersion);
}