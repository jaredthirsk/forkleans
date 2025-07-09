using System;
using Orleans.Serialization.Configuration;

namespace TestTypeForwards
{
    class Program
    {
        static void Main(string[] args)
        {
            // Test that TypeManifestProviderBase can be accessed
            Type typeManifestProviderBase = typeof(TypeManifestProviderBase);
            Console.WriteLine($"TypeManifestProviderBase: {typeManifestProviderBase.FullName}");
            
            // Test that ITypeManifestProvider can be accessed
            Type iTypeManifestProvider = typeof(ITypeManifestProvider);
            Console.WriteLine($"ITypeManifestProvider: {iTypeManifestProvider.FullName}");
            
            // Test that TypeManifestOptions can be accessed
            Type typeManifestOptions = typeof(TypeManifestOptions);
            Console.WriteLine($"TypeManifestOptions: {typeManifestOptions.FullName}");
            
            Console.WriteLine("All type forwards are working correctly!");
        }
    }
}