using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GenerateTypeForwardingAssemblies
{
    class Program
    {
        static void Main(string[] args)
        {
            var baseDir = args.Length > 0 ? args[0] : "../../src";
            var outputDir = args.Length > 1 ? args[1] : "./shims-proper";
            
            // Check if the second argument is a file path or directory
            bool isFilePath = outputDir.EndsWith(".dll");
            string actualOutputDir = isFilePath ? Path.GetDirectoryName(outputDir) ?? "." : outputDir;
            
            Directory.CreateDirectory(actualOutputDir);

            var assembliesMap = new Dictionary<string, string>
            {
                ["Orleans.Core"] = Path.Combine(baseDir, "Orleans.Core", "bin", "Release", "net8.0", "Granville.Orleans.Core.dll"),
                ["Orleans.Core.Abstractions"] = Path.Combine(baseDir, "Orleans.Core.Abstractions", "bin", "Release", "net8.0", "Granville.Orleans.Core.Abstractions.dll"),
                ["Orleans.Runtime"] = Path.Combine(baseDir, "Orleans.Runtime", "bin", "Release", "net8.0", "Granville.Orleans.Runtime.dll"),
                ["Orleans.Serialization"] = Path.Combine(baseDir, "Orleans.Serialization", "bin", "Release", "net8.0", "Granville.Orleans.Serialization.dll"),
                ["Orleans.Serialization.Abstractions"] = Path.Combine(baseDir, "Orleans.Serialization.Abstractions", "bin", "Release", "netstandard2.0", "Granville.Orleans.Serialization.Abstractions.dll"),
                ["Orleans.Reminders"] = Path.Combine(baseDir, "Orleans.Reminders", "bin", "Release", "net8.0", "Granville.Orleans.Reminders.dll"),
                ["Orleans.Persistence.Memory"] = Path.Combine(baseDir, "Orleans.Persistence.Memory", "bin", "Release", "net8.0", "Granville.Orleans.Persistence.Memory.dll"),
                ["Orleans.Server"] = Path.Combine(baseDir, "Orleans.Server", "bin", "Release", "net8.0", "Granville.Orleans.Server.dll"),
                ["Orleans.Serialization.SystemTextJson"] = Path.Combine(baseDir, "Orleans.Serialization.SystemTextJson", "bin", "Release", "net8.0", "Granville.Orleans.Serialization.SystemTextJson.dll"),
                ["Orleans.Sdk"] = Path.Combine(baseDir, "Orleans.Sdk", "bin", "Release", "net8.0", "Granville.Orleans.Sdk.dll"),
                ["Orleans.Client"] = Path.Combine(baseDir, "Orleans.Client", "bin", "Release", "net8.0", "Granville.Orleans.Client.dll"),
                ["Orleans.CodeGenerator"] = Path.Combine(baseDir, "Orleans.CodeGenerator", "bin", "Release", "netstandard2.0", "Granville.Orleans.CodeGenerator.dll"),
                ["Orleans.Analyzers"] = Path.Combine(baseDir, "Orleans.Analyzers", "bin", "Release", "netstandard2.0", "Granville.Orleans.Analyzers.dll"),
            };

            // If args[0] is a specific Granville assembly path and args[1] is a specific output file, 
            // generate just that one assembly
            if (args.Length == 2 && args[0].EndsWith(".dll") && args[1].EndsWith(".dll"))
            {
                var granvilleAssemblyPath = args[0];
                var outputAssemblyPath = args[1];
                var shimAssemblyName = Path.GetFileNameWithoutExtension(outputAssemblyPath);
                
                try
                {
                    Console.WriteLine($"Generating single shim: {shimAssemblyName}...");
                    GenerateTypeForwardingAssembly(shimAssemblyName, granvilleAssemblyPath, actualOutputDir);
                    Console.WriteLine($"  ✓ Successfully generated {shimAssemblyName}.dll");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ✗ Error generating {shimAssemblyName}: {ex.Message}");
                    Environment.Exit(1);
                }
            }
            else
            {
                // Generate all type forwarding assemblies
                foreach (var kvp in assembliesMap)
                {
                    var orleansName = kvp.Key;
                    var granvillePath = kvp.Value;

                    try
                    {
                        Console.WriteLine($"Generating {orleansName}...");
                        GenerateTypeForwardingAssembly(orleansName, granvillePath, actualOutputDir);
                        Console.WriteLine($"  ✓ Successfully generated {orleansName}.dll");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ✗ Error generating {orleansName}: {ex.Message}");
                    }
                }
            }
        }

        static void GenerateTypeForwardingAssembly(string orleansAssemblyName, string granvilleAssemblyPath, string outputDir)
        {
            if (!File.Exists(granvilleAssemblyPath))
            {
                throw new FileNotFoundException($"Granville assembly not found: {granvilleAssemblyPath}");
            }

            // Set up a more comprehensive assembly resolver
            var targetDir = Path.GetDirectoryName(granvilleAssemblyPath);
            var loadedAssemblies = new Dictionary<string, Assembly>();
            
            // Try to preload common dependencies
            var commonDependencies = new[] {
                "Microsoft.Extensions.Options",
                "Microsoft.Extensions.DependencyInjection.Abstractions",
                "Microsoft.Extensions.Logging.Abstractions",
                "Microsoft.Extensions.ObjectPool",
                "Microsoft.Extensions.Hosting.Abstractions",
                "System.IO.Pipelines",
                "System.Threading.Tasks.Extensions",
                "Newtonsoft.Json"
            };
            
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var assemblyName = new AssemblyName(args.Name);
                var simpleName = assemblyName.Name;
                
                // Check if already loaded
                if (loadedAssemblies.ContainsKey(simpleName))
                {
                    return loadedAssemblies[simpleName];
                }
                
                // Try various naming patterns
                var possibleNames = new[]
                {
                    simpleName,
                    simpleName.Replace("Orleans.", "Granville.Orleans."),
                    simpleName.Replace("Microsoft.Orleans.", "Granville.Orleans."),
                    "Granville." + simpleName
                };
                
                foreach (var name in possibleNames)
                {
                    var possiblePath = Path.Combine(targetDir, name + ".dll");
                    if (File.Exists(possiblePath))
                    {
                        try
                        {
                            var asm = Assembly.LoadFrom(possiblePath);
                            loadedAssemblies[simpleName] = asm;
                            return asm;
                        }
                        catch { }
                    }
                }
                
                // Try runtime directory for system assemblies
                var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
                var runtimePath = Path.Combine(runtimeDir, simpleName + ".dll");
                if (File.Exists(runtimePath))
                {
                    try
                    {
                        var asm = Assembly.LoadFrom(runtimePath);
                        loadedAssemblies[simpleName] = asm;
                        return asm;
                    }
                    catch { }
                }
                
                // Try NuGet packages cache
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var nugetCache = Path.Combine(userProfile, ".nuget", "packages");
                if (Directory.Exists(nugetCache))
                {
                    // Common patterns for finding packages
                    var packagePatterns = new[] {
                        simpleName.ToLower(),
                        simpleName.Replace(".", "-").ToLower()
                    };
                    
                    foreach (var pattern in packagePatterns)
                    {
                        var packageDir = Path.Combine(nugetCache, pattern);
                        if (Directory.Exists(packageDir))
                        {
                            // Look for the highest version
                            var versions = Directory.GetDirectories(packageDir).OrderByDescending(d => d).ToArray();
                            foreach (var versionDir in versions)
                            {
                                // Try common target frameworks
                                var frameworks = new[] { "net8.0", "net7.0", "net6.0", "netstandard2.1", "netstandard2.0" };
                                foreach (var fw in frameworks)
                                {
                                    var dllPath = Path.Combine(versionDir, "lib", fw, simpleName + ".dll");
                                    if (File.Exists(dllPath))
                                    {
                                        try
                                        {
                                            var asm = Assembly.LoadFrom(dllPath);
                                            loadedAssemblies[simpleName] = asm;
                                            return asm;
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }
                    }
                }
                
                return null;
            };

            // Load the Granville assembly to get types and version
            var granvilleAssembly = Assembly.LoadFrom(granvilleAssemblyPath);
            var version = granvilleAssembly.GetName().Version;
            var granvilleName = Path.GetFileNameWithoutExtension(granvilleAssemblyPath);

            // Preload all dependencies from the target directory
            foreach (var dllPath in Directory.GetFiles(targetDir, "*.dll"))
            {
                try
                {
                    var asmName = Path.GetFileNameWithoutExtension(dllPath);
                    if (!loadedAssemblies.ContainsKey(asmName))
                    {
                        var asm = Assembly.LoadFrom(dllPath);
                        loadedAssemblies[asmName] = asm;
                    }
                }
                catch { }
            }

            // Check if the Granville assembly has InternalsVisibleTo for the shim assembly
            var granvilleAttributes = granvilleAssembly.GetCustomAttributes<InternalsVisibleToAttribute>();
            var hasInternalsVisibleTo = granvilleAttributes.Any(attr => 
                attr.AssemblyName == orleansAssemblyName || 
                attr.AssemblyName.StartsWith(orleansAssemblyName + ","));
            
            if (hasInternalsVisibleTo)
            {
                Console.WriteLine($"  ✓ Found InternalsVisibleTo for {orleansAssemblyName}, will include internal types");
            }

            // Get all types to forward
            var typesToForward = new List<Type>();
            var skippedTypes = new List<(string name, string reason)>();
            try
            {
                // Use GetTypes() instead of GetExportedTypes() for better control
                var allTypes = granvilleAssembly.GetTypes();
                foreach (var type in allTypes)
                {
                    try
                    {
                        // Skip compiler-generated types
                        if (type.Name.Contains("<") || type.Name.Contains(">"))
                        {
                            if (type.FullName?.Contains("TypeManifest") == true)
                                skippedTypes.Add((type.FullName, "compiler-generated"));
                            continue;
                        }
                        
                        // Skip special name types
                        if (type.IsSpecialName)
                        {
                            if (type.FullName?.Contains("TypeManifest") == true)
                                skippedTypes.Add((type.FullName, "special name"));
                            continue;
                        }
                        
                        // Skip nested types - TypeForwardedTo cannot forward nested types
                        if (type.IsNested)
                        {
                            if (type.FullName?.Contains("AsyncTimer") == true || type.FullName?.Contains("TestAccessor") == true)
                                skippedTypes.Add((type.FullName, "nested type (cannot be forwarded)"));
                            continue;
                        }
                        
                        // Skip certain system types that may conflict
                        if (type.FullName == "System.Runtime.CompilerServices.IsExternalInit" ||
                            type.Namespace == "System.Runtime.CompilerServices.Unsafe" ||
                            (type.Namespace == "System.Runtime.CompilerServices" && type.Name.StartsWith("Is")) ||
                            type.FullName == "Microsoft.CodeAnalysis.EmbeddedAttribute")
                        {
                            skippedTypes.Add((type.FullName, "system type that may conflict"));
                            continue;
                        }
                        
                        // Check visibility
                        bool shouldInclude = false;
                        if (type.IsPublic && type.IsVisible)
                        {
                            shouldInclude = true;
                        }
                        else if (hasInternalsVisibleTo)
                        {
                            // Include internal types when we have InternalsVisibleTo
                            var isInternalType = type.IsNotPublic;
                            if (isInternalType)
                            {
                                shouldInclude = true;
                            }
                        }
                        
                        if (shouldInclude)
                        {
                            typesToForward.Add(type);
                        }
                        else if (!type.IsPublic)
                        {
                            if (type.FullName?.Contains("TypeManifest") == true || type.FullName?.Contains("AsyncTimer") == true)
                                skippedTypes.Add((type.FullName, "not public/internal (no InternalsVisibleTo)"));
                        }
                    }
                    catch { }
                }
                
                if (skippedTypes.Count > 0)
                {
                    Console.WriteLine($"  Skipped {skippedTypes.Count} TypeManifest-related types:");
                    foreach (var (name, reason) in skippedTypes)
                    {
                        Console.WriteLine($"    - {name}: {reason}");
                    }
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                Console.WriteLine($"  Warning: ReflectionTypeLoadException - {ex.LoaderExceptions?.Length ?? 0} loader exceptions");
                
                // Log detailed information about loader exceptions
                if (ex.LoaderExceptions != null)
                {
                    var loaderErrors = new Dictionary<string, List<Exception>>();
                    foreach (var loaderEx in ex.LoaderExceptions)
                    {
                        if (loaderEx != null)
                        {
                            var key = loaderEx.GetType().Name;
                            if (!loaderErrors.ContainsKey(key))
                                loaderErrors[key] = new List<Exception>();
                            loaderErrors[key].Add(loaderEx);
                        }
                    }
                    
                    foreach (var errorGroup in loaderErrors)
                    {
                        Console.WriteLine($"    {errorGroup.Key} ({errorGroup.Value.Count} occurrences):");
                        // Show first few examples
                        foreach (var err in errorGroup.Value.Take(3))
                        {
                            Console.WriteLine($"      - {err.Message}");
                        }
                        if (errorGroup.Value.Count > 3)
                        {
                            Console.WriteLine($"      ... and {errorGroup.Value.Count - 3} more");
                        }
                    }
                }
                
                // Try to get the types that loaded successfully
                var loadedTypeCount = 0;
                var failedTypeManifestTypes = new List<string>();
                for (int i = 0; i < ex.Types.Length; i++)
                {
                    var type = ex.Types[i];
                    if (type != null && !type.Name.Contains("<") && !type.Name.Contains(">") && !type.IsNested)
                    {
                        bool shouldInclude = false;
                        if (type.IsPublic && type.IsVisible)
                        {
                            shouldInclude = true;
                        }
                        else if (hasInternalsVisibleTo)
                        {
                            var isInternalType = type.IsNotPublic;
                            if (isInternalType)
                            {
                                shouldInclude = true;
                            }
                        }
                        
                        if (shouldInclude)
                        {
                            typesToForward.Add(type);
                            loadedTypeCount++;
                        }
                    }
                    else if (type == null && ex.LoaderExceptions != null && i < ex.LoaderExceptions.Length)
                    {
                        // Log which type failed to load
                        var loaderEx = ex.LoaderExceptions[i];
                        if (loaderEx?.Message.Contains("TypeManifest") == true)
                        {
                            failedTypeManifestTypes.Add(loaderEx.Message);
                        }
                    }
                }
                
                Console.WriteLine($"    Loaded {loadedTypeCount} types successfully");
                if (failedTypeManifestTypes.Count > 0)
                {
                    Console.WriteLine($"    Types related to TypeManifest that failed to load:");
                    foreach (var failed in failedTypeManifestTypes.Take(5))
                    {
                        Console.WriteLine($"      - {failed}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: Could not get types: {ex.Message}");
                // If we completely failed to get any types, this is an error
                if (typesToForward.Count == 0)
                {
                    throw new InvalidOperationException($"Failed to extract any types from {granvilleAssemblyPath}: {ex.Message}");
                }
            }

            Console.WriteLine($"  Found {typesToForward.Count} types to forward (including {typesToForward.Count(t => !t.IsPublic)} internal types)");
            
            // Check for specific important types
            var importantTypes = new[] { "TypeManifestProviderBase", "IAsyncTimerFactory", "DefaultStorageProviderSerializerOptionsConfigurator" };
            foreach (var typeName in importantTypes)
            {
                var hasType = typesToForward.Any(t => t.Name == typeName || t.Name.StartsWith(typeName + "`"));
                if (!hasType)
                {
                    // Check if it exists but wasn't included
                    try
                    {
                        var allTypesDebug = granvilleAssembly.GetTypes();
                        var matchingTypes = allTypesDebug.Where(t => t.Name == typeName || t.Name.StartsWith(typeName + "`")).ToList();
                        if (matchingTypes.Any())
                        {
                            Console.WriteLine($"  WARNING: {typeName} exists but is NOT in the types to forward!");
                            foreach (var t in matchingTypes)
                            {
                                Console.WriteLine($"    - {t.FullName} (IsPublic: {t.IsPublic}, IsVisible: {t.IsVisible}, IsNotPublic: {t.IsNotPublic})");
                            }
                        }
                    }
                    catch { }
                }
            }

            // Generate C# source code
            var source = GenerateSourceCode(orleansAssemblyName, granvilleName, version, typesToForward, hasInternalsVisibleTo);

            // Debug: write source to file for inspection
            var debugSourcePath = Path.Combine(outputDir, orleansAssemblyName + ".cs");
            File.WriteAllText(debugSourcePath, source);

            // Compile the source code
            CompileAssembly(orleansAssemblyName, source, granvilleAssemblyPath, outputDir);
        }

        static string GenerateSourceCode(string assemblyName, string targetAssemblyName, Version version, List<Type> typesToForward, bool hasInternalsVisibleTo)
        {
            var sb = new StringBuilder();

            // Assembly names should already be in Orleans.* format
            var internalAssemblyName = assemblyName;
            var shimAssemblyName = assemblyName;
            
            // Assembly attributes
            sb.AppendLine("using System.Reflection;");
            sb.AppendLine("using System.Runtime.CompilerServices;");
            sb.AppendLine();
            sb.AppendLine($"[assembly: AssemblyTitle(\"{shimAssemblyName}\")]");
            sb.AppendLine($"[assembly: AssemblyDescription(\"Type forwarding shim for Granville Orleans compatibility\")]");
            sb.AppendLine($"[assembly: AssemblyCompany(\"Granville\")]");
            sb.AppendLine($"[assembly: AssemblyProduct(\"Granville Orleans Compatibility\")]");
            sb.AppendLine($"[assembly: AssemblyVersion(\"{version}\")]");
            sb.AppendLine($"[assembly: AssemblyFileVersion(\"{version}\")]");
            sb.AppendLine();

            // Type forwards - deduplicate by full type name
            sb.AppendLine("// Type forwards to " + targetAssemblyName);
            if (hasInternalsVisibleTo)
            {
                sb.AppendLine("// Note: InternalsVisibleTo in " + targetAssemblyName + " allows us to forward internal types");
            }
            var processedTypes = new HashSet<string>();
            foreach (var type in typesToForward)
            {
                var fullTypeName = GetFullTypeName(type);
                if (processedTypes.Add(fullTypeName))
                {
                    // For generic types, we need to use the backtick syntax for TypeForwardedTo
                    var typeForwardName = GetTypeForwardName(type);
                    if (typeForwardName != null)
                    {
                        sb.AppendLine($"[assembly: TypeForwardedTo(typeof({typeForwardName}))]");
                    }
                }
            }

            // Add a dummy namespace to make it a valid compilation unit
            sb.AppendLine();
            sb.AppendLine("namespace " + shimAssemblyName.Replace(".", "_"));
            sb.AppendLine("{");
            sb.AppendLine("    // This assembly contains only type forwards");
            sb.AppendLine("}");

            return sb.ToString();
        }

        static string GetTypeForwardName(Type type)
        {
            if (type == null)
            {
                return "System.Object";
            }

            // Skip compiler-generated types that have no proper FullName
            if (type.FullName == null || type.FullName.Contains("<"))
            {
                return null;
            }

            // For nested types, we need to use the fully qualified name with +
            if (type.IsNested)
            {
                // For nested types in TypeForwardedTo, we need to replace + with .
                var fullName = type.FullName.Replace('+', '.');
                
                // For generic nested types, handle the syntax properly
                if (type.IsGenericType && type.IsGenericTypeDefinition)
                {
                    // Replace the backtick notation with angle brackets
                    var genericParams = type.GetGenericArguments();
                    var placeholders = new string(',', genericParams.Length - 1);
                    
                    // Find the last backtick (for the nested type, not its parent)
                    var lastBacktick = fullName.LastIndexOf('`');
                    if (lastBacktick > 0)
                    {
                        // Extract the number after the backtick
                        var numStart = lastBacktick + 1;
                        var numEnd = numStart;
                        while (numEnd < fullName.Length && char.IsDigit(fullName[numEnd]))
                            numEnd++;
                        
                        // Remove the backtick and number
                        fullName = fullName.Substring(0, lastBacktick) + fullName.Substring(numEnd);
                        return $"{fullName}<{placeholders}>";
                    }
                }
                
                return fullName;
            }

            // For generic types, use angle bracket syntax
            if (type.IsGenericType)
            {
                var ns = type.Namespace;
                var name = type.Name;
                
                // Remove the backtick and number
                var backtickIndex = name.IndexOf('`');
                if (backtickIndex > 0)
                {
                    name = name.Substring(0, backtickIndex);
                }
                
                // For generic type definitions, use empty angle brackets
                if (type.IsGenericTypeDefinition)
                {
                    var genericParams = type.GetGenericArguments();
                    var placeholders = new string(',', genericParams.Length - 1);
                    var fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                    return $"{fullName}<{placeholders}>";
                }
                
                // For closed generic types, we shouldn't be seeing them in type forwards
                var genericArgs = type.GetGenericArguments().Select(GetTypeForwardName);
                var baseName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                return $"{baseName}<{string.Join(", ", genericArgs)}>";
            }

            // For non-generic types, just use the full name
            var typeName = type.FullName ?? type.Name;
            return typeName;
        }

        static string GetFullTypeName(Type type)
        {
            if (type == null)
            {
                return "System.Object"; // Fallback
            }

            // For nested types, we need to use the declaring type's namespace
            if (type.IsNested)
            {
                var declaringType = type.DeclaringType;
                var declaringTypeName = GetFullTypeName(declaringType);
                return $"{declaringTypeName}.{type.Name}";
            }

            // For generic types, we need to get the proper full name
            if (type.IsGenericType)
            {
                var ns = type.Namespace;
                var name = type.Name;
                
                // For open generic types (type definitions), include the generic parameter placeholders
                if (type.IsGenericTypeDefinition)
                {
                    var genericParams = type.GetGenericArguments();
                    var paramNames = new string[genericParams.Length];
                    for (int i = 0; i < genericParams.Length; i++)
                    {
                        // Use just the parameter names (T, TKey, etc)
                        paramNames[i] = genericParams[i].Name;
                    }
                    
                    // Remove the backtick and number from generic type names
                    if (name.Contains('`'))
                    {
                        name = name.Substring(0, name.IndexOf('`'));
                    }
                    
                    var fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                    return $"{fullName}<{string.Join(", ", paramNames)}>";
                }
                
                // For closed generic types, include the actual type arguments
                // Remove the backtick and number from generic type names
                if (name.Contains('`'))
                {
                    name = name.Substring(0, name.IndexOf('`'));
                }
                
                var genericArgs = type.GetGenericArguments().Select(GetFullTypeName);
                var baseName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                return $"{baseName}<{string.Join(", ", genericArgs)}>";
            }

            // For non-generic types, just use the full name
            var typeName = type.FullName ?? type.Name;
            return typeName.Replace('+', '.');
        }

        static void CompileAssembly(string assemblyName, string sourceCode, string referencePath, string outputDir)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

            // Get reference assemblies
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(TypeForwardedToAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(referencePath)
            };

            // Add references from the same directory as the target assembly
            var targetDir = Path.GetDirectoryName(referencePath);
            foreach (var dll in Directory.GetFiles(targetDir, "*.dll"))
            {
                if (dll != referencePath && !Path.GetFileName(dll).StartsWith("Microsoft."))
                {
                    try
                    {
                        references.Add(MetadataReference.CreateFromFile(dll));
                    }
                    catch
                    {
                        // Ignore dlls we can't reference
                    }
                }
            }

            // Add basic runtime assemblies
            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            foreach (var assembly in new[] { "System.Runtime.dll", "System.Collections.dll", "netstandard.dll", "System.Threading.dll" })
            {
                var path = Path.Combine(runtimeDir, assembly);
                if (File.Exists(path))
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }
            }

            // Use Orleans.* as the internal assembly name for the shim (not Granville.Orleans.*)
            var internalAssemblyName = assemblyName.StartsWith("Microsoft.")
                ? assemblyName.Substring("Microsoft.".Length)
                : assemblyName;
                
            var compilation = CSharpCompilation.Create(
                internalAssemblyName,  // Use Orleans.Core as assembly name for the shim
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    platform: Platform.AnyCpu
                ));

            var outputPath = Path.Combine(outputDir, assemblyName + ".dll");
            var result = compilation.Emit(outputPath);

            if (!result.Success)
            {
                var errors = result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString());
                throw new InvalidOperationException($"Compilation failed:\n{string.Join("\n", errors)}");
            }
        }
    }
}