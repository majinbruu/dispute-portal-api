using DisputePortal.SharedKernel;
using Prometheus;

namespace DisputePortal.IntegrationTests.Infrastructure;

/// <summary>
/// Counts what the application records on its business counters while a test runs. The counters live as long
/// as the process, so the probe remembers where each one stood when the test started.
/// </summary>
internal sealed class MetricProbe
{
    private readonly Dictionary<Counter, double> _atStart = typeof(PortalMetrics).GetFields()
        .Select(field => field.GetValue(null))
        .OfType<Counter>()
        .ToDictionary(counter => counter, Total);

    /// <summary>How much the counter rose since the test started, over all its label values.</summary>
    public long Count(Counter counter) => (long)(Total(counter) - _atStart[counter]);

    private static double Total(Counter counter) => counter.LabelNames.Length == 0
        ? counter.Value
        : counter.GetAllLabelValues().Sum(labels => counter.WithLabels(labels).Value);
}
