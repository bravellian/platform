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
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Bravellian.Platform.Tests.Modularity;

[Collection("ModuleRegistryTests")]
public sealed class ModuleSystemTests
{
    [Fact]
    public void Modules_register_services_and_health()
    {
        ModuleRegistry.Reset();
        ModuleRegistry.RegisterModule<SampleModule>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [SampleModule.RequiredKey] = "value",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddModuleServices(configuration, NullLoggerFactory.Instance);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<MarkerService>().Value.ShouldBe("value");

        var healthOptions = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        healthOptions.Registrations.ShouldContain(r => r.Name == "sample_module");
    }

    [Fact]
    public void Modules_are_registered_in_di()
    {
        ModuleRegistry.Reset();
        ModuleRegistry.RegisterModule<SampleModule>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [SampleModule.RequiredKey] = "value",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddModuleServices(configuration, NullLoggerFactory.Instance);

        using var provider = services.BuildServiceProvider();
        var module = provider.GetRequiredService<IModuleDefinition>();
        module.Key.ShouldBe("sample-module");
    }

    [Fact]
    public void Module_keys_must_be_unique()
    {
        ModuleRegistry.Reset();
        ModuleRegistry.RegisterModule<SampleModule>();
        ModuleRegistry.RegisterModule<ConflictingModule>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [SampleModule.RequiredKey] = "value",
                [ConflictingModule.RequiredKey] = "other",
            })
            .Build();

        var services = new ServiceCollection();
        Should.Throw<InvalidOperationException>(() =>
            services.AddModuleServices(configuration, NullLoggerFactory.Instance));
    }

    [Fact]
    public void Module_keys_must_be_url_safe()
    {
        ModuleRegistry.Reset();
        ModuleRegistry.RegisterModule<ModuleWithInvalidKey>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [ModuleWithInvalidKey.RequiredKey] = "value",
            })
            .Build();

        var services = new ServiceCollection();
        var ex = Should.Throw<InvalidOperationException>(() =>
            services.AddModuleServices(configuration, NullLoggerFactory.Instance));

        ex.Message.ShouldContain("cannot contain slashes");
    }

    [Fact]
    public void Engine_descriptors_must_use_module_key()
    {
        ModuleRegistry.Reset();
        ModuleRegistry.RegisterModule<ModuleWithMismatchedEngineDescriptor>();

        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        var ex = Should.Throw<InvalidOperationException>(() =>
            services.AddModuleServices(configuration, NullLoggerFactory.Instance));

        ex.Message.ShouldContain("Engine descriptor module key");
    }

    [Fact]
    public void Webhook_metadata_must_be_unique()
    {
        ModuleRegistry.Reset();
        ModuleRegistry.RegisterModule<WebhookModuleOne>();
        ModuleRegistry.RegisterModule<WebhookModuleTwo>();

        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        var ex = Should.Throw<InvalidOperationException>(() =>
            services.AddModuleServices(configuration, NullLoggerFactory.Instance));

        ex.Message.ShouldContain("already handled");
    }

    [Fact]
    public void Registering_module_type_is_idempotent()
    {
        ModuleRegistry.Reset();
        ModuleRegistry.RegisterModule<SampleModule>();
        Should.NotThrow(() => ModuleRegistry.RegisterModule<SampleModule>());
    }

    private sealed class SampleModule : IModuleDefinition
    {
        internal const string RequiredKey = "sample:required";

        public string Key => "sample-module";

        public string DisplayName => "Sample Module";

        public IEnumerable<string> GetRequiredConfigurationKeys()
        {
            yield return RequiredKey;
        }

        public IEnumerable<string> GetOptionalConfigurationKeys() => Array.Empty<string>();

        public void LoadConfiguration(IReadOnlyDictionary<string, string> required, IReadOnlyDictionary<string, string> optional)
        {
        }

        public void AddModuleServices(IServiceCollection services)
        {
            services.AddSingleton(new MarkerService("value"));
        }

        public void RegisterHealthChecks(ModuleHealthCheckBuilder builder)
        {
            builder.AddCheck("sample_module", () => HealthCheckResult.Healthy());
        }

        public IEnumerable<IModuleEngineDescriptor> DescribeEngines() => Array.Empty<IModuleEngineDescriptor>();
    }

    private sealed class ConflictingModule : IModuleDefinition
    {
        internal const string RequiredKey = "conflict:required";

        public string Key => "sample-module";

        public string DisplayName => "Conflict";

        public IEnumerable<string> GetRequiredConfigurationKeys()
        {
            yield return RequiredKey;
        }

        public IEnumerable<string> GetOptionalConfigurationKeys() => Array.Empty<string>();

        public void LoadConfiguration(IReadOnlyDictionary<string, string> required, IReadOnlyDictionary<string, string> optional)
        {
        }

        public void AddModuleServices(IServiceCollection services)
        {
        }

        public void RegisterHealthChecks(ModuleHealthCheckBuilder builder)
        {
        }

        public IEnumerable<IModuleEngineDescriptor> DescribeEngines() => Array.Empty<IModuleEngineDescriptor>();
    }

    private sealed class ModuleWithInvalidKey : IModuleDefinition
    {
        internal const string RequiredKey = "invalid:required";

        public string Key => "invalid/key";

        public string DisplayName => "Invalid Key Module";

        public IEnumerable<string> GetRequiredConfigurationKeys()
        {
            yield return RequiredKey;
        }

        public IEnumerable<string> GetOptionalConfigurationKeys() => Array.Empty<string>();

        public void LoadConfiguration(IReadOnlyDictionary<string, string> required, IReadOnlyDictionary<string, string> optional)
        {
        }

        public void AddModuleServices(IServiceCollection services)
        {
        }

        public void RegisterHealthChecks(ModuleHealthCheckBuilder builder)
        {
        }

        public IEnumerable<IModuleEngineDescriptor> DescribeEngines() => Array.Empty<IModuleEngineDescriptor>();
    }

    private sealed class ModuleWithMismatchedEngineDescriptor : IModuleDefinition
    {
        public string Key => "mismatch";

        public string DisplayName => "Module With Mismatched Descriptor";

        public IEnumerable<string> GetRequiredConfigurationKeys() => Array.Empty<string>();

        public IEnumerable<string> GetOptionalConfigurationKeys() => Array.Empty<string>();

        public void LoadConfiguration(IReadOnlyDictionary<string, string> required, IReadOnlyDictionary<string, string> optional)
        {
        }

        public void AddModuleServices(IServiceCollection services)
        {
            services.AddSingleton<MismatchedUiEngine>();
        }

        public void RegisterHealthChecks(ModuleHealthCheckBuilder builder)
        {
        }

        public IEnumerable<IModuleEngineDescriptor> DescribeEngines()
        {
            yield return new ModuleEngineDescriptor<IUiEngine<MismatchedCommand, MismatchedViewModel>>(
                "other-module",
                new ModuleEngineManifest("ui.mismatch", "1.0", "Mismatched", EngineKind.Ui),
                sp => sp.GetRequiredService<MismatchedUiEngine>());
        }
    }

    private sealed class WebhookModuleOne : IModuleDefinition
    {
        public string Key => "webhook-one";

        public string DisplayName => "Webhook Module One";

        public IEnumerable<string> GetRequiredConfigurationKeys() => Array.Empty<string>();

        public IEnumerable<string> GetOptionalConfigurationKeys() => Array.Empty<string>();

        public void LoadConfiguration(IReadOnlyDictionary<string, string> required, IReadOnlyDictionary<string, string> optional)
        {
        }

        public void AddModuleServices(IServiceCollection services)
        {
            services.AddSingleton<WebhookEngineOne>();
        }

        public void RegisterHealthChecks(ModuleHealthCheckBuilder builder)
        {
        }

        public IEnumerable<IModuleEngineDescriptor> DescribeEngines()
        {
            yield return new ModuleEngineDescriptor<IWebhookEngine<WebhookPayload>>(
                Key,
                new ModuleEngineManifest(
                    "webhook.one",
                    "1.0",
                    "Webhook One",
                    EngineKind.Webhook,
                    WebhookMetadata: new[]
                    {
                        new ModuleEngineWebhookMetadata("postmark", "bounce", new ModuleEngineSchema("payload", typeof(WebhookPayload))),
                    }),
                sp => sp.GetRequiredService<WebhookEngineOne>());
        }
    }

    private sealed class WebhookModuleTwo : IModuleDefinition
    {
        public string Key => "webhook-two";

        public string DisplayName => "Webhook Module Two";

        public IEnumerable<string> GetRequiredConfigurationKeys() => Array.Empty<string>();

        public IEnumerable<string> GetOptionalConfigurationKeys() => Array.Empty<string>();

        public void LoadConfiguration(IReadOnlyDictionary<string, string> required, IReadOnlyDictionary<string, string> optional)
        {
        }

        public void AddModuleServices(IServiceCollection services)
        {
            services.AddSingleton<WebhookEngineTwo>();
        }

        public void RegisterHealthChecks(ModuleHealthCheckBuilder builder)
        {
        }

        public IEnumerable<IModuleEngineDescriptor> DescribeEngines()
        {
            yield return new ModuleEngineDescriptor<IWebhookEngine<WebhookPayload>>(
                Key,
                new ModuleEngineManifest(
                    "webhook.two",
                    "1.0",
                    "Webhook Two",
                    EngineKind.Webhook,
                    WebhookMetadata: new[]
                    {
                        new ModuleEngineWebhookMetadata("postmark", "bounce", new ModuleEngineSchema("payload", typeof(WebhookPayload))),
                    }),
                sp => sp.GetRequiredService<WebhookEngineTwo>());
        }
    }

    private sealed record MismatchedCommand;

    private sealed record MismatchedViewModel(string Value);

    private sealed class MismatchedUiEngine : IUiEngine<MismatchedCommand, MismatchedViewModel>
    {
        public Task<UiEngineResult<MismatchedViewModel>> ExecuteAsync(MismatchedCommand command, CancellationToken cancellationToken)
        {
            return Task.FromResult(new UiEngineResult<MismatchedViewModel>(new MismatchedViewModel("ok")));
        }
    }

    private sealed record WebhookPayload(string Value);

    private sealed class WebhookEngineOne : IWebhookEngine<WebhookPayload>
    {
        public Task<WebhookOutcome> HandleAsync(WebhookRequest<WebhookPayload> request, CancellationToken cancellationToken)
        {
            return Task.FromResult(WebhookOutcome.Acknowledge());
        }
    }

    private sealed class WebhookEngineTwo : IWebhookEngine<WebhookPayload>
    {
        public Task<WebhookOutcome> HandleAsync(WebhookRequest<WebhookPayload> request, CancellationToken cancellationToken)
        {
            return Task.FromResult(WebhookOutcome.Acknowledge());
        }
    }

    private sealed record MarkerService(string Value);
}
