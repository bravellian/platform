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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

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

    [Fact]
    public async Task Ui_engine_invocation_returns_view_model_and_navigation_tokens()
    {
        var provider = BuildServiceProvider();
        var adapter = new UiEngineAdapter(provider.GetRequiredService<ModuleEngineDiscoveryService>(), provider);

        var response = await adapter.ExecuteAsync<LoginCommand, LoginViewModel>("fake-module", "ui.login", new LoginCommand("admin", "pass"), CancellationToken.None);

        Assert.Equal("admin", response.ViewModel.Username);
        Assert.Contains(response.NavigationTargets!, token => token.Token == "dashboard" && token.TargetKind == NavigationTargetKind.Route);
        Assert.Contains("event:login", response.Events);
    }

    [Fact]
    public async Task Webhook_adapter_enforces_signature_and_maps_outcome()
    {
        var provider = BuildServiceProvider();
        var adapter = new WebhookEngineAdapter(provider.GetRequiredService<ModuleEngineDiscoveryService>(), provider, provider.GetRequiredService<IWebhookSignatureValidator>());

        var request = new WebhookAdapterRequest<PostmarkBouncePayload>(
            "postmark",
            "bounce",
            new Dictionary<string, string> { ["X-Signature"] = "postmark:raw-body" },
            "raw-body",
            "idemp-1",
            1,
            null,
            new PostmarkBouncePayload("HardBounce", "Mail rejected"));

        var response = await adapter.DispatchAsync(request, CancellationToken.None);

        Assert.Equal(WebhookOutcomeType.EnqueueEvent, response.Outcome);
    }

    [Fact]
    public async Task Webhook_adapter_rejects_invalid_signature()
    {
        var provider = BuildServiceProvider();
        var adapter = new WebhookEngineAdapter(provider.GetRequiredService<ModuleEngineDiscoveryService>(), provider, provider.GetRequiredService<IWebhookSignatureValidator>());

        var request = new WebhookAdapterRequest<PostmarkBouncePayload>(
            "postmark",
            "bounce",
            new Dictionary<string, string> { ["X-Signature"] = "invalid-signature" },
            "raw-body",
            "idemp-1",
            1,
            null,
            new PostmarkBouncePayload("HardBounce", "Mail rejected"));

        var response = await adapter.DispatchAsync(request, CancellationToken.None);

        Assert.Equal(WebhookOutcomeType.Acknowledge, response.Outcome);
        Assert.Contains("Signature validation failed", response.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Webhook_adapter_requires_idempotency_key_when_configured(string idempotencyKey)
    {
        var provider = BuildServiceProvider();
        var adapter = new WebhookEngineAdapter(provider.GetRequiredService<ModuleEngineDiscoveryService>(), provider, provider.GetRequiredService<IWebhookSignatureValidator>());

        var request = new WebhookAdapterRequest<PostmarkBouncePayload>(
            "postmark",
            "bounce",
            new Dictionary<string, string> { ["X-Signature"] = "postmark:raw-body" },
            "raw-body",
            idempotencyKey,
            1,
            null,
            new PostmarkBouncePayload("HardBounce", "Mail rejected"));

        var response = await adapter.DispatchAsync(request, CancellationToken.None);

        Assert.Equal(WebhookOutcomeType.Retry, response.Outcome);
        Assert.Contains("Idempotency key is required", response.Reason);
    }

    [Fact]
    public async Task Webhook_adapter_throws_when_engine_is_not_registered()
    {
        var provider = BuildServiceProvider();
        var adapter = new WebhookEngineAdapter(provider.GetRequiredService<ModuleEngineDiscoveryService>(), provider, provider.GetRequiredService<IWebhookSignatureValidator>());

        var request = new WebhookAdapterRequest<PostmarkBouncePayload>(
            "missing",
            "event",
            new Dictionary<string, string> { ["X-Signature"] = "missing:raw-body" },
            "raw-body",
            "idemp-1",
            1,
            null,
            new PostmarkBouncePayload("HardBounce", "Mail rejected"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.DispatchAsync(request, CancellationToken.None));
        Assert.Contains("No webhook engine registered", ex.Message);
    }

    [Fact]
    public async Task Webhook_adapter_throws_when_engine_contract_is_mismatched()
    {
        var provider = BuildServiceProvider();
        var adapter = new WebhookEngineAdapter(provider.GetRequiredService<ModuleEngineDiscoveryService>(), provider, provider.GetRequiredService<IWebhookSignatureValidator>());

        var request = new WebhookAdapterRequest<OtherWebhookPayload>(
            "postmark",
            "bounce",
            new Dictionary<string, string> { ["X-Signature"] = "postmark:raw-body" },
            "raw-body",
            "idemp-1",
            1,
            null,
            new OtherWebhookPayload("wrong"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.DispatchAsync(request, CancellationToken.None));
        Assert.Contains("does not implement expected webhook contract", ex.Message);
    }

    [Fact]
    public async Task Ui_engine_exception_propagates_to_adapter()
    {
        var provider = BuildServiceProvider();
        var adapter = new UiEngineAdapter(provider.GetRequiredService<ModuleEngineDiscoveryService>(), provider);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await adapter.ExecuteAsync<LoginCommand, LoginViewModel>("fake-module", "ui.login", new LoginCommand(string.Empty, "pass"), CancellationToken.None));
    }

    [Fact]
    public async Task Ui_adapter_throws_when_engine_is_not_registered()
    {
        var provider = BuildServiceProvider();
        var adapter = new UiEngineAdapter(provider.GetRequiredService<ModuleEngineDiscoveryService>(), provider);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await adapter.ExecuteAsync<LoginCommand, LoginViewModel>("fake-module", "ui.missing", new LoginCommand("admin", "pass"), CancellationToken.None));

        Assert.Contains("No UI engine registered", ex.Message);
    }

    [Fact]
    public async Task Ui_adapter_throws_when_engine_contract_is_mismatched()
    {
        var provider = BuildServiceProvider();
        var adapter = new UiEngineAdapter(provider.GetRequiredService<ModuleEngineDiscoveryService>(), provider);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await adapter.ExecuteAsync<LoginCommand, LoginViewModel>("fake-module", "webhook.postmark", new LoginCommand("admin", "pass"), CancellationToken.None));

        Assert.Contains("does not implement the expected UI contract", ex.Message);
    }

    [Fact]
    public void Discovery_service_filters_engines()
    {
        var provider = BuildServiceProvider();
        var discovery = provider.GetRequiredService<ModuleEngineDiscoveryService>();

        var allEngines = discovery.List();
        Assert.Contains(allEngines, e => e.Manifest.Kind == EngineKind.Ui && e.ModuleKey == "fake-module");

        var webhookEngines = discovery.List(EngineKind.Webhook, featureArea: "Notifications");
        Assert.Contains(webhookEngines, e => e.Manifest.Id == "webhook.postmark" && e.ModuleKey == "fake-module");

        var resolved = discovery.ResolveWebhookEngine("postmark", "bounce");
        Assert.NotNull(resolved);
        Assert.Equal("fake-module", resolved!.ModuleKey);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWebhookSignatureValidator, TestSignatureValidator>();
        services.AddSingleton<IRequiredServiceValidator, TestRequiredServiceValidator>();
        services.AddModuleServices(new ConfigurationBuilder().Build());
        services.AddSingleton<UiEngineAdapter>();
        services.AddSingleton<WebhookEngineAdapter>();
        return services.BuildServiceProvider();
    }

    private sealed class FakeEngineModule : IModuleDefinition
    {
        public string Key => "fake-module";

        public string DisplayName => "Fake Engines";

        public IEnumerable<string> GetOptionalConfigurationKeys() => Array.Empty<string>();

        public IEnumerable<string> GetRequiredConfigurationKeys() => Array.Empty<string>();

        public void LoadConfiguration(IReadOnlyDictionary<string, string> required, IReadOnlyDictionary<string, string> optional)
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

            yield return new ModuleEngineDescriptor<IWebhookEngine<PostmarkBouncePayload>>(
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

    private sealed record OtherWebhookPayload(string Value);

    private sealed class PostmarkWebhookEngine : IWebhookEngine<PostmarkBouncePayload>
    {
        public Task<WebhookOutcome> HandleAsync(WebhookRequest<PostmarkBouncePayload> request, CancellationToken cancellationToken)
        {
            if (request.Payload.Type.Equals("HardBounce", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(WebhookOutcome.Enqueue(new { request.Payload.Description, request.EventType }));
            }

            return Task.FromResult(WebhookOutcome.Acknowledge());
        }
    }

    private sealed class TestSignatureValidator : IWebhookSignatureValidator
    {
        public bool Validate(ModuleEngineSecurity security, IReadOnlyDictionary<string, string> headers, string rawBody, string? providedSignature)
        {
            headers.TryGetValue("X-Signature", out var headerSignature);
            var signature = providedSignature ?? headerSignature;
            return security.SignatureAlgorithm == ModuleSignatureAlgorithm.HmacSha256
                && signature == $"{security.SecretScope}:{rawBody}";
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
