using Forkleans.Core;

namespace Forkleans.Runtime
{
    /// <summary>
    /// Provides access to grain state with functionality to save, clear, and refresh the state.
    /// </summary>
    /// <typeparam name="TState">The underlying state type.</typeparam>
    /// <seealso cref="Forkleans.Core.IStorage{TState}" />
    public interface IPersistentState<TState> : IStorage<TState>
    {
    }
}
