using System;
using System.Collections.Generic;
using Granville.Benchmarks.Core.Transport;

namespace Granville.Benchmarks.Runner.Services
{
    /// <summary>
    /// Configuration options for benchmark execution
    /// </summary>
    public class BenchmarkOptions
    {
        public int ClientCount { get; set; } = 100;
        public int MessageSize { get; set; } = 256;
        public int MessagesPerSecond { get; set; } = 60;
        public TimeSpan WarmupDuration { get; set; } = TimeSpan.FromSeconds(10);
        public TimeSpan TestDuration { get; set; } = TimeSpan.FromMinutes(1);
        public TimeSpan CooldownDuration { get; set; } = TimeSpan.FromSeconds(5);
        public List<TransportConfiguration> Transports { get; set; } = new();
        public List<NetworkCondition> NetworkConditions { get; set; } = new();
        
        /// <summary>
        /// When true, use actual network calls instead of simulation delays
        /// </summary>
        public bool UseRawTransport { get; set; } = false;
        
        /// <summary>
        /// Server host for raw transport benchmarks
        /// </summary>
        public string ServerHost { get; set; } = "127.0.0.1";
        
        /// <summary>
        /// Server port for raw transport benchmarks
        /// </summary>
        public int ServerPort { get; set; } = 12345;
        
        /// <summary>
        /// When true, use actual network transport implementations instead of simulation
        /// </summary>
        public bool UseActualTransport { get; set; } = false;
        
        // Legacy properties for backward compatibility
        public int TestDurationSeconds { get; set; } = 60;
        public int ConcurrentClients { get; set; } = 10;
        public string OutputPath { get; set; } = "./results";
    }
    
    /// <summary>
    /// Transport configuration for benchmarks
    /// </summary>
    public class TransportConfiguration
    {
        public string Type { get; set; } = "";
        public bool Reliable { get; set; }
        public Dictionary<string, object> Settings { get; set; } = new();
        
        // Legacy properties for backward compatibility
        public int TimeoutMs { get; set; } = 5000;
    }
}