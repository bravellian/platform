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

using System.Runtime.CompilerServices;

namespace Bravellian.Platform.Examples;

/// <summary>
/// Example fanout planner for ETL operations that demonstrates how to implement
/// the IFanoutPlanner interface for a multi-tenant system.
/// 
/// This planner enumerates tenants and work types (payments, vendors, contacts)
/// and determines which combinations need processing based on their last completion times.
/// </summary>
public sealed class EtlFanoutPlanner : BaseFanoutPlanner
{
    private readonly ITenantReader tenantReader;

    /// <summary>
    /// Initializes a new instance of the <see cref="EtlFanoutPlanner"/> class.
    /// </summary>
    /// <param name="policyRepository">Repository for fanout policies and cadence settings.</param>
    /// <param name="cursorRepository">Repository for tracking completion cursors.</param>
    /// <param name="timeProvider">Time provider for current timestamp operations.</param>
    /// <param name="tenantReader">Service for reading tenant information.</param>
    public EtlFanoutPlanner(
        IFanoutPolicyRepository policyRepository,
        IFanoutCursorRepository cursorRepository,
        TimeProvider timeProvider,
        ITenantReader tenantReader)
        : base(policyRepository, cursorRepository, timeProvider)
    {
        this.tenantReader = tenantReader ?? throw new ArgumentNullException(nameof(tenantReader));
    }

    /// <inheritdoc/>
    protected override async IAsyncEnumerable<(string ShardKey, string WorkKey)> EnumerateCandidatesAsync(
        string fanoutTopic,
        string? workKey,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Get all enabled tenants that should participate in ETL processing
        var tenants = await this.tenantReader.ListEnabledAsync(ct).ConfigureAwait(false);

        // Define the work types available for ETL processing
        var allWorkKeys = new[] { "payments", "vendors", "contacts", "products" };
        
        // If a specific work key is provided, only process that work type
        var targetWorkKeys = workKey is null ? allWorkKeys : new[] { workKey };

        // Generate all combinations of tenant (shard) and work type
        foreach (var tenant in tenants)
        {
            foreach (var wk in targetWorkKeys)
            {
                yield return (ShardKey: tenant.Id.ToString("D"), WorkKey: wk);
            }
        }
    }
}

/// <summary>
/// Interface for reading tenant information for fanout planning.
/// This would be implemented by your application's tenant management system.
/// </summary>
public interface ITenantReader
{
    /// <summary>
    /// Gets all tenants that are enabled for processing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A list of enabled tenants.</returns>
    Task<IReadOnlyList<TenantInfo>> ListEnabledAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Represents basic tenant information for fanout planning.
/// </summary>
public sealed record TenantInfo(Guid Id, string Name, bool IsEnabled);

/// <summary>
/// Example outbox handler that processes payment ETL slices.
/// This demonstrates how to handle fanout slices in the downstream processing pipeline.
/// </summary>
public sealed class PaymentEtlHandler : IOutboxHandler
{
    private readonly IInbox inbox;
    private readonly IFanoutCursorRepository cursorRepository;
    private readonly IPaymentEtlService etlService;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="PaymentEtlHandler"/> class.
    /// </summary>
    /// <param name="inbox">Inbox service for idempotency.</param>
    /// <param name="cursorRepository">Repository for updating completion cursors.</param>
    /// <param name="etlService">Service for performing payment ETL operations.</param>
    /// <param name="timeProvider">Time provider for timestamps.</param>
    public PaymentEtlHandler(
        IInbox inbox,
        IFanoutCursorRepository cursorRepository,
        IPaymentEtlService etlService,
        TimeProvider timeProvider)
    {
        this.inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        this.cursorRepository = cursorRepository ?? throw new ArgumentNullException(nameof(cursorRepository));
        this.etlService = etlService ?? throw new ArgumentNullException(nameof(etlService));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc/>
    public string Topic => "fanout:etl:payments";

    /// <inheritdoc/>
    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        // Deserialize the fanout slice from the message payload
        var slice = System.Text.Json.JsonSerializer.Deserialize<FanoutSlice>(message.Payload)!;

        // Create idempotency key from slice components
        var idempotencyKey = $"{slice.FanoutTopic}|{slice.WorkKey}|{slice.ShardKey}|{slice.WindowStart:O}";

        // Check if this slice has already been processed
        if (await this.inbox.AlreadyProcessedAsync(idempotencyKey, "fanout", cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            return; // Already processed, skip
        }

        // Mark as being processed
        await this.inbox.MarkProcessingAsync(idempotencyKey, cancellationToken).ConfigureAwait(false);

        try
        {
            // Determine the time window for ETL processing
            var since = slice.WindowStart ?? this.timeProvider.GetUtcNow().AddHours(-1); // Default to last hour
            var until = this.timeProvider.GetUtcNow();

            // Perform the ETL operation for this shard (tenant) and time window
            await this.etlService.ProcessPaymentsAsync(
                tenantId: Guid.Parse(slice.ShardKey),
                since: since,
                until: until,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Update the completion cursor to track progress
            await this.cursorRepository.MarkCompletedAsync(
                slice.FanoutTopic,
                slice.WorkKey,
                slice.ShardKey,
                until,
                cancellationToken).ConfigureAwait(false);

            // Mark as successfully processed
            await this.inbox.MarkProcessedAsync(idempotencyKey, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Mark as dead letter on error (the outbox will handle retries)
            await this.inbox.MarkDeadAsync(idempotencyKey, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}

/// <summary>
/// Interface for payment ETL operations.
/// This would be implemented by your application's ETL processing service.
/// </summary>
public interface IPaymentEtlService
{
    /// <summary>
    /// Processes payments for a specific tenant within a time window.
    /// </summary>
    /// <param name="tenantId">The tenant ID to process payments for.</param>
    /// <param name="since">The start of the time window.</param>
    /// <param name="until">The end of the time window.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    Task ProcessPaymentsAsync(Guid tenantId, DateTimeOffset since, DateTimeOffset until, CancellationToken cancellationToken);
}