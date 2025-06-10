namespace TestGrainInterfaces
{
    public enum GenderType
    {
        Male,
        Female
    }

    [Serializable]
    [Forkleans.GenerateSerializer]
    public class PersonAttributes
    {
        [Forkleans.Id(0)]
        public string FirstName { get; set; }
        [Forkleans.Id(1)]
        public string LastName { get; set; }
        [Forkleans.Id(2)]
        public GenderType Gender { get; set; }
    }

    /// <summary>
    /// Orleans grain communication interface IPerson
    /// </summary>
    public interface IPersonGrain : Forkleans.IGrainWithGuidKey
    {
        Task RegisterBirth(PersonAttributes person);
        Task Marry(IPersonGrain spouse);

        Task<PersonAttributes> GetTentativePersonalAttributes();

        // Tests

        Task RunTentativeConfirmedStateTest();
    }
}
