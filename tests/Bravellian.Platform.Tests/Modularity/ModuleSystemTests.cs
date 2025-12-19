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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Bravellian.Platform.Tests.Modularity;

[Collection("ModuleRegistryTests")]
public sealed class ModuleSystemTests
{
    [Fact]
    public void Background_modules_register_services_and_health()
    {
        ModuleRegistry.Reset();
        ModuleRegistry.RegisterBackgroundModule<SampleBackgroundModule>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SampleBackgroundModule.RequiredKey] = "value",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddBackgroundModuleServices(configuration, NullLoggerFactory.Instance);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<MarkerService>().Value.ShouldBe("value");

        var healthOptions = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        healthOptions.Registrations.ShouldContain(r => r.Name == "background_module");
    }

    [Fact]
    public void Api_modules_map_routes_using_registered_instances()
    {
        ModuleRegistry.Reset();
        ApiModuleRegistry.RegisterApiModule<SampleApiModule>();

        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [SampleApiModule.RequiredKey] = "abc",
        });

        builder.Services.AddApiModuleServices(builder.Configuration, NullLoggerFactory.Instance);
        var app = builder.Build();
        
        // Verify that MapModuleEndpoints executes without error and returns the app
        var result = app.MapModuleEndpoints();
        result.ShouldBe(app);
        
        // Verify module instance is registered
        var module = app.Services.GetService<IApiModule>();
        module.ShouldNotBeNull();
        module.Key.ShouldBe("sample-api");
    }

    [Fact]
    public void Full_stack_navigation_is_built_and_sorted()
    {
        ModuleRegistry.Reset();
        FullStackModuleRegistry.RegisterFullStackModule<SampleFullStackModule>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SampleFullStackModule.RequiredKey] = "xyz",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddFullStackModuleServices(configuration, NullLoggerFactory.Instance);

        using var provider = services.BuildServiceProvider();
        var navigation = provider.GetRequiredService<ModuleNavigationService>()
            .BuildNavigation();

        navigation.ShouldContain(entry => entry.FullPath == "/full-stack/home" && entry.Group == "Tools");
    }

    [Fact]
    public void Duplicate_keys_are_rejected()
    {
        ModuleRegistry.Reset();
        ApiModuleRegistry.RegisterApiModule<SampleApiModule>();
        FullStackModuleRegistry.RegisterFullStackModule<ConflictingModule>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SampleApiModule.RequiredKey] = "abc",
                [ConflictingModule.RequiredKey] = "xyz",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddApiModuleServices(configuration, NullLoggerFactory.Instance);

        Should.Throw<InvalidOperationException>(() => services.AddFullStackModuleServices(configuration, NullLoggerFactory.Instance));
    }

    [Fact]
    public void Module_keys_with_slashes_are_rejected()
    {
        ModuleRegistry.Reset();
        ApiModuleRegistry.RegisterApiModule<ModuleWithInvalidKey>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ModuleWithInvalidKey.RequiredKey] = "test",
            })
            .Build();

        var services = new ServiceCollection();
        
        var ex = Should.Throw<InvalidOperationException>(() => services.AddApiModuleServices(configuration, NullLoggerFactory.Instance));
        ex.Message.ShouldContain("invalid characters");
        ex.Message.ShouldContain("cannot contain slashes");
    }

    [Fact]
    public void Duplicate_module_type_registrations_in_same_category_are_idempotent()
    {
        ModuleRegistry.Reset();
        ApiModuleRegistry.RegisterApiModule<SampleApiModule>();
        
        // Second registration should be a no-op, not an error
        Should.NotThrow(() => ApiModuleRegistry.RegisterApiModule<SampleApiModule>());
    }

    [Fact]
    public void Module_cannot_be_registered_in_multiple_categories()
    {
        ModuleRegistry.Reset();
        ApiModuleRegistry.RegisterApiModule<DualInterfaceModule>();

        var ex = Should.Throw<InvalidOperationException>(() => FullStackModuleRegistry.RegisterFullStackModule<DualInterfaceModule>());
        ex.Message.ShouldContain("already registered in a different category");
    }

    private sealed class SampleBackgroundModule : IBackgroundModule
    {
        internal const string RequiredKey = "sample:required";
        private string? value;

        public string Key => "background-module";

        public string DisplayName => "Background Module";

        public IEnumerable<string> GetRequiredConfigurationKeys()
        {
            yield return RequiredKey;
        }

        public IEnumerable<string> GetOptionalConfigurationKeys() => Array.Empty<string>();

        public void LoadConfiguration(IReadOnlyDictionary<string, string> required, IReadOnlyDictionary<string, string> optional)
        {
            value = required[RequiredKey];
        }

        public void AddModuleServices(IServiceCollection services)
        {
            services.AddSingleton(new MarkerService(value ?? string.Empty));
        }

        public void RegisterHealthChecks(ModuleHealthCheckBuilder builder)
        {
            builder.AddCheck("background_module", () => HealthCheckResult.Healthy());
        }
    }

    private sealed class SampleApiModule : IApiModule
    {
        internal const string RequiredKey = "sample-api:required";
        private string? value;

        public string Key => "sample-api";

        public string DisplayName => "Sample API";

        public IEnumerable<string> GetRequiredConfigurationKeys()
        {
            yield return RequiredKey;
        }

        public IEnumerable<string> GetOptionalConfigurationKeys() => Array.Empty<string>();

        public void LoadConfiguration(IReadOnlyDictionary<string, string> required, IReadOnlyDictionary<string, string> optional)
        {
            value = required[RequiredKey];
        }

        public void AddModuleServices(IServiceCollection services)
        {
            services.AddSingleton(new MarkerService(value ?? string.Empty));
        }

        public void RegisterHealthChecks(ModuleHealthCheckBuilder builder)
        {
            builder.AddCheck("api_module", () => HealthCheckResult.Healthy());
        }

        public void MapApiEndpoints(RouteGroupBuilder group)
        {
            group.MapGet("/status", () => Results.Ok(new { Value = value }));
        }
    }

    private sealed class SampleFullStackModule : IFullStackModule, INavigationModuleMetadata
    {
        internal const string RequiredKey = "fullstack:required";
        private string? value;

        public string Key => "full-stack";

        public string DisplayName => "Full Stack Module";

        public string AreaName => "FullStack";

        public string NavigationGroup => "Tools";

        public int NavigationOrder => 1;

        public IEnumerable<string> GetRequiredConfigurationKeys()
        {
            yield return RequiredKey;
        }

        public IEnumerable<string> GetOptionalConfigurationKeys() => Array.Empty<string>();

        public void LoadConfiguration(IReadOnlyDictionary<string, string> required, IReadOnlyDictionary<string, string> optional)
        {
            value = required[RequiredKey];
        }

        public void AddModuleServices(IServiceCollection services)
        {
            services.AddSingleton(new MarkerService(value ?? string.Empty));
        }

        public void RegisterHealthChecks(ModuleHealthCheckBuilder builder)
        {
            builder.AddCheck("full_stack_module", () => HealthCheckResult.Healthy());
        }

        public void MapApiEndpoints(RouteGroupBuilder group)
        {
            group.MapGet("/info", () => Results.Ok());
        }

        public void ConfigureRazorPages(RazorPagesOptions options)
        {
            options.Conventions.AuthorizeAreaFolder(AreaName, "/");
        }

        public IEnumerable<ModuleNavLink> GetNavLinks()
        {
            yield return ModuleNavLink.Create("Home", "/home", 0, "home");
        }
    }

    private sealed class ConflictingModule : IFullStackModule
    {
        internal const string RequiredKey = "conflict:required";

        public string Key => "sample-api";

        public string DisplayName => "Conflict";

        public string AreaName => "Conflict";

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

        public void ConfigureRazorPages(RazorPagesOptions options)
        {
        }

        public IEnumerable<ModuleNavLink> GetNavLinks()
        {
            yield break;
        }

        public void MapApiEndpoints(RouteGroupBuilder group)
        {
        }
    }

    private sealed class ModuleWithInvalidKey : IApiModule
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

        public void MapApiEndpoints(RouteGroupBuilder group)
        {
        }
    }

    private sealed class DualInterfaceModule : IApiModule, IFullStackModule
    {
        internal const string RequiredKey = "dual:required";

        public string Key => "dual-module";
        public string DisplayName => "Dual Module";
        public string AreaName => "Dual";

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

        public void MapApiEndpoints(RouteGroupBuilder group)
        {
        }

        public void ConfigureRazorPages(RazorPagesOptions options)
        {
        }

        public IEnumerable<ModuleNavLink> GetNavLinks()
        {
            yield break;
        }
    }

    private sealed record MarkerService(string Value);
}
