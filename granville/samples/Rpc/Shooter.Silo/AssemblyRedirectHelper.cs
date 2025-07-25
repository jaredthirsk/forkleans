#if false // Using the Shooter.Shared one instead
using System.Reflection;
using System.Runtime.Loader;

namespace Shooter.Silo;

public static class AssemblyRedirectHelper
{
    private static readonly Dictionary<string, string> AssemblyRedirects = new()
    {
        ["Orleans.Core.Abstractions"] = "Orleans.Core.Abstractions",
        ["Orleans.Core"] = "Orleans.Core",
        ["Orleans.Runtime"] = "Orleans.Runtime",
        ["Orleans.Serialization"] = "Granville.Orleans.Serialization",
        ["Orleans.Serialization.Abstractions"] = "Orleans.Serialization.Abstractions",
        ["Orleans.Server"] = "Orleans.Server",
        ["Orleans.Sdk"] = "Orleans.Sdk",
        ["Orleans.Reminders"] = "Orleans.Reminders",
        ["Orleans.CodeGenerator"] = "Orleans.CodeGenerator",
        ["Orleans.Analyzers"] = "Orleans.Analyzers"
    };

    public static void ConfigureAssemblyRedirects()
    {
        AssemblyLoadContext.Default.Resolving += OnAssemblyResolving;
    }

    private static Assembly? OnAssemblyResolving(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        // Check if this is an Orleans assembly that needs to be redirected
        if (assemblyName.Name != null && AssemblyRedirects.TryGetValue(assemblyName.Name, out var redirectTo))
        {
            Console.WriteLine($"Redirecting assembly {assemblyName.Name} to {redirectTo}");

            try
            {
                // Try to load the Orleans assembly
                var orleansAssemblyName = new AssemblyName(redirectTo)
                {
                    Version = assemblyName.Version ?? new Version(9, 2, 0, 43)
                };

                return context.LoadFromAssemblyName(orleansAssemblyName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to redirect {assemblyName.Name} to {redirectTo}: {ex.Message}");
            }
        }

        return null;
    }
}
#endif