using Microsoft.Extensions.Options;
using Forkleans.Configuration;
using Forkleans.Internal;
using Forkleans.Placement.Rebalancing;

namespace Forkleans.Runtime.Placement.Rebalancing;

internal sealed class FailedSessionBackoffProvider(IOptions<ActivationRebalancerOptions> options)
    : FixedBackoff(options.Value.SessionCyclePeriod), IFailedSessionBackoffProvider;