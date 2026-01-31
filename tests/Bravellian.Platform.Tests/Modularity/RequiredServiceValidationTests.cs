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
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

#pragma warning disable CA1861
namespace Bravellian.Platform.Tests.Modularity;

[Collection("ModuleRegistryTests")]
public sealed class RequiredServiceValidationTests
{
    /// <summary>
    /// When a UI engine declares required services and no validator is registered, then ExecuteAsync fails and mentions IRequiredServiceValidator.
    /// </summary>
    /// <intent>
    /// Ensure required service validation is enforced when engines declare dependencies.
    /// </intent>
    /// <scenario>
    /// Given RequiredServiceModule is registered and the service collection omits an IRequiredServiceValidator.
    /// </scenario>
    /// <behavior>
    /// Then ExecuteAsync throws an InvalidOperationException whose message references IRequiredServiceValidator.
    /// </behavior>
    [Fact]
    public async Task Ui_adapter_requires_required_service_validator_when_engine_declares_required_services()
    {
        ModuleRegistry.Reset();
        ModuleRegistry.RegisterModule<RequiredServiceModule>();

        var services = new ServiceCollection();
        services.AddModuleServices(new ConfigurationBuilder().Build(), NullLoggerFactory.Instance);

        using var provider = services.BuildServiceProvider();
        var adapter = new UiEngineAdapter(provider.GetRequiredService<ModuleEngineDiscoveryService>(), provider);

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await adapter.ExecuteAsync<DummyCommand, DummyViewModel>(
                "required-service-module",
                "ui.required",
                new DummyCommand(),
                CancellationToken.None));

