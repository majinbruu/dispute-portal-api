using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using DisputePortal.SharedKernel;

namespace DisputePortal.IntegrationTests.Infrastructure;

/// <summary>Counts what the application records on its business counters while a test runs.</summary>
internal sealed class MetricProbe : IDisposable
{
    private readonly ConcurrentDictionary<string, long> _totals = new();
    private readonly MeterListener _listener = new();

    public MetricProbe()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == PortalMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
            _totals.AddOrUpdate(instrument.Name, measurement, (_, total) => total + measurement));
        _listener.Start();
    }

    /// <summary>How much the counter rose since the test started.</summary>
    public long Count(Counter<long> counter) => _totals.GetValueOrDefault(counter.Name);

    public void Dispose() => _listener.Dispose();
}
