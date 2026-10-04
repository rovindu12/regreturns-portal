using Serilog.Core;
using Serilog.Events;

namespace RegReturns.ServiceDefaults.Logging;

/// <summary>Replaces top-level log properties with sensitive names (password, token, email...) by a mask.</summary>
internal sealed class SensitiveDataEnricher : ILogEventEnricher
{
    private static readonly ScalarValue Masked = new(SensitiveData.Mask);

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var name in logEvent.Properties.Keys.Where(SensitiveData.IsSensitiveName).ToList())
        {
            logEvent.AddOrUpdateProperty(new LogEventProperty(name, Masked));
        }
    }
}
