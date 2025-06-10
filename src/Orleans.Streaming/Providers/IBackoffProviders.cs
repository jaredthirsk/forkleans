using Forkleans.Internal;
using Forkleans.Streams;

namespace Forkleans.Runtime.Providers;

/// <summary>
/// Functionality for determining how long the <see cref="IPersistentStreamPullingAgent"/> will wait between successive attempts to deliver a message.
/// </summary>
public interface IMessageDeliveryBackoffProvider : IBackoffProvider { }

/// <summary>
/// Functionality for determining how long the <see cref="IPersistentStreamPullingAgent"/> will wait between successive attempts to read a message from a queue.
/// </summary>
public interface IQueueReaderBackoffProvider : IBackoffProvider { }
