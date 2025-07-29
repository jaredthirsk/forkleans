using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Granville.Rpc.Multiplexing
{
    /// <summary>
    /// Strategy for determining which server should handle a grain request.
    /// </summary>
    public interface IGrainRoutingStrategy
    {
        /// <summary>
        /// Selects the appropriate server for a grain request.
        /// </summary>
        /// <param name="grainInterface">The grain interface type.</param>
        /// <param name="grainKey">The grain's primary key.</param>
        /// <param name="servers">Available servers indexed by ServerId.</param>
        /// <param name="context">Routing context with additional information.</param>
        /// <returns>The ServerId of the selected server, or null if no suitable server found.</returns>
        Task<string> SelectServerAsync(
            Type grainInterface,
            string grainKey,
            IReadOnlyDictionary<string, IServerDescriptor> servers,
            IRoutingContext context);
    }

    /// <summary>
    /// Context information for routing decisions.
    /// </summary>
    public interface IRoutingContext
    {
        /// <summary>
        /// Gets a property value from the context.
        /// </summary>
        T GetProperty<T>(string key);

        /// <summary>
        /// Sets a property value in the context.
        /// </summary>
        void SetProperty<T>(string key, T value);

        /// <summary>
        /// Checks if a property exists in the context.
        /// </summary>
        bool HasProperty(string key);

        /// <summary>
        /// Removes a property from the context.
        /// </summary>
        bool RemoveProperty(string key);

        /// <summary>
        /// Gets all property keys in the context.
        /// </summary>
        IEnumerable<string> GetPropertyKeys();
    }

    /// <summary>
    /// Default implementation of IRoutingContext.
    /// </summary>
    public class RoutingContext : IRoutingContext
    {
        private readonly Dictionary<string, object> _properties = new();

        public T GetProperty<T>(string key)
        {
            if (_properties.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }
            return default(T);
        }

        public void SetProperty<T>(string key, T value)
        {
            _properties[key] = value;
        }

        public bool HasProperty(string key)
        {
            return _properties.ContainsKey(key);
        }

        public bool RemoveProperty(string key)
        {
            return _properties.Remove(key);
        }

        public IEnumerable<string> GetPropertyKeys()
        {
            return _properties.Keys;
        }

        /// <summary>
        /// Indexer for convenient property access.
        /// </summary>
        public object this[string key]
        {
            get => _properties.TryGetValue(key, out var value) ? value : null;
            set => _properties[key] = value;
        }
    }
}