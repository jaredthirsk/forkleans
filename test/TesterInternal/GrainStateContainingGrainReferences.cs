using Forkleans.Runtime;

namespace TesterInternal
{
    [Serializable]
    [Forkleans.GenerateSerializer]
    public class GrainStateContainingGrainReferences
    {
        [Forkleans.Id(0)]
        public IAddressable Grain { get; set; }
        [Forkleans.Id(1)]
        public List<IAddressable> GrainList { get; set; }
        [Forkleans.Id(2)]
        public Dictionary<string, IAddressable> GrainDict { get; set; }

        public GrainStateContainingGrainReferences()
        {
            GrainList = new List<IAddressable>();
            GrainDict = new Dictionary<string, IAddressable>();
        }
    }
}
