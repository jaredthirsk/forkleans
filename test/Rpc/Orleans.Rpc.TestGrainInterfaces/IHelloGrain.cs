using System.Threading.Tasks;
using Forkleans;
using Forkleans.Rpc;

namespace Forkleans.Rpc.TestGrainInterfaces
{
    /// <summary>
    /// Test grain interface for RPC communication.
    /// </summary>
    [RpcConnection(PersistentConnection = true)]
    public interface IHelloGrain : IRpcGrainInterfaceWithStringKey
    {
        /// <summary>
        /// Simple greeting method.
        /// </summary>
        [RpcMethod(DeliveryMode = RpcDeliveryMode.Reliable)]
        ValueTask<string> SayHello(string name);

        /// <summary>
        /// Echo method for testing.
        /// </summary>
        [RpcMethod(DeliveryMode = RpcDeliveryMode.Reliable)]
        ValueTask<string> Echo(string message);

        /// <summary>
        /// Method that returns a complex object.
        /// </summary>
        [RpcMethod(DeliveryMode = RpcDeliveryMode.Reliable)]
        ValueTask<HelloResponse> GetDetailedGreeting(HelloRequest request);
    }

    /// <summary>
    /// Test request object.
    /// </summary>
    [GenerateSerializer]
    public class HelloRequest
    {
        [Id(0)]
        public string Name { get; set; }
        
        [Id(1)]
        public int Age { get; set; }
        
        [Id(2)]
        public string Location { get; set; }
    }

    /// <summary>
    /// Test response object.
    /// </summary>
    [GenerateSerializer]
    public class HelloResponse
    {
        [Id(0)]
        public string Greeting { get; set; }
        
        [Id(1)]
        public string ServerTime { get; set; }
        
        [Id(2)]
        public int ProcessId { get; set; }
    }
}
