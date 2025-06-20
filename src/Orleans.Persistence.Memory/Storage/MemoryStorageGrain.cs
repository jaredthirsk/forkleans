using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Forkleans.Storage.Internal;

namespace Forkleans.Storage
{
    /// <summary>
    /// Implementation class for the Storage Grain used by In-memory storage provider
    /// <c>Forkleans.Storage.MemoryStorage</c>
    /// </summary>
    [KeepAlive]
    internal partial class MemoryStorageGrain : Grain, IMemoryStorageGrain
    {
        private readonly Dictionary<string, object> _store = new();
        private readonly ILogger _logger;

        public MemoryStorageGrain(ILogger<MemoryStorageGrain> logger)
        {
            _logger = logger;
        }

        public Task<IGrainState<T>> ReadStateAsync<T>(string grainStoreKey)
        {
            LogDebugReadState(grainStoreKey);
            _store.TryGetValue(grainStoreKey, out var entry);
            return Task.FromResult((IGrainState<T>)entry);
        }

        public Task<string> WriteStateAsync<T>(string grainStoreKey, IGrainState<T> grainState)
        {
            LogDebugWriteState(grainStoreKey, grainState.ETag);
            var currentETag = GetETagFromStorage<T>(grainStoreKey);
            ValidateEtag(currentETag, grainState.ETag, grainStoreKey, "Update");
            grainState.ETag = NewEtag();
            _store[grainStoreKey] = grainState;
            LogDebugDoneWriteState(grainStoreKey, grainState.ETag);
            return Task.FromResult(grainState.ETag);
        }

        public Task DeleteStateAsync<T>(string grainStoreKey, string etag)
        {
            LogDebugDeleteState(grainStoreKey, etag);

            var currentETag = GetETagFromStorage<T>(grainStoreKey);
            ValidateEtag(currentETag, etag, grainStoreKey, "Delete");
            // Do not remove it from the dictionary, just set the value to null to remember that this item
            // was once in the store, and now is deleted
            _store[grainStoreKey] = null;
            return Task.CompletedTask;
        }

        private static string NewEtag()
        {
            return Guid.NewGuid().ToString("N");
        }

        private string GetETagFromStorage<T>(string grainStoreKey)
        {
            string currentETag = null;
            if (_store.TryGetValue(grainStoreKey, out var entry))
            {
                // If the entry is null, it was removed from storage
                currentETag = entry != null ? ((IGrainState<T>)entry).ETag : string.Empty;
            }
            return currentETag;
        }

        private void ValidateEtag(string currentETag, string receivedEtag, string grainStoreKey, string operation)
        {
            // if we have no current etag, we will accept the users data.
            // This is a mitigation for when the memory storage grain is lost due to silo crash.
            if (currentETag == null)
                return;

            // if this is our first write, and we have an empty etag, we're good
            if (string.IsNullOrEmpty(currentETag) && receivedEtag == null)
                return;

            // if current state and new state have matching etags, or we're to ignore the ETag, we're good
            if (receivedEtag == currentETag || receivedEtag == "*")
                return;

            // else we have an etag mismatch
            LogWarningEtagMismatch(operation, grainStoreKey, currentETag, receivedEtag);
            throw new MemoryStorageEtagMismatchException(currentETag, receivedEtag);
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "ReadStateAsync for grain: {GrainStoreKey}"
        )]
        private partial void LogDebugReadState(string grainStoreKey);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "WriteStateAsync for grain: {GrainStoreKey} eTag: {ETag}"
        )]
        private partial void LogDebugWriteState(string grainStoreKey, string etag);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Done WriteStateAsync for grain: {GrainStoreKey} eTag: {ETag}"
        )]
        private partial void LogDebugDoneWriteState(string grainStoreKey, string etag);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "DeleteStateAsync for grain: {GrainStoreKey} eTag: {ETag}"
        )]
        private partial void LogDebugDeleteState(string grainStoreKey, string etag);

        [LoggerMessage(
            Level = LogLevel.Warning,
            EventId = 0,
            Message = "Etag mismatch during {Operation} for grain {GrainStoreKey}: Expected = {Expected} Received = {Received}"
        )]
        private partial void LogWarningEtagMismatch(string operation, string grainStoreKey, string expected, string received);
    }
}
