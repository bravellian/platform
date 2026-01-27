// Copyright (c) Bravellian
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform.Webhooks.AspNetCore.Tests;

public sealed class WebhookEndpointTests
{
    [Fact]
    public async Task AcceptedReturns202AndCallsIngestorAsync()
    {
        var payload = "{\"id\":\"evt_123\"}";
        var fake = new RecordingIngestor(WebhookIngestDecision.Accepted, HttpStatusCode.Accepted);

        await using var app = await BuildAppAsync(fake);
        var client = app.GetTestClient();

        var response = await client.PostAsync("/webhooks/stripe?source=test", new StringContent(payload, Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        fake.Calls.Count.ShouldBe(1);
        fake.Calls[0].Provider.ShouldBe("stripe");
        fake.Calls[0].Envelope.Method.ShouldBe("POST");
        fake.Calls[0].Envelope.Path.ShouldBe("/webhooks/stripe");
        fake.Calls[0].Envelope.QueryString.ShouldBe("?source=test");
        Encoding.UTF8.GetString(fake.Calls[0].Envelope.BodyBytes).ShouldBe(payload);
    }

    [Fact]
    public async Task RejectedReturns401Async()
    {
        var fake = new RecordingIngestor(WebhookIngestDecision.Rejected, HttpStatusCode.Unauthorized);

        await using var app = await BuildAppAsync(fake);
        var client = app.GetTestClient();

        var response = await client.PostAsync("/webhooks/stripe", new StringContent("{}", Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        fake.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public void LoggingCallbacksEmitEvents()
    {
        var loggerFactory = new CapturingLoggerFactory();
        var setup = new WebhookLoggingOptionsSetup(loggerFactory);
        var options = new WebhookOptions();

        var now = new DateTimeOffset(2024, 01, 01, 0, 0, 0, TimeSpan.Zero);

        setup.PostConfigure(null, options);

        var envelope = new WebhookEnvelope(
            "fake",
            now,
            "POST",
            "/webhooks/fake",
            string.Empty,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            "application/json",
            "{}"u8.ToArray(),
            "127.0.0.1");

        options.OnIngested?.Invoke(new WebhookIngestResult(
            WebhookIngestDecision.Accepted,
            HttpStatusCode.Accepted,
            null,
            "evt_1",
            "envelope.completed",
            "dedupe-1",
            null,
            null,
            false), envelope);
        options.OnRejected?.Invoke("bad", envelope, null);
        options.OnProcessed?.Invoke(new ProcessingResult(WebhookEventStatus.Completed, 1, null), new WebhookEventContext(
            "fake",
            "dedupe-1",
            "evt_1",
            "envelope.completed",
            null,
            now,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            "{}"u8.ToArray(),
            "application/json"));

        loggerFactory.LogEntries.Count.ShouldBeGreaterThan(0);
        loggerFactory.LogEntries.Any(entry => entry.Message.Contains(WebhookTelemetryEvents.IngestAccepted, StringComparison.Ordinal)).ShouldBeTrue();
        loggerFactory.LogEntries.Any(entry => entry.Message.Contains(WebhookTelemetryEvents.IngestRejected, StringComparison.Ordinal)).ShouldBeTrue();
        loggerFactory.LogEntries.Any(entry => entry.Message.Contains(WebhookTelemetryEvents.ProcessCompleted, StringComparison.Ordinal)).ShouldBeTrue();
    }

    private static async Task<WebApplication> BuildAppAsync(RecordingIngestor ingestor)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IWebhookIngestor>(ingestor);

        var app = builder.Build();
        app.MapPost("/webhooks/{provider}", async (HttpContext context, string provider, IWebhookIngestor ingest, CancellationToken ct) =>
        {
            return await WebhookEndpoint.HandleAsync(context, provider, ingest, ct);
        });

        await app.StartAsync();
        return app;
    }

    private sealed class RecordingIngestor : IWebhookIngestor
    {
        private readonly WebhookIngestDecision decision;
        private readonly HttpStatusCode statusCode;

        public RecordingIngestor(WebhookIngestDecision decision, HttpStatusCode statusCode)
        {
            this.decision = decision;
            this.statusCode = statusCode;
        }

        public List<Call> Calls { get; } = new();

        public Task<WebhookIngestResult> IngestAsync(string providerName, WebhookEnvelope envelope, CancellationToken cancellationToken)
        {
            Calls.Add(new Call(providerName, envelope));
            return Task.FromResult(new WebhookIngestResult(decision, statusCode, null, null, null, null, null, null, false));
        }

        public sealed record Call(string Provider, WebhookEnvelope Envelope);
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        public List<LogEntry> LogEntries { get; } = new();

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new CapturingLogger(LogEntries, categoryName);
        }

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly List<LogEntry> entries;
        private readonly string category;

        public CapturingLogger(List<LogEntry> entries, string category)
        {
            this.entries = entries;
            this.category = category;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Add(new LogEntry(logLevel, category, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed record LogEntry(LogLevel Level, string Category, string Message);
}

