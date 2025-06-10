using System.Threading.Tasks;
using Forkleans.Metadata;

namespace Forkleans.Runtime
{
    internal interface ISiloManifestSystemTarget : ISystemTarget
    {
        ValueTask<GrainManifest> GetSiloManifest();
    }
}