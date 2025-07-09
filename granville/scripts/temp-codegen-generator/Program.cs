using Orleans;
using Orleans.Runtime;

// Reference types to trigger code generation
public static class TypeReferences
{
    public static void Reference()
    {
        _ = typeof(GrainId);
        _ = typeof(SiloAddress);
        _ = typeof(ActivationId);
        _ = typeof(MembershipVersion);
        _ = typeof(GrainType);
        _ = typeof(GrainInterfaceType);
        _ = typeof(Orleans.Metadata.ClusterManifest);
        _ = typeof(Orleans.Metadata.GrainManifest);
    }
}
