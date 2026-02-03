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

using Bravellian.Platform.Modularity;
using Bravellian.Platform.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Xunit;

#pragma warning disable CA1861
namespace Bravellian.Platform.Tests;

[Collection("ModuleRegistryTests")]
public sealed class EngineRefactoringTests
{
    public EngineRefactoringTests()
    {
        ModuleEngineRegistry.Reset();
        ModuleRegistry.Reset();
        ModuleRegistry.RegisterModule<FakeEngineModule>();
    }

    /// <summary>
    /// When the UI engine executes a valid login command, then it returns a view model and navigation tokens.
    /// </summary>
    /// <intent>
    /// Verify UI engine execution returns expected view model data and navigation metadata.
    /// </intent>
    /// <scenario>
    /// Given a UiEngineAdapter wired with FakeEngineModule and a LoginCommand with credentials.
    /// </scenario>
    /// <behavior>
    /// Then the response contains the username, a dashboard navigation token, and a login event.
    /// </behavior>
    [Fact]
    public async Task Ui_engine_invocation_returns_view_model_and_navigation_tokens()
    {
        var provider = BuildServiceProvider();
        var adapter = new UiEngineAdapter(provider.GetRequiredService<ModuleEngineDiscoveryService>(), provider);

        var response = await adapter.ExecuteAsync<LoginCommand, LoginViewModel>("fake-module", "ui.login", new LoginCommand("admin", "pass"), CancellationToken.None);

        Assert.Equal("admin", response.ViewModel.Username);
        var navigationTargets = response.NavigationTargets;
        Assert.NotNull(navigationTargets);
        Assert.Contains(navigationTargets, token => string.Equals(token.Token, "dashboard", StringComparison.Ordinal)
            && token.TargetKind == NavigationTargetKind.Route);
        var events = response.Events;
        Assert.NotNull(events);
        Assert.Contains("event:login", events, StringComparer.Ordinal);
    }

