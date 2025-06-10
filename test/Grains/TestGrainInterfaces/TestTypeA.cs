namespace UnitTests.GrainInterfaces
{
    [Serializable]
    [Forkleans.GenerateSerializer]
    public class TestTypeA
    {
        [Forkleans.Id(0)]
        public ICollection<TestTypeA> Collection { get; set; }
    }
}
