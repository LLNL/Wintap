using gov.llnl.wintap.collect.models;

namespace gov.llnl.wintap.core.infrastructure.health
{
    internal interface IWintapHealthCheck
    {
        string Name { get; }

        bool Passes(WintapMessage msg);

        string Describe(WintapMessage msg);
    }
}
