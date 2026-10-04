using System.Reflection;

using Serilog.Core;
using Serilog.Events;

namespace RegReturns.ServiceDefaults.Logging;

/// <summary>
/// When an object is destructured into a log event (<c>{@Value}</c>), masks properties that have sensitive
/// names or carry a PersonalData/SensitiveData/Secret attribute.
/// </summary>
internal sealed class SensitiveDataDestructuringPolicy : IDestructuringPolicy
{
    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, out LogEventPropertyValue result)
    {
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is string or decimal or DateTime or DateTimeOffset or Guid
            || value is System.Collections.IEnumerable)
        {
            result = null!;
            return false;
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToList();
        if (!properties.Exists(p => SensitiveData.IsSensitiveName(p.Name) || SensitiveData.IsMarkedSensitive(p)))
        {
            result = null!;
            return false;
        }

        var logProperties = properties.Select(p => new LogEventProperty(
            p.Name,
            SensitiveData.IsSensitiveName(p.Name) || SensitiveData.IsMarkedSensitive(p)
                ? new ScalarValue(SensitiveData.Mask)
                : propertyValueFactory.CreatePropertyValue(p.GetValue(value), destructureObjects: true)));
        result = new StructureValue(logProperties, type.Name);
        return true;
    }
}
