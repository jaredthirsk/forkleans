#r "nuget: Microsoft.CodeAnalysis.CSharp.Workspaces, 4.0.1"
#r "nuget: Microsoft.Build.Locator, 1.4.1"

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Build.Locator;

class ApiAnalyzer
{
    public static async Task AnalyzeProjects(params string[] projectPaths)
    {
        MSBuildLocator.RegisterDefaults();
        
        using var workspace = MSBuildWorkspace.Create();
        
        foreach (var projectPath in projectPaths)
        {
            try
            {
                var project = await workspace.OpenProjectAsync(projectPath);
                await AnalyzeProject(project);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading {projectPath}: {ex.Message}");
            }
        }
    }
    
    private static async Task AnalyzeProject(Project project)
    {
        Console.WriteLine($"\n=== {project.Name} ===");
        
        var compilation = await project.GetCompilationAsync();
        if (compilation == null) return;
        
        var publicTypes = new Dictionary<string, List<INamedTypeSymbol>>();
        var namespaces = new HashSet<string>();
        
        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var root = await tree.GetRootAsync();
            
            // Find all type declarations
            var typeDeclarations = root.DescendantNodes()
                .OfType<BaseTypeDeclarationSyntax>()
                .Where(t => t.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)));
            
            foreach (var typeDecl in typeDeclarations)
            {
                var symbol = semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
                if (symbol?.DeclaredAccessibility == Accessibility.Public)
                {
                    var ns = symbol.ContainingNamespace?.ToString() ?? "Global";
                    namespaces.Add(ns);
                    
                    var kind = symbol.TypeKind.ToString();
                    if (\!publicTypes.ContainsKey(kind))
                        publicTypes[kind] = new List<INamedTypeSymbol>();
                    publicTypes[kind].Add(symbol);
                }
            }
        }
        
        // Output results
        Console.WriteLine($"Total public types: {publicTypes.Values.Sum(l => l.Count)}");
        Console.WriteLine("\nBreakdown by type:");
        foreach (var kvp in publicTypes.OrderBy(k => k.Key))
        {
            Console.WriteLine($"  {kvp.Key}: {kvp.Value.Count}");
        }
        
        Console.WriteLine($"\nNamespaces ({namespaces.Count}):");
        foreach (var ns in namespaces.OrderBy(n => n).Take(10))
        {
            Console.WriteLine($"  {ns}");
        }
        if (namespaces.Count > 10)
            Console.WriteLine($"  ... and {namespaces.Count - 10} more");
    }
}

// Execute analysis
await ApiAnalyzer.AnalyzeProjects(
    "./src/Orleans.Core/Orleans.Core.csproj",
    "./src/Orleans.Runtime/Orleans.Runtime.csproj",
    "./src/Orleans.Serialization/Orleans.Serialization.csproj"
);