    /// <summary>
    /// When a webhook request has a valid event type, then the handler is invoked.
    /// </summary>
    /// <intent>
    /// Verify webhook classification and handler dispatch for accepted requests.
    /// </intent>
    /// <scenario>
    /// Given a Postmark webhook envelope with the expected event type header.
    /// </scenario>
    /// <behavior>
    /// Then classification accepts the request and the handler processes the webhook.
    /// </behavior>
    [Fact]
    public async Task Webhook_pipeline_authenticates_and_dispatches()
    {
        var provider = BuildServiceProvider();
        var registry = provider.GetRequiredService<IWebhookProviderRegistry>();
        var webhookProvider = registry.Get("postmark");

        Assert.NotNull(webhookProvider);

        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ModuleWebhookOptions.DefaultEventTypeHeaderName] = "bounce",
        };

        var envelope = new WebhookEnvelope(
            "postmark",
            DateTimeOffset.UtcNow,
            "POST",
            "/webhooks/postmark/bounce",
            string.Empty,
            headers,
            "application/json",
            Encoding.UTF8.GetBytes("{\"type\":\"HardBounce\",\"description\":\"Mail rejected\"}"),
            null);

        var classify = await webhookProvider.Classifier.ClassifyAsync(envelope, CancellationToken.None);
        Assert.Equal(WebhookIngestDecision.Accepted, classify.Decision);
        Assert.Equal("bounce", classify.EventType);

        var context = new WebhookEventContext(
            "postmark",
            "dedupe-1",
            classify.ProviderEventId,
            classify.EventType,
            null,
            DateTimeOffset.UtcNow,
            headers,
            envelope.BodyBytes,
            envelope.ContentType);

        await webhookProvider.Handlers.Single().HandleAsync(context, CancellationToken.None);

        var engine = provider.GetRequiredService<PostmarkWebhookEngine>();
        Assert.True(engine.Handled);
    }

    /// <summary>
    /// When the event type header is missing, then classification rejects the request.
    /// </summary>
    /// <intent>
    /// Ensure webhook classification requires an explicit event type header.
    /// </intent>
    /// <scenario>
    /// Given a webhook envelope without the event type header.
    /// </scenario>
    /// <behavior>
    /// Then classification returns a rejected decision with a failure reason.
    /// </behavior>
    [Fact]
    public async Task Webhook_pipeline_requires_event_type_header()
    {
        var provider = BuildServiceProvider();
        var registry = provider.GetRequiredService<IWebhookProviderRegistry>();
        var webhookProvider = registry.Get("postmark");

        Assert.NotNull(webhookProvider);

        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        var envelope = new WebhookEnvelope(
            "postmark",
            DateTimeOffset.UtcNow,
            "POST",
            "/webhooks/postmark/bounce",
            string.Empty,
            headers,
            "application/json",
            Encoding.UTF8.GetBytes("{}"),
            null);

        var classify = await webhookProvider!.Classifier.ClassifyAsync(envelope, CancellationToken.None);
        Assert.Equal(WebhookIngestDecision.Rejected, classify.Decision);
        Assert.Contains("event type header", classify.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// When the UI engine throws during execution, then the adapter propagates the exception.
    /// </summary>
    /// <intent>
    /// Ensure engine exceptions are not swallowed by the adapter.
    /// </intent>
    /// <scenario>
    /// Given a LoginCommand with a missing username that triggers an ArgumentException in the engine.
    /// </scenario>
    /// <behavior>
    /// Then ExecuteAsync throws ArgumentException.
    /// </behavior>
    [Fact]
    public async Task Ui_engine_exception_propagates_to_adapter()
    {
        var provider = BuildServiceProvider();
        var adapter = new UiEngineAdapter(provider.GetRequiredService<ModuleEngineDiscoveryService>(), provider);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await adapter.ExecuteAsync<LoginCommand, LoginViewModel>("fake-module", "ui.login", new LoginCommand(string.Empty, "pass"), CancellationToken.None));
    }

    /// <summary>
    /// When a UI engine is not registered for the requested engine ID, then ExecuteAsync throws.
    /// </summary>
    /// <intent>
    /// Ensure missing UI engines surface a clear error.
    /// </intent>
    /// <scenario>
    /// Given a UiEngineAdapter and a request targeting an unknown UI engine ID.
    /// </scenario>
    /// <behavior>
    /// Then ExecuteAsync throws InvalidOperationException with a "No UI engine registered" message.
    /// </behavior>
    [Fact]
    public async Task Ui_adapter_throws_when_engine_is_not_registered()
    {
        var provider = BuildServiceProvider();
        var adapter = new UiEngineAdapter(provider.GetRequiredService<ModuleEngineDiscoveryService>(), provider);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await adapter.ExecuteAsync<LoginCommand, LoginViewModel>("fake-module", "ui.missing", new LoginCommand("admin", "pass"), CancellationToken.None));

        Assert.Contains("No UI engine registered", ex.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// When a UI adapter targets an engine with a non-UI contract, then ExecuteAsync throws.
    /// </summary>
    /// <intent>
    /// Prevent UI adapters from invoking engines with mismatched contracts.
    /// </intent>
    /// <scenario>
    /// Given a UiEngineAdapter and a request for a webhook engine ID.
    /// </scenario>
    /// <behavior>
    /// Then ExecuteAsync throws InvalidOperationException indicating the UI contract mismatch.
    /// </behavior>
    [Fact]
    public async Task Ui_adapter_throws_when_engine_contract_is_mismatched()
    {
        var provider = BuildServiceProvider();
        var adapter = new UiEngineAdapter(provider.GetRequiredService<ModuleEngineDiscoveryService>(), provider);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await adapter.ExecuteAsync<LoginCommand, LoginViewModel>("fake-module", "webhook.postmark", new LoginCommand("admin", "pass"), CancellationToken.None));

        Assert.Contains("does not implement the expected UI contract", ex.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// When engines are listed and resolved with filters, then discovery returns the expected UI and webhook descriptors.
    /// </summary>
    /// <intent>
    /// Validate engine discovery filtering and webhook resolution by provider and event.
    /// </intent>
    /// <scenario>
    /// Given a ModuleEngineDiscoveryService built from FakeEngineModule descriptors.
    /// </scenario>
    /// <behavior>
    /// Then UI and webhook engines are found via filters and ResolveWebhookEngine returns the expected module key.
    /// </behavior>
    [Fact]
    public void Discovery_service_filters_engines()
    {
        var provider = BuildServiceProvider();
        var discovery = provider.GetRequiredService<ModuleEngineDiscoveryService>();

        var allEngines = discovery.List();
        Assert.Contains(allEngines, e => e.Manifest.Kind == EngineKind.Ui
            && string.Equals(e.ModuleKey, "fake-module", StringComparison.Ordinal));

        var webhookEngines = discovery.List(EngineKind.Webhook, featureArea: "Notifications");
        Assert.Contains(webhookEngines, e => string.Equals(e.Manifest.Id, "webhook.postmark", StringComparison.Ordinal)
            && string.Equals(e.ModuleKey, "fake-module", StringComparison.Ordinal));

        var resolved = discovery.List(EngineKind.Webhook)
            .FirstOrDefault(engine => engine.Manifest.WebhookMetadata?.Any(metadata =>
                string.Equals(metadata.Provider, "postmark", StringComparison.OrdinalIgnoreCase)
                && string.Equals(metadata.EventType, "bounce", StringComparison.OrdinalIgnoreCase)) == true);
        Assert.NotNull(resolved);
        Assert.Equal("fake-module", resolved!.ModuleKey);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequiredServiceValidator, TestRequiredServiceValidator>();
        services.AddModuleServices(new ConfigurationBuilder().Build());
        services.AddModuleWebhookProviders();
        services.AddSingleton<UiEngineAdapter>();
        return services.BuildServiceProvider();
    }

    private sealed class FakeEngineModule : IModuleDefinition
    {
        public string Key => "fake-module";

        public string DisplayName => "Fake Engines";

        public IEnumerable<string> GetOptionalConfigurationKeys() => Array.Empty<string>();

        public IEnumerable<string> GetRequiredConfigurationKeys() => Array.Empty<string>();

        public void LoadConfiguration(IReadOnlyDictionary<string, string> required, IReadOnlyDictionary<string, string> optionalConfiguration)
        {
        }

        public void AddModuleServices(IServiceCollection services)
        {
            services.AddSingleton<LoginUiEngine>();
            services.AddSingleton<PostmarkWebhookEngine>();
        }

        public void RegisterHealthChecks(ModuleHealthCheckBuilder builder)
        {
        }

        public IEnumerable<IModuleEngineDescriptor> DescribeEngines()
        {
            yield return new ModuleEngineDescriptor<IUiEngine<LoginCommand, LoginViewModel>>(
                Key,
                new ModuleEngineManifest(
                    "ui.login",
                    "1.0",
                    "Login page engine",
                    EngineKind.Ui,
                    "Auth",
                    new ModuleEngineCapabilities(new[] { "login" }, new[] { "login.loggedIn" }, SupportsStreaming: false),
                    new[] { new ModuleEngineSchema("command", typeof(LoginCommand)) },
                    new[] { new ModuleEngineSchema("viewModel", typeof(LoginViewModel)) },
                    new ModuleEngineNavigationHints(new[] { new ModuleNavigationToken("dashboard", NavigationTargetKind.Route) }),
                    new[] { nameof(LoginUiEngine) },
                    new ModuleEngineAdapterHints(false, false, false, false, true),
                    null,
                    new ModuleEngineCompatibility("1.0", null)),
                sp => sp.GetRequiredService<LoginUiEngine>());

            yield return new ModuleEngineDescriptor<IModuleWebhookEngine<PostmarkBouncePayload>>(
                Key,
                new ModuleEngineManifest(
                    "webhook.postmark",
                    "1.0",
                    "Postmark bounce webhook handler",
                    EngineKind.Webhook,
                    "Notifications",
                    new ModuleEngineCapabilities(new[] { "handle" }, new[] { "bounce.received" }, SupportsStreaming: false),
                    new[] { new ModuleEngineSchema("payload", typeof(PostmarkBouncePayload)) },
                    Array.Empty<ModuleEngineSchema>(),
                    null,
                    new[] { nameof(PostmarkWebhookEngine) },
                    new ModuleEngineAdapterHints(true, true, true, false, true),
                    new ModuleEngineSecurity(ModuleSignatureAlgorithm.HmacSha256, "postmark", TimeSpan.FromMinutes(10)),
                    new ModuleEngineCompatibility("1.0", "Initial"),
                    new[]
                    {
                        new ModuleEngineWebhookMetadata("postmark", "bounce", new ModuleEngineSchema("payload", typeof(PostmarkBouncePayload)), new[] { "logging" }, Retries: 3),
                    }),
                sp => sp.GetRequiredService<PostmarkWebhookEngine>());
        }
    }

    private sealed record LoginCommand(string Username, string Password);

    private sealed record LoginViewModel(string Username, bool Success);

    private sealed class LoginUiEngine : IUiEngine<LoginCommand, LoginViewModel>
    {
        public Task<UiEngineResult<LoginViewModel>> ExecuteAsync(LoginCommand command, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(command.Username))
            {
                throw new ArgumentException("Username is required", nameof(command));
            }

            var viewModel = new LoginViewModel(command.Username, true);
            return Task.FromResult(new UiEngineResult<LoginViewModel>(
                viewModel,
                new[] { new ModuleNavigationToken("dashboard", NavigationTargetKind.Route) },
                new[] { "event:login" }));
        }
    }

    private sealed record PostmarkBouncePayload(string Type, string Description);

    private sealed class PostmarkWebhookEngine : IModuleWebhookEngine<PostmarkBouncePayload>
    {
        public bool Handled { get; private set; }

        public Task HandleAsync(ModuleWebhookRequest<PostmarkBouncePayload> request, CancellationToken cancellationToken)
        {
            Handled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class TestRequiredServiceValidator : IRequiredServiceValidator
    {
        public IReadOnlyCollection<string> GetMissingServices(IReadOnlyCollection<string> requiredServices)
        {
            return Array.Empty<string>();
        }
    }
}
#pragma warning restore CA1861
