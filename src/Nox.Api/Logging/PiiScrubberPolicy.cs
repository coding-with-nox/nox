using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace Nox.Api.Logging;

/// <summary>
/// Serilog destructuring policy that masks email addresses in log messages.
/// Applied globally via LoggerConfiguration.Destructure.With().
/// </summary>
public sealed partial class PiiScrubberPolicy : IDestructuringPolicy
{
    [GeneratedRegex(@"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    public bool TryDestructure(object value, ILogEventPropertyValueFactory factory, out LogEventPropertyValue result)
    {
        if (value is string str)
        {
            var masked = EmailRegex().Replace(str, "[email-redacted]");
            if (masked != str)
            {
                result = new ScalarValue(masked);
                return true;
            }
        }
        result = null!;
        return false;
    }
}
