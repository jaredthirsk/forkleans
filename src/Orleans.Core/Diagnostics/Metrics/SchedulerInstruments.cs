using System.Diagnostics.Metrics;

namespace Forkleans.Runtime;

internal class SchedulerInstruments
{
    internal static readonly Counter<int> LongRunningTurnsCounter = Instruments.Meter.CreateCounter<int>(InstrumentNames.SCHEDULER_NUM_LONG_RUNNING_TURNS);
}