        ex.Message.ShouldContain(nameof(IRequiredServiceValidator));
    }

    /// <summary>
    /// When required services are missing, then ExecuteAsync throws with missing service details.
    /// </summary>
    /// <intent>
    /// Validate that required service checks surface missing dependencies in the error message.
    /// </intent>
    /// <scenario>
    /// Given RequiredServiceModule is registered and a TestRequiredServiceValidator reports no available services.
    /// </scenario>
    /// <behavior>
    /// Then ExecuteAsync throws and the message contains the missing service name and summary text.
    /// </behavior>
    [Fact]
    public async Task Ui_adapter_throws_when_required_services_are_missing()
    {
        ModuleRegistry.Reset();
        ModuleRegistry.RegisterModule<RequiredServiceModule>();

        var services = new ServiceCollection();
        services.AddSingleton<IRequiredServiceValidator>(new TestRequiredServiceValidator(Array.Empty<string>()));
        services.AddModuleServices(new ConfigurationBuilder().Build(), NullLoggerFactory.Instance);

        using var provider = services.BuildServiceProvider();
        var adapter = new UiEngineAdapter(provider.GetRequiredService<ModuleEngineDiscoveryService>(), provider);

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await adapter.ExecuteAsync<DummyCommand, DummyViewModel>(
                "required-service-module",
                "ui.required",
                new DummyCommand(),
                CancellationToken.None));

        ex.Message.ShouldContain("missing required services");
        ex.Message.ShouldContain("cache");
    }

    /// <summary>
    /// When required services are satisfied, then ExecuteAsync returns the engine result.
    /// </summary>
    /// <intent>
    /// Confirm successful execution when required services are available.
    /// </intent>
    /// <scenario>
    /// Given RequiredServiceModule is registered and a TestRequiredServiceValidator lists cache and telemetry as available.
    /// </scenario>
    /// <behavior>
    /// Then the returned view model contains the expected "ok" value.
    /// </behavior>
    [Fact]
    public async Task Ui_adapter_executes_when_required_services_are_satisfied()
    {
        ModuleRegistry.Reset();
        ModuleRegistry.RegisterModule<RequiredServiceModule>();

        var services = new ServiceCollection();
        services.AddSingleton<IRequiredServiceValidator>(new TestRequiredServiceValidator(new[] { "cache", "telemetry" }));
        services.AddModuleServices(new ConfigurationBuilder().Build(), NullLoggerFactory.Instance);

        using var provider = services.BuildServiceProvider();
        var adapter = new UiEngineAdapter(provider.GetRequiredService<ModuleEngineDiscoveryService>(), provider);

        var response = await adapter.ExecuteAsync<DummyCommand, DummyViewModel>(
            "required-service-module",
            "ui.required",
            new DummyCommand(),
            CancellationToken.None);

        response.ViewModel.Value.ShouldBe("ok");
    }

    /// <summary>
    /// When an engine descriptor factory returns null, then ResolveEngine throws and identifies the module/engine key.
    /// </summary>
    /// <intent>
    /// Guard against null engine factories during discovery resolution.
    /// </intent>
    /// <scenario>
    /// Given a ModuleEngineDescriptor whose factory returns null and an empty service provider.
    /// </scenario>
    /// <behavior>
    /// Then ResolveEngine throws an InvalidOperationException mentioning "returned null" and the descriptor key.
    /// </behavior>
    [Fact]
    public void Discovery_service_resolve_engine_throws_when_factory_returns_null()
    {
        var discovery = new ModuleEngineDiscoveryService();

        var descriptor = new ModuleEngineDescriptor<IUiEngine<DummyCommand, DummyViewModel>>(
            "null-module",
            new ModuleEngineManifest(
                "ui.null",
                "1.0",
                "Null engine factory",
                EngineKind.Ui),
            _ => null!);

        var services = new ServiceCollection().BuildServiceProvider();

        var ex = Should.Throw<InvalidOperationException>(() => discovery.ResolveEngine(descriptor, services));
        ex.Message.ShouldContain("returned null");
        ex.Message.ShouldContain("null-module/ui.null");
    }

    private sealed class RequiredServiceModule : IModuleDefinition
    {
        public string Key => "required-service-module";

        public string DisplayName => "Required Service Module";

        public IEnumerable<string> GetRequiredConfigurationKeys() => Array.Empty<string>();

        public IEnumerable<string> GetOptionalConfigurationKeys() => Array.Empty<string>();

        public void LoadConfiguration(IReadOnlyDictionary<string, string> required, IReadOnlyDictionary<string, string> optionalConfiguration)
        {
        }

        public void AddModuleServices(IServiceCollection services)
        {
            services.AddSingleton<DummyUiEngine>();
        }

        public void RegisterHealthChecks(ModuleHealthCheckBuilder builder)
        {
        }

        public IEnumerable<IModuleEngineDescriptor> DescribeEngines()
        {
            yield return new ModuleEngineDescriptor<IUiEngine<DummyCommand, DummyViewModel>>(
                Key,
                new ModuleEngineManifest(
                    "ui.required",
                    "1.0",
                    "Engine that requires host services",
                    EngineKind.Ui,
                    RequiredServices: new[] { "cache", "telemetry" }),
                sp => sp.GetRequiredService<DummyUiEngine>());
        }
    }

    private sealed record DummyCommand;

    private sealed record DummyViewModel(string Value);

    private sealed class DummyUiEngine : IUiEngine<DummyCommand, DummyViewModel>
    {
        public Task<UiEngineResult<DummyViewModel>> ExecuteAsync(DummyCommand command, CancellationToken cancellationToken)
        {
            return Task.FromResult(new UiEngineResult<DummyViewModel>(new DummyViewModel("ok")));
        }
    }

    private sealed class TestRequiredServiceValidator : IRequiredServiceValidator
    {
        private readonly HashSet<string> available;

        public TestRequiredServiceValidator(IEnumerable<string> available)
        {
            this.available = new HashSet<string>(available, StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyCollection<string> GetMissingServices(IReadOnlyCollection<string> requiredServices)
        {
            var missing = new List<string>();
            foreach (var service in requiredServices)
            {
                if (!available.Contains(service))
                {
                    missing.Add(service);
                }
            }

            return missing;
        }
    }
}
#pragma warning restore CA1861

