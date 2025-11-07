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

namespace Bravellian.Platform;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Background service that registers fanout topics as recurring jobs with the scheduler.
/// This service runs once during startup to ensure all fanout topics are scheduled.
/// </summary>
internal sealed class FanoutJobRegistrationService : BackgroundService
{
    private readonly IServiceProvider serviceProvider;
    private readonly FanoutTopicOptions options;
    private readonly ILogger<FanoutJobRegistrationService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FanoutJobRegistrationService"/> class.
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving dependencies.</param>
    /// <param name="options">Fanout topic options for this registration.</param>
    public FanoutJobRegistrationService(IServiceProvider serviceProvider, FanoutTopicOptions options)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.logger = serviceProvider.GetRequiredService<ILogger<FanoutJobRegistrationService>>();
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Wait for schema deployment to complete if enabled
            var schemaCompletion = this.serviceProvider.GetService(typeof(IDatabaseSchemaCompletion)) as IDatabaseSchemaCompletion;
            if (schemaCompletion != null)
            {
                await schemaCompletion.SchemaDeploymentCompleted.ConfigureAwait(false);
            }

            // Register the fanout job with the scheduler
            using var scope = this.serviceProvider.CreateScope();
            var scheduler = scope.ServiceProvider.GetRequiredService<ISchedulerClient>();

            var jobName = this.options.WorkKey is null
                ? $"fanout-{this.options.FanoutTopic}"
                : $"fanout-{this.options.FanoutTopic}-{this.options.WorkKey}";

            var payload = JsonSerializer.Serialize(new FanoutJobHandler.FanoutJobPayload(
                this.options.FanoutTopic,
                this.options.WorkKey));

            await scheduler.CreateOrUpdateJobAsync(
                jobName: jobName,
                topic: "fanout.coordinate",
                cronSchedule: this.options.Cron,
                payload: payload).ConfigureAwait(false);

            this.logger.LogInformation(
                "Registered fanout job {JobName} for topic {FanoutTopic}:{WorkKey} with schedule {CronSchedule}",
                jobName,
                this.options.FanoutTopic,
                this.options.WorkKey,
                this.options.Cron);

            // Store the policy in the database for the planner to use
            var policyRepository = scope.ServiceProvider.GetRequiredService<IFanoutPolicyRepository>();
            await this.EnsurePolicyAsync(policyRepository, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.logger.LogError(
                ex,
                "Failed to register fanout job for topic {FanoutTopic}:{WorkKey}",
                this.options.FanoutTopic,
                this.options.WorkKey);

            // Don't rethrow - we don't want to crash the application, just log the error
        }
    }

    private async Task EnsurePolicyAsync(IFanoutPolicyRepository policyRepository, CancellationToken cancellationToken)
    {
        try
        {
            // Check if policy already exists - if so, we're done
            var existing = await policyRepository.GetCadenceAsync(
                this.options.FanoutTopic,
                this.options.WorkKey ?? "default",
                cancellationToken).ConfigureAwait(false);

            // Policy already exists, no need to insert
            if (existing.everySeconds > 0)
            {
                this.logger.LogDebug(
                    "Fanout policy already exists for {FanoutTopic}:{WorkKey}",
                    this.options.FanoutTopic,
                    this.options.WorkKey);
                return;
            }
        }
        catch (Exception ex)
        {
            this.logger.LogDebug(
                ex,
                "Policy check failed, will attempt to insert policy for {FanoutTopic}:{WorkKey}",
                this.options.FanoutTopic,
                this.options.WorkKey);
        }

        // Insert the policy (this method should handle upserts or ignore conflicts)
        try
        {
            await this.InsertPolicyAsync(policyRepository, cancellationToken).ConfigureAwait(false);
            this.logger.LogDebug(
                "Ensured fanout policy exists for {FanoutTopic}:{WorkKey}",
                this.options.FanoutTopic,
                this.options.WorkKey);
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(
                ex,
                "Failed to ensure fanout policy for {FanoutTopic}:{WorkKey}",
                this.options.FanoutTopic,
                this.options.WorkKey);
        }
    }

    private async Task InsertPolicyAsync(IFanoutPolicyRepository policyRepository, CancellationToken cancellationToken)
    {
        // We need to add a method to insert policies. For now, we'll create a simple SQL insert.
        // This should be moved to the repository interface in the future.
        var connectionString = this.serviceProvider.GetRequiredService<IOptionsSnapshot<SqlFanoutOptions>>().Value.ConnectionString;
        var schemaName = this.serviceProvider.GetRequiredService<IOptionsSnapshot<SqlFanoutOptions>>().Value.SchemaName;
        var tableName = this.serviceProvider.GetRequiredService<IOptionsSnapshot<SqlFanoutOptions>>().Value.PolicyTableName;

        var connection = new SqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = $"""

                        INSERT INTO [{schemaName}].[{tableName}] 
                            (FanoutTopic, WorkKey, DefaultEverySeconds, JitterSeconds, CreatedAt, UpdatedAt)
                        SELECT @FanoutTopic, @WorkKey, @DefaultEverySeconds, @JitterSeconds, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()
                        WHERE NOT EXISTS (
                            SELECT 1 FROM [{schemaName}].[{tableName}] 
                            WHERE FanoutTopic = @FanoutTopic AND WorkKey = @WorkKey
                        )
            """;

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@FanoutTopic", this.options.FanoutTopic);
            command.Parameters.AddWithValue("@WorkKey", this.options.WorkKey ?? "default");
            command.Parameters.AddWithValue("@DefaultEverySeconds", this.options.DefaultEverySeconds);
            command.Parameters.AddWithValue("@JitterSeconds", this.options.JitterSeconds);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
