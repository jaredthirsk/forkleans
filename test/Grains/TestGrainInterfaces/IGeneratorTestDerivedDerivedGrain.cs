namespace UnitTests.GrainInterfaces
{
    [Serializable]
    [Forkleans.GenerateSerializer]
    public class ReplaceArguments
    {
        [Forkleans.Id(0)]
        public string OldString { get; private set; }
        [Forkleans.Id(1)]
        public string NewString { get; private set; }

        public ReplaceArguments(string oldStr, string newStr)
        {
            OldString = oldStr;
            NewString = newStr;
        }
    }

    public interface IGeneratorTestDerivedDerivedGrain : IGeneratorTestDerivedGrain2
    {
        Task<string> StringNConcat(string[] strArray);
        Task<string> StringReplace(ReplaceArguments strs);
    }
}