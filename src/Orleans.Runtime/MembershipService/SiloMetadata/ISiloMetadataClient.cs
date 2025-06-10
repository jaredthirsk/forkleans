using System.Threading.Tasks;

#nullable enable
namespace Forkleans.Runtime.MembershipService.SiloMetadata;

internal interface ISiloMetadataClient
{
    Task<SiloMetadata> GetSiloMetadata(SiloAddress siloAddress);
}
