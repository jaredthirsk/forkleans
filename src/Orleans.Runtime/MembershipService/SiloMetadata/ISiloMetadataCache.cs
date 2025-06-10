#nullable enable

namespace Forkleans.Runtime.MembershipService.SiloMetadata;

public interface ISiloMetadataCache
{
    SiloMetadata GetSiloMetadata(SiloAddress siloAddress);
}