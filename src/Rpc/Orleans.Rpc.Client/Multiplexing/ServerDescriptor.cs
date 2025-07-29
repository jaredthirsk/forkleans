using System;
using System.Collections.Generic;

namespace Granville.Rpc.Multiplexing
{
    /// <summary>
    /// Default implementation of IServerDescriptor.
    /// </summary>
    public class ServerDescriptor : IServerDescriptor
    {
        public string ServerId { get; set; } = string.Empty;
        public string HostName { get; set; } = string.Empty;
        public int Port { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
        public bool IsPrimary { get; set; }
        public DateTime LastHealthCheck { get; set; } = DateTime.MinValue;
        public ServerHealthStatus HealthStatus { get; set; } = ServerHealthStatus.Unknown;
    }
}