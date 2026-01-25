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
using Xunit;

namespace Bravellian.Platform.Tests.Modularity;

[Collection("ModuleRegistryTests")]
public sealed class RazorPagesConfigurationTests
{
    /// <summary>
    /// When ConfigureRazorModulePages is invoked for a registered Razor module, then its assembly is added to the Razor application parts.
    /// </summary>
    /// <intent>
    /// Verify that Razor module assemblies are registered with Razor pages.
    /// </intent>
    /// <scenario>
    /// Given a module registry containing TestRazorModule and services configured with the required configuration key.
    /// </scenario>
    /// <behavior>
    /// Then the MVC ApplicationPartManager includes an AssemblyPart for the module assembly.
    /// </behavior>
    [Fact]
    public void ConfigureRazorModulePages_registers_application_parts()
    {
        ModuleRegistry.Reset();
        ModuleRegistry.RegisterModule<TestRazorModule>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [TestRazorModule.RequiredKey] = "test-value",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddModuleServices(configuration, NullLoggerFactory.Instance);

        var mvcBuilder = services.AddRazorPages();
        mvcBuilder.ConfigureRazorModulePages(NullLoggerFactory.Instance);

        var assemblyPart = mvcBuilder.PartManager.ApplicationParts
            .OfType<AssemblyPart>()
            .FirstOrDefault(p => p.Assembly == typeof(TestRazorModule).Assembly);

        assemblyPart.ShouldNotBeNull();
    }

    /// <summary>
    /// When ConfigureRazorModulePages runs for a registered module, then RazorPagesOptions can be resolved from the service provider.
    /// </summary>
    /// <intent>
    /// Confirm that module Razor configuration wires up Razor pages options in DI.
    /// </intent>
    /// <scenario>
    /// Given TestRazorModule is registered and services are configured with its required configuration key.
    /// </scenario>
    /// <behavior>
    /// Then an IOptionsMonitor<RazorPagesOptions> is available from the built service provider.
    /// </behavior>
    [Fact]
    public void ConfigureRazorModulePages_invokes_module_configuration()
    {
        ModuleRegistry.Reset();
        ModuleRegistry.RegisterModule<TestRazorModule>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [TestRazorModule.RequiredKey] = "test-value",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddModuleServices(configuration, NullLoggerFactory.Instance);

        var mvcBuilder = services.AddRazorPages();
        mvcBuilder.ConfigureRazorModulePages(NullLoggerFactory.Instance);

        using var provider = services.BuildServiceProvider();

        var optionsMonitor = provider.GetService<IOptionsMonitor<RazorPagesOptions>>();
        optionsMonitor.ShouldNotBeNull();
    }

    private sealed class TestRazorModule : IRazorModule
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
            options.Conventions.AuthorizeAreaFolder(AreaName, "/");
        }

        public IEnumerable<IModuleEngineDescriptor> DescribeEngines() => Array.Empty<IModuleEngineDescriptor>();
    }
}
