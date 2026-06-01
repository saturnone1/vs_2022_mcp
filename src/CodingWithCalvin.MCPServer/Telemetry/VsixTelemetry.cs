using System;
using System.Diagnostics;

namespace CodingWithCalvin.Otel4Vsix;

internal static class VsixTelemetry
{
    public static NoOpTelemetryBuilder Configure() => new();
    public static NoOpTracer Tracer { get; } = new();
    public static void TrackException(Exception exception) { }
    public static void Shutdown() { }
}

internal sealed class NoOpTelemetryBuilder
{
    public NoOpTelemetryBuilder WithServiceName(string serviceName) => this;
    public NoOpTelemetryBuilder WithServiceVersion(string serviceVersion) => this;
    public NoOpTelemetryBuilder WithVisualStudioAttributes(object package) => this;
    public NoOpTelemetryBuilder WithEnvironmentAttributes() => this;
    public NoOpTelemetryBuilder WithOtlpHttp(string endpoint) => this;
    public NoOpTelemetryBuilder WithHeader(string name, string value) => this;
    public void Initialize() { }
}

internal sealed class NoOpTracer
{
    public NoOpActivity StartActivity(string name) => new();
}

internal sealed class NoOpActivity : IDisposable
{
    public void SetStatus(ActivityStatusCode statusCode, string description = "") { }
    public void RecordException(Exception exception) { }
    public void Dispose() { }
}
