using System.Runtime.CompilerServices;

// Granville Orleans assemblies
[assembly: InternalsVisibleTo("Granville.Orleans.Runtime")]
[assembly: InternalsVisibleTo("Granville.Orleans.Core")]

// Orleans shim assemblies need access for compatibility
[assembly: InternalsVisibleTo("Orleans.Runtime")]
[assembly: InternalsVisibleTo("Orleans.Core")]
[assembly: InternalsVisibleTo("Orleans.Serialization")]