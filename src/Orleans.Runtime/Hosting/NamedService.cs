using System;

namespace Forkleans.Runtime.Hosting
{
    internal class NamedService<TService>(string name)
    {
        public string Name { get; } = name;
    }
}
