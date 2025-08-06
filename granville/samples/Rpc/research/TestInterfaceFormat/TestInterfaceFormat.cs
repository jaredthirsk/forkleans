using System;
using Orleans.Runtime;
using Orleans.Metadata;
using Orleans.Serialization.TypeSystem;
using Shooter.Shared.RpcInterfaces;

class Program
{
    static void Main()
    {
        try
        {
            var interfaceType = typeof(IGameRpcGrain);
            Console.WriteLine($"Interface Type: {interfaceType}");
            Console.WriteLine($"Interface Type FullName: {interfaceType.FullName}");
            Console.WriteLine($"Interface Type AssemblyQualifiedName: {interfaceType.AssemblyQualifiedName}");
            
            // Try to format it as Orleans would
            var formatted = RuntimeTypeNameFormatter.Format(interfaceType);
            Console.WriteLine($"RuntimeTypeNameFormatter.Format: {formatted}");
            
            // Create a GrainInterfaceType
            var grainInterfaceType = new GrainInterfaceType(formatted);
            Console.WriteLine($"GrainInterfaceType.ToString(): {grainInterfaceType}");
            
            // Check what we're looking for
            var grainTypeStr = grainInterfaceType.ToString();
            Console.WriteLine($"Contains 'Rpc': {grainTypeStr.Contains("Rpc")}");
            Console.WriteLine($"Contains 'Shooter': {grainTypeStr.Contains("Shooter")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
        }
    }
}
