using System.Diagnostics.Metrics;

namespace Forkleans.Runtime;

public static class Instruments
{
    public static readonly Meter Meter = new("Microsoft.Orleans");
}
