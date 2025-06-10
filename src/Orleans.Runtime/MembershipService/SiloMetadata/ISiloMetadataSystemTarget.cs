using System.Threading.Tasks;

#nullable enable
namespace Forkleans.Runtime.MembershipService.SiloMetadata;

[Alias("Forkleans.Runtime.MembershipService.SiloMetadata.ISiloMetadataSystemTarget")]
internal interface ISiloMetadataSystemTarget : ISystemTarget
{
    [Alias("GetSiloMetadata")]
    Task<SiloMetadata> GetSiloMetadata();
}