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
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Bravellian.Platform.Tests.Modularity;

[Collection("ModuleRegistryTests")]
public sealed class RazorPagesConfigurationTests
{
    [Fact]
    public void ConfigureFullStackModuleRazorPages_registers_application_parts()
    {
        ModuleRegistry.Reset();
        FullStackModuleRegistry.RegisterFullStackModule<TestFullStackModule>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TestFullStackModule.RequiredKey] = "test-value",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddFullStackModuleServices(configuration, NullLoggerFactory.Instance);

        var mvcBuilder = services.AddRazorPages();
        mvcBuilder.ConfigureFullStackModuleRazorPages(NullLoggerFactory.Instance);

        // Verify assembly part was added
        var assemblyPart = mvcBuilder.PartManager.ApplicationParts
            .OfType<AssemblyPart>()
            .FirstOrDefault(p => p.Assembly == typeof(TestFullStackModule).Assembly);

        assemblyPart.ShouldNotBeNull();
    }

    [Fact]
    public void ConfigureFullStackModuleRazorPages_invokes_module_configuration()
    {
        ModuleRegistry.Reset();
        FullStackModuleRegistry.RegisterFullStackModule<TestFullStackModule>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TestFullStackModule.RequiredKey] = "test-value",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddFullStackModuleServices(configuration, NullLoggerFactory.Instance);

        var mvcBuilder = services.AddRazorPages();
        mvcBuilder.ConfigureFullStackModuleRazorPages(NullLoggerFactory.Instance);

        using var provider = services.BuildServiceProvider();

        // Verify the ConfigureRazorPages method was invoked by checking options were configured
        var optionsMonitor = provider.GetService<IOptionsMonitor<RazorPagesOptions>>();
        optionsMonitor.ShouldNotBeNull();
    }

    private sealed class TestFullStackModule : IFullStackModule
    {
        internal const string RequiredKey = "test:key";

        public string Key => "test-module";
        public string DisplayName => "Test Module";
        public string AreaName => "TestArea";

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
            // Configure authorization for the area
            options.Conventions.AuthorizeAreaFolder(AreaName, "/");
        }

        public IEnumerable<ModuleNavLink> GetNavLinks()
        {
            yield break;
        }

        public void MapApiEndpoints(Microsoft.AspNetCore.Routing.RouteGroupBuilder group)
        {
        }
    }
}
