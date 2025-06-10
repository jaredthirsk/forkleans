using System.Data;

#if CLUSTERING_ADONET
namespace Forkleans.Clustering.AdoNet.Storage
#elif PERSISTENCE_ADONET
namespace Forkleans.Persistence.AdoNet.Storage
#elif REMINDERS_ADONET
namespace Forkleans.Reminders.AdoNet.Storage
#elif STREAMING_ADONET
namespace Forkleans.Streaming.AdoNet.Storage
#elif GRAINDIRECTORY_ADONET
namespace Forkleans.GrainDirectory.AdoNet.Storage
#elif TESTER_SQLUTILS
namespace Forkleans.Tests.SqlUtils
#else
// No default namespace intentionally to cause compile errors if something is not defined
#endif
{
    internal class NoOpCommandInterceptor : ICommandInterceptor
    {
        public static readonly ICommandInterceptor Instance = new NoOpCommandInterceptor();

        private NoOpCommandInterceptor()
        {
            
        }

        public void Intercept(IDbCommand command)
        {
            //NOP
        }
    }
}
