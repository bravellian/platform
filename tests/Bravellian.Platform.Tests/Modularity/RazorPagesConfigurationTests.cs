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
