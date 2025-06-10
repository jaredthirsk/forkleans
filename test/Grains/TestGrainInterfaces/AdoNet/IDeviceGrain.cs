namespace Forkleans.SqlUtils.StorageProvider.GrainInterfaces
{
    public interface IDeviceGrain : IGrainWithGuidKey
    {
        Task<string> GetSerialNumber();

        Task SetOwner(ICustomerGrain customer);
    }
}