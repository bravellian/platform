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
    /// <summary>When AddSqlOutbox is called with a missing connection string, then it throws an OptionsValidationException.</summary>
    /// <intent>Ensure outbox registration validates required connection string settings.</intent>
    /// <scenario>Given a ServiceCollection and SqlOutboxOptions with an empty ConnectionString.</scenario>
    /// <behavior>Then AddSqlOutbox throws OptionsValidationException.</behavior>
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

    /// <summary>When AddSqlInbox is called with cleanup enabled and a zero interval, then it throws an OptionsValidationException.</summary>
    /// <intent>Validate inbox cleanup interval must be greater than zero when enabled.</intent>
    /// <scenario>Given SqlInboxOptions with EnableAutomaticCleanup true and CleanupInterval set to TimeSpan.Zero.</scenario>
    /// <behavior>Then AddSqlInbox throws OptionsValidationException.</behavior>
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

    /// <summary>When AddSqlOutbox is called with cleanup enabled and a zero interval, then it throws an OptionsValidationException.</summary>
    /// <intent>Validate outbox cleanup interval must be greater than zero when enabled.</intent>
    /// <scenario>Given SqlOutboxOptions with EnableAutomaticCleanup true and CleanupInterval set to TimeSpan.Zero.</scenario>
    /// <behavior>Then AddSqlOutbox throws OptionsValidationException.</behavior>
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

    /// <summary>When AddSqlInbox is called with a missing connection string, then it throws an OptionsValidationException.</summary>
    /// <intent>Ensure inbox registration validates required connection string settings.</intent>
    /// <scenario>Given a ServiceCollection and SqlInboxOptions with an empty ConnectionString.</scenario>
    /// <behavior>Then AddSqlInbox throws OptionsValidationException.</behavior>
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

    /// <summary>When AddSqlInbox is called with a missing schema name, then it throws an OptionsValidationException.</summary>
    /// <intent>Ensure inbox registration requires a non-empty schema name.</intent>
    /// <scenario>Given SqlInboxOptions with a valid connection string and an empty SchemaName.</scenario>
    /// <behavior>Then AddSqlInbox throws OptionsValidationException.</behavior>
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

    /// <summary>When AddSqlOutbox is called with a missing schema name, then it throws an OptionsValidationException.</summary>
    /// <intent>Ensure outbox registration requires a non-empty schema name.</intent>
    /// <scenario>Given SqlOutboxOptions with a valid connection string and an empty SchemaName.</scenario>
    /// <behavior>Then AddSqlOutbox throws OptionsValidationException.</behavior>
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

    /// <summary>When AddSqlFanout is called with a missing connection string, then it throws an OptionsValidationException.</summary>
    /// <intent>Ensure fanout registration validates required connection string settings.</intent>
    /// <scenario>Given a ServiceCollection and SqlFanoutOptions with a whitespace ConnectionString.</scenario>
    /// <behavior>Then AddSqlFanout throws OptionsValidationException.</behavior>
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

    /// <summary>When AddSqlFanout is called with a missing schema name, then it throws an OptionsValidationException.</summary>
    /// <intent>Ensure fanout registration requires a non-empty schema name.</intent>
    /// <scenario>Given SqlFanoutOptions with a valid connection string and an empty SchemaName.</scenario>
    /// <behavior>Then AddSqlFanout throws OptionsValidationException.</behavior>
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
