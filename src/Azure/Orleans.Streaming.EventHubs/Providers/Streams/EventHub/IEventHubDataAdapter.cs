using System;
using Azure.Messaging.EventHubs;
using Forkleans.Providers.Streams.Common;
using Forkleans.Runtime;
using Forkleans.Streams;

namespace Forkleans.Streaming.EventHubs
{
    public interface IEventHubDataAdapter : IQueueDataAdapter<EventData>, ICacheDataAdapter
    {
        CachedMessage FromQueueMessage(StreamPosition position, EventData queueMessage, DateTime dequeueTime, Func<int, ArraySegment<byte>> getSegment);

        StreamPosition GetStreamPosition(string partition, EventData queueMessage);

        string GetOffset(CachedMessage cachedMessage);

        string GetPartitionKey(StreamId streamId);

        StreamId GetStreamIdentity(EventData queueMessage);
    }
}
