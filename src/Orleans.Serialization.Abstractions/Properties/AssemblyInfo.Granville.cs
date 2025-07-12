using System.Runtime.CompilerServices;

// Granville Orleans assemblies
[assembly: InternalsVisibleTo("Granville.Orleans.Runtime")]
[assembly: InternalsVisibleTo("Granville.Orleans.Core")]
[assembly: InternalsVisibleTo("Granville.Orleans.Serialization")]

// Orleans shim assemblies need access for compatibility
[assembly: InternalsVisibleTo("Orleans.Serialization")]
[assembly: InternalsVisibleTo("Orleans.Serialization.Abstractions")]
[assembly: InternalsVisibleTo("Orleans.Runtime")]