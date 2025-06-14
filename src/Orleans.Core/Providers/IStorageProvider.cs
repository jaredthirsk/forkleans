using System;
using System.Runtime.Serialization;
using Forkleans.Runtime;

namespace Forkleans.Storage
{
    /// <summary>
    /// Exception thrown whenever a grain call is attempted with a bad / missing storage provider configuration settings for that grain.
    /// </summary>
    [Serializable, GenerateSerializer]
    public sealed class BadProviderConfigException : ForkleansException
    {
        public BadProviderConfigException()
        { }
        public BadProviderConfigException(string msg)
            : base(msg)
        { }
        public BadProviderConfigException(string msg, Exception exc)
            : base(msg, exc)
        { }

        [Obsolete]
        private BadProviderConfigException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        { }
    }
}
