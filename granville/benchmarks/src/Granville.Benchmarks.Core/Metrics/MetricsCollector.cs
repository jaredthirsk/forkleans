using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Granville.Benchmarks.Core.Metrics
{
    public class MetricsCollector
    {
        private readonly ConcurrentBag<double> _latencies = new();
        private readonly Stopwatch _stopwatch = new();
        private long _successCount;
        private long _failureCount;
        private long _timeoutCount;
        private long _bytesSent;
        private long _bytesReceived;
        private long _packetsSent;
        private long _packetsReceived;
        private long _packetsLost;
        
        private Process? _currentProcess;
        private long _startingGen0;
        private long _startingGen1;
        private long _startingGen2;
        private long _startingTotalMemory;
        
        public void Start()
        {
            _stopwatch.Restart();
            _currentProcess = Process.GetCurrentProcess();
            
            // Capture starting GC stats
            _startingGen0 = GC.CollectionCount(0);
            _startingGen1 = GC.CollectionCount(1);
            _startingGen2 = GC.CollectionCount(2);
            _startingTotalMemory = GC.GetTotalMemory(false);
        }
        
        public void RecordLatency(double latencyMicros)
        {
            _latencies.Add(latencyMicros);
        }
        
        public void RecordSuccess()
        {
            Interlocked.Increment(ref _successCount);
        }
        
        public void RecordFailure()
        {
            Interlocked.Increment(ref _failureCount);
        }
        
        public void RecordTimeout()
        {
            Interlocked.Increment(ref _timeoutCount);
        }
        
        public void RecordBytesSent(long bytes)
        {
            Interlocked.Add(ref _bytesSent, bytes);
        }
        
        public void RecordBytesReceived(long bytes)
        {
            Interlocked.Add(ref _bytesReceived, bytes);
        }
        
        public void RecordPacketSent()
        {
            Interlocked.Increment(ref _packetsSent);
        }
        
        public void RecordPacketReceived()
        {
            Interlocked.Increment(ref _packetsReceived);
        }
        
        public void RecordPacketLost()
        {
            Interlocked.Increment(ref _packetsLost);
        }
        
        /// <summary>
        /// Records an error occurrence (maps to RecordFailure)
        /// </summary>
        public void RecordError(string? errorMessage = null)
        {
            RecordFailure();
        }
        
        /// <summary>
        /// Records a successful message with latency (maps to RecordSuccess and RecordLatency)
        /// </summary>
        public void RecordMessage(double latencyMicros)
        {
            RecordSuccess();
            RecordLatency(latencyMicros);
        }
        
        /// <summary>
        /// Records a successful message with latency and byte count
        /// </summary>
        public void RecordMessage(double latencyMicros, int byteCount)
        {
            RecordSuccess();
            RecordLatency(latencyMicros);
            RecordBytesSent(byteCount);
            RecordPacketSent();
        }
        
        public BenchmarkMetrics GetMetrics(string testName, string transportType, bool isReliable, int messageSize, int concurrentClients)
        {
            _stopwatch.Stop();
            var endTime = DateTime.UtcNow;
            var duration = _stopwatch.Elapsed;
            
            var latencyArray = _latencies.ToArray();
            Array.Sort(latencyArray);
            
            var metrics = new BenchmarkMetrics
            {
                TestName = testName,
                StartTime = endTime - duration,
                EndTime = endTime,
                TransportType = transportType,
                IsReliable = isReliable,
                MessageSize = messageSize,
                ConcurrentClients = concurrentClients,
                
                // Message counts
                SuccessfulCalls = _successCount,
                FailedCalls = _failureCount,
                TimeoutCalls = _timeoutCount,
                TotalMessages = _successCount + _failureCount + _timeoutCount,
                MessagesPerSecond = (_successCount + _failureCount + _timeoutCount) / duration.TotalSeconds,
                
                // Network metrics
                BytesSent = _bytesSent,
                BytesReceived = _bytesReceived,
                BytesPerSecond = (_bytesSent + _bytesReceived) / duration.TotalSeconds,
                PacketsSent = _packetsSent,
                PacketsReceived = _packetsReceived,
                PacketsLost = _packetsLost,
                
                // GC metrics
                Gen0Collections = (int)(GC.CollectionCount(0) - _startingGen0),
                Gen1Collections = (int)(GC.CollectionCount(1) - _startingGen1),
                Gen2Collections = (int)(GC.CollectionCount(2) - _startingGen2),
                TotalAllocations = GC.GetTotalMemory(false) - _startingTotalMemory,
            };
            
            // Latency metrics
            if (latencyArray.Length > 0)
            {
                metrics.MinLatency = latencyArray[0];
                metrics.MaxLatency = latencyArray[latencyArray.Length - 1];
                metrics.AverageLatency = latencyArray.Average();
                metrics.MedianLatency = GetPercentile(latencyArray, 50);
                metrics.P95Latency = GetPercentile(latencyArray, 95);
                metrics.P99Latency = GetPercentile(latencyArray, 99);
            }
            
            // CPU metrics (if available)
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                try
                {
                    metrics.AverageCpuUsage = _currentProcess.TotalProcessorTime.TotalMilliseconds / duration.TotalMilliseconds / Environment.ProcessorCount * 100;
                    metrics.PeakMemoryUsage = _currentProcess.PeakWorkingSet64;
                }
                catch { }
            }
            
            return metrics;
        }
        
        private static double GetPercentile(double[] sortedValues, int percentile)
        {
            if (sortedValues.Length == 0) return 0;
            
            var index = (percentile / 100.0) * (sortedValues.Length - 1);
            var lower = (int)Math.Floor(index);
            var upper = (int)Math.Ceiling(index);
            
            if (lower == upper)
                return sortedValues[lower];
                
            var weight = index - lower;
            return sortedValues[lower] * (1 - weight) + sortedValues[upper] * weight;
        }
    }
}