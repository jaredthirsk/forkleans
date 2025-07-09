using Orleans;
using Orleans.Runtime;

namespace TestCodeGen
{
    public class TestClass
    {
        public void TestMethod()
        {
            var grainId = GrainId.Create("test", "key");
            var siloAddress = SiloAddress.New("localhost", 11111);
        }
    }
}
