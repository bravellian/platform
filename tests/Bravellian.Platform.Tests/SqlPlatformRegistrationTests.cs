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

using Bravellian.Platform.Audit;
using Bravellian.Platform.Metrics;
using Bravellian.Platform.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Bravellian.Platform.Tests;

public sealed class SqlPlatformRegistrationTests
{
    /// <summary>
    /// When AddSqlPlatform is called, core SQL Server storage dependencies should be registered.
    /// </summary>
    /// <intent>Verify the all-at-once SQL Server registration wires the required services.</intent>
    /// <scenario>Given a service collection configured with AddSqlPlatform.</scenario>
    /// <behavior>Then outbox, inbox, audit, operations, and metrics dependencies resolve.</behavior>
    [Fact]
    public void AddSqlPlatform_RegistersCoreDependencies()
    {
        var services = new ServiceCollection();

        services.AddSqlPlatform("Server=localhost;Database=Test;");

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IOutbox>());
        Assert.NotNull(provider.GetService<IInbox>());
        Assert.NotNull(provider.GetService<IAuditEventWriter>());
        Assert.NotNull(provider.GetService<IOperationTracker>());
        Assert.NotNull(provider.GetService<IMetricRegistrar>());
        Assert.NotNull(provider.GetService<IPlatformDatabaseDiscovery>());
    }
}

