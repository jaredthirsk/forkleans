//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------
namespace Forkleans.Configuration
{
    public partial class AdoNetReminderTableOptions
    {
        [Redact]
        public string ConnectionString { get { throw null; } set { } }

        public string Invariant { get { throw null; } set { } }
    }

    public partial class AdoNetReminderTableOptionsValidator : IConfigurationValidator
    {
        public AdoNetReminderTableOptionsValidator(Microsoft.Extensions.Options.IOptions<AdoNetReminderTableOptions> options) { }

        public void ValidateConfiguration() { }
    }
}

namespace Forkleans.Hosting
{
    public static partial class SiloBuilderReminderExtensions
    {
        public static Microsoft.Extensions.DependencyInjection.IServiceCollection UseAdoNetReminderService(this Microsoft.Extensions.DependencyInjection.IServiceCollection services, System.Action<Microsoft.Extensions.Options.OptionsBuilder<Configuration.AdoNetReminderTableOptions>> configureOptions) { throw null; }

        public static ISiloBuilder UseAdoNetReminderService(this ISiloBuilder builder, System.Action<Microsoft.Extensions.Options.OptionsBuilder<Configuration.AdoNetReminderTableOptions>> configureOptions) { throw null; }

        public static ISiloBuilder UseAdoNetReminderService(this ISiloBuilder builder, System.Action<Configuration.AdoNetReminderTableOptions> configureOptions) { throw null; }
    }
}

namespace Forkleans.Reminders.AdoNet.Storage
{
    public partial class ForkleansRelationalDownloadStream : System.IO.Stream
    {
        public ForkleansRelationalDownloadStream(System.Data.Common.DbDataReader reader, int ordinal) { }

        public override bool CanRead { get { throw null; } }

        public override bool CanSeek { get { throw null; } }

        public override bool CanTimeout { get { throw null; } }

        public override bool CanWrite { get { throw null; } }

        public override long Length { get { throw null; } }

        public override long Position { get { throw null; } set { } }

        public override System.Threading.Tasks.Task CopyToAsync(System.IO.Stream destination, int bufferSize, System.Threading.CancellationToken cancellationToken) { throw null; }

        protected override void Dispose(bool disposing) { }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) { throw null; }

        public override System.Threading.Tasks.Task<int> ReadAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken) { throw null; }

        public override long Seek(long offset, System.IO.SeekOrigin origin) { throw null; }

        public override void SetLength(long value) { }

        public override void Write(byte[] buffer, int offset, int count) { }
    }
}