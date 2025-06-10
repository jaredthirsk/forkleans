namespace Forkleans.Configuration
{
    public class ZooKeeperGatewayListProviderOptions
    {
        /// <summary>
        /// Connection string for ZooKeeper storage
        /// </summary>
        [Redact]
        public string ConnectionString { get; set; }
    }
}
