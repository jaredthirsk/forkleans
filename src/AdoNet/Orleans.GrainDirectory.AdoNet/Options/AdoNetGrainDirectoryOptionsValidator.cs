using static System.String;

namespace Forkleans.Configuration;

/// <summary>
/// Validates <see cref="AdoNetGrainDirectoryOptions"/> configuration.
/// </summary>
public class AdoNetGrainDirectoryOptionsValidator(AdoNetGrainDirectoryOptions options, string name) : IConfigurationValidator
{
    /// <inheritdoc />
    public void ValidateConfiguration()
    {
        if (options is null)
        {
            throw new ForkleansConfigurationException($"Invalid {nameof(AdoNetGrainDirectoryOptions)} values for {nameof(AdoNetGrainDirectory)}|{name}. {nameof(options)} is required.");
        }

        if (IsNullOrWhiteSpace(options.Invariant))
        {
            throw new ForkleansConfigurationException($"Invalid {nameof(AdoNetGrainDirectoryOptions)} values for {nameof(AdoNetGrainDirectory)}|{name}. {nameof(options.Invariant)} is required.");
        }

        if (IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new ForkleansConfigurationException($"Invalid {nameof(AdoNetGrainDirectoryOptions)} values for {nameof(AdoNetGrainDirectory)}|{name}. {nameof(options.ConnectionString)} is required.");
        }
    }
}
