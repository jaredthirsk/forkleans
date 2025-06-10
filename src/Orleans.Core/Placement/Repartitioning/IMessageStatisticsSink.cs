#nullable enable
using System;
using Forkleans.Runtime;

namespace Forkleans.Placement.Repartitioning;

internal interface IMessageStatisticsSink
{
    Action<Message>? GetMessageObserver();
}

internal sealed class NoOpMessageStatisticsSink : IMessageStatisticsSink
{
    public Action<Message>? GetMessageObserver() => null;
}