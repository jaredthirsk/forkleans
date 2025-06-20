using System;
using System.Runtime.Serialization;

namespace Forkleans.Runtime.MembershipService
{
    /// <summary>
    /// Exception used to indicate that a cluster membership entry which was expected to be present.
    /// </summary>
    /// <seealso cref="Forkleans.Runtime.ForkleansException" />
    [Serializable]
    [GenerateSerializer]
    public sealed class OrleansMissingMembershipEntryException : ForkleansException
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="OrleansMissingMembershipEntryException"/> class.
        /// </summary>
        public OrleansMissingMembershipEntryException() : base("Membership table does not contain information an entry for this silo.") { }

        /// <summary>
        /// Initializes a new instance of the <see cref="OrleansMissingMembershipEntryException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        public OrleansMissingMembershipEntryException(string message) : base(message) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="OrleansMissingMembershipEntryException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The inner exception.</param>
        public OrleansMissingMembershipEntryException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="OrleansMissingMembershipEntryException"/> class.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The context.</param>
        [Obsolete]
        private OrleansMissingMembershipEntryException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
