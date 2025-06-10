using System;
using Forkleans.Configuration;

namespace Forkleans.Runtime.MembershipService;

internal static class MembershipTableEntryExtensions
{
    public static bool HasMissedIAmAlives(this MembershipEntry entry, ClusterMembershipOptions options, DateTime time)
        => time - entry.EffectiveIAmAliveTime > options.AllowedIAmAliveMissPeriod;
}
