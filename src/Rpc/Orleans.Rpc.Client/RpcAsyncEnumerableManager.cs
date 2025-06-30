using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Serialization;

namespace Granville.Rpc
{
    /// <summary>
    /// Manages IAsyncEnumerable operations on the client side.
    /// This is distinct from Orleans streams which are pub/sub based.
    /// </summary>
    internal class RpcAsyncEnumerableManager
    {
        private readonly ILogger<RpcAsyncEnumerableManager> _logger;
        private readonly Serializer _serializer;
        private readonly ConcurrentDictionary<Guid, AsyncEnumerableOperation> _activeOperations = new();

        public RpcAsyncEnumerableManager(ILogger<RpcAsyncEnumerableManager> logger, Serializer serializer)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        /// <summary>
        /// Creates a new async enumerable operation and returns an IAsyncEnumerable.
        /// </summary>
        public IAsyncEnumerable<T> CreateStream<T>(Guid streamId, CancellationToken cancellationToken)
        {
            var channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });

            var operation = new AsyncEnumerableOperation<T>
            {
                StreamId = streamId,
                Channel = channel,
                ItemType = typeof(T),
                CancellationToken = cancellationToken,
                StartedAt = DateTime.UtcNow
            };

            if (!_activeOperations.TryAdd(streamId, operation))
            {
                throw new InvalidOperationException($"Stream {streamId} already exists");
            }

            _logger.LogDebug("Created async enumerable operation {StreamId} for type {Type}", streamId, typeof(T).Name);

            // Register cancellation
            cancellationToken.Register(() => CancelStream(streamId));

            return ReadFromChannel(operation);
        }

        /// <summary>
        /// Processes an async enumerable item received from the server.
        /// </summary>
        public async Task ProcessAsyncEnumerableItem(Protocol.RpcAsyncEnumerableItem item)
        {
            if (!_activeOperations.TryGetValue(item.StreamId, out var operation))
            {
                _logger.LogWarning("Received async enumerable item for unknown stream {StreamId}", item.StreamId);
                return;
            }

            try
            {
                if (item.IsComplete)
                {
                    // Stream completed
                    if (!string.IsNullOrEmpty(item.ErrorMessage))
                    {
                        // Stream failed with error
                        _logger.LogError("Stream {StreamId} failed: {Error}", item.StreamId, item.ErrorMessage);
                        await operation.SetError(new Exception(item.ErrorMessage));
                    }
                    else
                    {
                        // Stream completed successfully
                        _logger.LogDebug("Stream {StreamId} completed", item.StreamId);
                        await operation.Complete();
                    }
                    
                    _activeOperations.TryRemove(item.StreamId, out _);
                }
                else if (item.ItemData != null && item.ItemData.Length > 0)
                {
                    // Process stream item
                    // Deserialize using JSON for now (matching server-side serialization)
                    var json = System.Text.Encoding.UTF8.GetString(item.ItemData);
                    var value = System.Text.Json.JsonSerializer.Deserialize(json, operation.ItemType);
                    await operation.AddItem(value);
                    
                    _logger.LogTrace("Processed item {SequenceNumber} for stream {StreamId}", 
                        item.SequenceNumber, item.StreamId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing async enumerable item for stream {StreamId}", item.StreamId);
                await operation.SetError(ex);
                _activeOperations.TryRemove(item.StreamId, out _);
            }
        }

        /// <summary>
        /// Cancels an async enumerable operation.
        /// </summary>
        public void CancelStream(Guid streamId)
        {
            if (_activeOperations.TryRemove(streamId, out var operation))
            {
                _logger.LogDebug("Cancelling stream {StreamId}", streamId);
                operation.Cancel();
            }
        }

        private async IAsyncEnumerable<T> ReadFromChannel<T>(AsyncEnumerableOperation<T> operation, 
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var reader = operation.Channel.Reader;
            
            try
            {
                await foreach (var item in reader.ReadAllAsync(cancellationToken))
                {
                    yield return item;
                }
            }
            finally
            {
                _activeOperations.TryRemove(operation.StreamId, out _);
            }
        }

        private abstract class AsyncEnumerableOperation
        {
            public Guid StreamId { get; set; }
            public Type ItemType { get; set; } = typeof(object);
            public CancellationToken CancellationToken { get; set; }
            public DateTime StartedAt { get; set; }

            public abstract Task AddItem(object item);
            public abstract Task Complete();
            public abstract Task SetError(Exception error);
            public abstract void Cancel();
        }

        private class AsyncEnumerableOperation<T> : AsyncEnumerableOperation
        {
            public Channel<T> Channel { get; set; } = null!;

            public override async Task AddItem(object item)
            {
                if (item is T typedItem)
                {
                    await Channel.Writer.WriteAsync(typedItem, CancellationToken);
                }
                else
                {
                    throw new InvalidCastException($"Cannot cast {item?.GetType()} to {typeof(T)}");
                }
            }

            public override Task Complete()
            {
                Channel.Writer.TryComplete();
                return Task.CompletedTask;
            }

            public override Task SetError(Exception error)
            {
                Channel.Writer.TryComplete(error);
                return Task.CompletedTask;
            }

            public override void Cancel()
            {
                Channel.Writer.TryComplete(new OperationCanceledException());
            }
        }
    }
}