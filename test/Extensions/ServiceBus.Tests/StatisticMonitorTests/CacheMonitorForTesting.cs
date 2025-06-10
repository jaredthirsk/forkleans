using Forkleans.Providers.Streams.Common;

namespace ServiceBus.Tests.MonitorTests
{
    public class CacheMonitorForTesting : ICacheMonitor
    {
        public CacheMonitorCounters CallCounters { get; } = new CacheMonitorCounters();
        
        public void TrackCachePressureMonitorStatusChange(string pressureMonitorType, bool underPressure, double? cachePressureContributionCount, double? currentPressure,
            double? flowControlThreshold)
        {
            Interlocked.Increment(ref CallCounters.TrackCachePressureMonitorStatusChangeCallCounter);
        }

        public void ReportCacheSize(long totalCacheSizeInByte)
        {
            Interlocked.Increment(ref CallCounters.ReportCacheSizeCallCounter);
        }

        public void ReportMessageStatistics(DateTime? oldestMessageEnqueueTimeUtc, DateTime? oldestMessageDequeueTimeUtc, DateTime? newestMessageEnqueueTimeUtc, long totalMessageCount)
        {
            Interlocked.Increment(ref CallCounters.ReportMessageStatisticsCallCounter);
        }

        public void TrackMemoryAllocated(int memoryInByte)
        {
            Interlocked.Increment(ref CallCounters.TrackMemoryAllocatedCallCounter);
        }

        public void TrackMemoryReleased(int memoryInByte)
        {
            Interlocked.Increment(ref CallCounters.TrackMemoryReleasedCallCounter);
        }

        public void TrackMessagesAdded(long mesageAdded)
        {
            Interlocked.Increment(ref CallCounters.TrackMessageAddedCounter);
        }

        public void TrackMessagesPurged(long messagePurged)
        {
            Interlocked.Increment(ref CallCounters.TrackMessagePurgedCounter);
        }
    }

    [Serializable]
    [Forkleans.GenerateSerializer]
    public class CacheMonitorCounters
    {
        [Forkleans.Id(0)]
        public int TrackCachePressureMonitorStatusChangeCallCounter;
        [Forkleans.Id(1)]
        public int ReportCacheSizeCallCounter;
        [Forkleans.Id(2)]
        public int ReportMessageStatisticsCallCounter;
        [Forkleans.Id(3)]
        public int TrackMemoryAllocatedCallCounter;
        [Forkleans.Id(4)]
        public int TrackMemoryReleasedCallCounter;
        [Forkleans.Id(5)]
        public int TrackMessageAddedCounter;
        [Forkleans.Id(6)]
        public int TrackMessagePurgedCounter;
    }
}
