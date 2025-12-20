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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Tests;

public class OptionsValidationTests
{
    [Fact]
    public void AddSqlOutbox_ThrowsForMissingConnectionString()
    {
        var services = new ServiceCollection();

        Assert.Throws<OptionsValidationException>(() =>
            services.AddSqlOutbox(new SqlOutboxOptions
            {
                ConnectionString = string.Empty,
            }));
    }

    [Fact]
    public void AddSqlInbox_ThrowsForZeroCleanupInterval()
    {
        var services = new ServiceCollection();

        Assert.Throws<OptionsValidationException>(() =>
            services.AddSqlInbox(new SqlInboxOptions
            {
                ConnectionString = "Data Source=(local);Initial Catalog=Inbox;Integrated Security=True;TrustServerCertificate=True",
                EnableAutomaticCleanup = true,
                CleanupInterval = TimeSpan.Zero,
            }));
    }

    [Fact]
    public void AddSqlOutbox_ThrowsForZeroCleanupInterval()
    {
        var services = new ServiceCollection();

        Assert.Throws<OptionsValidationException>(() =>
            services.AddSqlOutbox(new SqlOutboxOptions
            {
                ConnectionString = "Data Source=(local);Initial Catalog=Outbox;Integrated Security=True;TrustServerCertificate=True",
                EnableAutomaticCleanup = true,
                CleanupInterval = TimeSpan.Zero,
            }));
    }

    [Fact]
    public void AddSqlInbox_ThrowsForMissingConnectionString()
    {
        var services = new ServiceCollection();

        Assert.Throws<OptionsValidationException>(() =>
            services.AddSqlInbox(new SqlInboxOptions
            {
                ConnectionString = string.Empty,
            }));
    }

    [Fact]
    public void AddSqlInbox_ThrowsForMissingSchemaName()
    {
        var services = new ServiceCollection();

        Assert.Throws<OptionsValidationException>(() =>
            services.AddSqlInbox(new SqlInboxOptions
            {
                ConnectionString = "Data Source=(local);Initial Catalog=Inbox;Integrated Security=True;TrustServerCertificate=True",
                SchemaName = string.Empty,
            }));
    }

    [Fact]
    public void AddSqlOutbox_ThrowsForMissingSchemaName()
    {
        var services = new ServiceCollection();

        Assert.Throws<OptionsValidationException>(() =>
            services.AddSqlOutbox(new SqlOutboxOptions
            {
                ConnectionString = "Data Source=(local);Initial Catalog=Outbox;Integrated Security=True;TrustServerCertificate=True",
                SchemaName = string.Empty,
            }));
    }

    [Fact]
    public void AddSqlFanout_ThrowsForMissingConnectionString()
    {
        var services = new ServiceCollection();

        Assert.Throws<OptionsValidationException>(() =>
            services.AddSqlFanout(new SqlFanoutOptions
            {
                ConnectionString = "   ",
            }));
    }

    [Fact]
    public void AddSqlFanout_ThrowsForMissingSchemaName()
    {
        var services = new ServiceCollection();

        Assert.Throws<OptionsValidationException>(() =>
            services.AddSqlFanout(new SqlFanoutOptions
            {
                ConnectionString = "Data Source=(local);Initial Catalog=Fanout;Integrated Security=True;TrustServerCertificate=True",
                SchemaName = string.Empty,
            }));
    }
}
