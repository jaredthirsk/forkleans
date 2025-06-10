using System;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Forkleans.Runtime;
using Forkleans.Serialization;
using Forkleans.Transactions.Abstractions;

namespace Forkleans.Transactions
{
    public class TransactionalStateFactory : ITransactionalStateFactory
    {
        private readonly IGrainContextAccessor contextAccessor;
        public TransactionalStateFactory(IGrainContextAccessor contextAccessor)
        {
            this.contextAccessor = contextAccessor;
        }

        public ITransactionalState<TState> Create<TState>(TransactionalStateConfiguration config) where TState : class, new()
        {
            var currentContext = this.contextAccessor.GrainContext;
            TransactionalState<TState> transactionalState = ActivatorUtilities.CreateInstance<TransactionalState<TState>>(currentContext.ActivationServices, config, this.contextAccessor);
            transactionalState.Participate(currentContext.ObservableLifecycle);
            return transactionalState;
        }

        public static JsonSerializerSettings GetJsonSerializerSettings(IServiceProvider serviceProvider)
        {
            var serializerSettings = OrleansJsonSerializerSettings.GetDefaultSerializerSettings(serviceProvider);
            serializerSettings.PreserveReferencesHandling = PreserveReferencesHandling.None;
            return serializerSettings;
        }
    }
}
