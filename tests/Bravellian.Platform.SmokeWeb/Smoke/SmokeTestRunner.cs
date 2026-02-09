using System;
using System.Globalization;
using Bravellian.Platform.Audit;
using Bravellian.Platform.Idempotency;
using Bravellian.Platform.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.SmokeWeb.Smoke;

public sealed class SmokeTestRunner : IDisposable
{
    private readonly SmokeTestState state;
    private readonly SmokeTestSignals signals;
    private readonly SmokeFanoutRepositories fanoutRepositories;
    private readonly SmokeRuntimeInfo runtimeInfo;
    private readonly SmokePlatformClientResolver platformClients;
    private readonly ISystemLeaseFactory leaseFactory;
    private readonly IServiceProvider serviceProvider;
    private readonly IDatabaseSchemaCompletion? schemaCompletion;
    private readonly TimeProvider timeProvider;
    private readonly SmokeOptions options;
    private readonly SemaphoreSlim runLock = new(1, 1);

    public SmokeTestRunner(
        SmokeTestState state,
        SmokeTestSignals signals,
        SmokeFanoutRepositories fanoutRepositories,
        SmokeRuntimeInfo runtimeInfo,
        SmokePlatformClientResolver platformClients,
        ISystemLeaseFactory leaseFactory,
        IServiceProvider serviceProvider,
        TimeProvider timeProvider,
        IOptions<SmokeOptions> options,
        IDatabaseSchemaCompletion? schemaCompletion = null)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.signals = signals ?? throw new ArgumentNullException(nameof(signals));
        this.fanoutRepositories = fanoutRepositories ?? throw new ArgumentNullException(nameof(fanoutRepositories));
        this.runtimeInfo = runtimeInfo ?? throw new ArgumentNullException(nameof(runtimeInfo));
        this.platformClients = platformClients ?? throw new ArgumentNullException(nameof(platformClients));
        this.leaseFactory = leaseFactory ?? throw new ArgumentNullException(nameof(leaseFactory));
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        this.schemaCompletion = schemaCompletion;
    }

    public async Task<SmokeRun> StartAsync(CancellationToken cancellationToken)
    {
        await runLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existingRun = state.CurrentRun;
            if (existingRun is { IsCompleted: false })
            {
                return existingRun;
            }

            var run = state.StartRun(runtimeInfo.Provider, timeProvider.GetUtcNow());
            _ = Task.Run(() => RunAsync(run), CancellationToken.None);
            return run;
        }
        finally
        {
            runLock.Release();
        }
    }

    private async Task RunAsync(SmokeRun run)
    {
        try
        {
            if (schemaCompletion != null)
            {
                try
                {
                    await schemaCompletion.SchemaDeploymentCompleted.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    state.MarkStepFailed(run, SmokeStepNames.Outbox, timeProvider.GetUtcNow(), $"Schema deployment failed: {ex.ToString()}");
                }
            }

            await RunLeaseAsync(run).ConfigureAwait(false);
            await RunLeaseStormAsync(run).ConfigureAwait(false);
            await RunOutboxAsync(run).ConfigureAwait(false);
            await RunInboxAsync(run).ConfigureAwait(false);
            await RunSchedulerAsync(run).ConfigureAwait(false);
            await RunFanoutSmallAsync(run).ConfigureAwait(false);
            await RunFanoutBurstAsync(run).ConfigureAwait(false);
            await RunIdempotencyAsync(run).ConfigureAwait(false);
            await RunOperationsAsync(run).ConfigureAwait(false);
            await RunAuditAsync(run).ConfigureAwait(false);
        }
        finally
        {
            state.MarkRunCompleted(run, timeProvider.GetUtcNow());
        }
    }

    private async Task RunLeaseAsync(SmokeRun run)
    {
        var started = timeProvider.GetUtcNow();
        state.MarkStepRunning(run, SmokeStepNames.Lease, started);

        try
        {
            var lease = await leaseFactory.AcquireAsync(
                "smoke:lease",
                TimeSpan.FromSeconds(15),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            if (lease == null)
            {
                state.MarkStepFailed(
                    run,
                    SmokeStepNames.Lease,
                    timeProvider.GetUtcNow(),
                    $"Lease not acquired (elapsed {FormatElapsed(started)}).");
                return;
            }

            await using (lease.ConfigureAwait(false))
            {
                var renewed = await lease.TryRenewNowAsync().ConfigureAwait(false);

                var concurrentLease = await leaseFactory.AcquireAsync(
                    "smoke:lease",
                    TimeSpan.FromSeconds(5),
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);

                if (concurrentLease != null)
                {
                    await concurrentLease.DisposeAsync().ConfigureAwait(false);
                    state.MarkStepFailed(
                        run,
                        SmokeStepNames.Lease,
                        timeProvider.GetUtcNow(),
                        $"Lease not exclusive (secondary acquire succeeded, renewed={renewed}, elapsed {FormatElapsed(started)}).");
                    return;
                }

                state.MarkStepSucceeded(
                    run,
                    SmokeStepNames.Lease,
                    timeProvider.GetUtcNow(),
                    $"Lease acquired and {(renewed ? "renewed" : "not renewed")} (exclusive, elapsed {FormatElapsed(started)}).");
            }

            var reacquired = await leaseFactory.AcquireAsync(
                "smoke:lease",
                TimeSpan.FromSeconds(5),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            if (reacquired == null)
            {
                state.MarkStepFailed(
                    run,
                    SmokeStepNames.Lease,
                    timeProvider.GetUtcNow(),
                    $"Lease release check failed (could not reacquire, elapsed {FormatElapsed(started)}).");
                return;
            }

            await reacquired.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            state.MarkStepFailed(run, SmokeStepNames.Lease, timeProvider.GetUtcNow(), ex.ToString());
        }
    }

    private async Task RunOutboxAsync(SmokeRun run)
    {
        var started = timeProvider.GetUtcNow();
        state.MarkStepRunning(run, SmokeStepNames.Outbox, started);

        try
        {
            var outbox = await platformClients.GetOutboxAsync(CancellationToken.None).ConfigureAwait(false);
            var payload = new SmokePayload(run.RunId, SmokeStepNames.Outbox, timeProvider.GetUtcNow());
            var json = JsonSerializer.Serialize(payload);
            await outbox.EnqueueAsync(SmokeTopics.Outbox, json, CancellationToken.None).ConfigureAwait(false);

            var timeout = GetTimeout();
            var signal = await signals.WaitAsync(run.RunId, SmokeStepNames.Outbox, timeout, CancellationToken.None).ConfigureAwait(false);
            if (signal.IsSuccess)
            {
                state.MarkStepSucceeded(
                    run,
                    SmokeStepNames.Outbox,
                    timeProvider.GetUtcNow(),
                    $"{signal.Message} (elapsed {FormatElapsed(started)}).");
            }
            else
            {
                state.MarkStepFailed(
                    run,
                    SmokeStepNames.Outbox,
                    timeProvider.GetUtcNow(),
                    $"{signal.Message} (timeout {FormatTimeout(timeout)}, elapsed {FormatElapsed(started)}).");
            }
        }
        catch (Exception ex)
        {
            state.MarkStepFailed(run, SmokeStepNames.Outbox, timeProvider.GetUtcNow(), ex.ToString());
        }
    }

    private async Task RunInboxAsync(SmokeRun run)
    {
        var started = timeProvider.GetUtcNow();
        state.MarkStepRunning(run, SmokeStepNames.Inbox, started);

        try
        {
            var inbox = await platformClients.GetInboxAsync(CancellationToken.None).ConfigureAwait(false);
            var payload = new SmokePayload(run.RunId, SmokeStepNames.Inbox, timeProvider.GetUtcNow());
            var json = JsonSerializer.Serialize(payload);
            var messageId = InboxMessageIdentifier.From(Guid.NewGuid().ToString("N"));

            await inbox.EnqueueAsync(
                SmokeTopics.Inbox,
                "smoke",
                messageId,
                json,
                CancellationToken.None).ConfigureAwait(false);

            var timeout = GetTimeout();
            var signal = await signals.WaitAsync(run.RunId, SmokeStepNames.Inbox, timeout, CancellationToken.None).ConfigureAwait(false);
            if (signal.IsSuccess)
            {
                state.MarkStepSucceeded(
                    run,
                    SmokeStepNames.Inbox,
                    timeProvider.GetUtcNow(),
                    $"{signal.Message} (elapsed {FormatElapsed(started)}).");
            }
            else
            {
                state.MarkStepFailed(
                    run,
                    SmokeStepNames.Inbox,
                    timeProvider.GetUtcNow(),
                    $"{signal.Message} (timeout {FormatTimeout(timeout)}, elapsed {FormatElapsed(started)}).");
            }
        }
        catch (Exception ex)
        {
            state.MarkStepFailed(run, SmokeStepNames.Inbox, timeProvider.GetUtcNow(), ex.ToString());
        }
    }

    private async Task RunSchedulerAsync(SmokeRun run)
    {
        var started = timeProvider.GetUtcNow();
        state.MarkStepRunning(run, SmokeStepNames.Scheduler, started);

        try
        {
            var scheduler = await platformClients.GetSchedulerAsync(CancellationToken.None).ConfigureAwait(false);
            var payload = new SmokePayload(run.RunId, SmokeStepNames.Scheduler, timeProvider.GetUtcNow());
            var json = JsonSerializer.Serialize(payload);
            var dueTime = timeProvider.GetUtcNow().AddSeconds(2);

            await scheduler.ScheduleTimerAsync(
                SmokeTopics.Scheduler,
                json,
                dueTime,
                CancellationToken.None).ConfigureAwait(false);

            var timeout = GetTimeout();
            var signal = await signals.WaitAsync(run.RunId, SmokeStepNames.Scheduler, timeout, CancellationToken.None).ConfigureAwait(false);
            if (signal.IsSuccess)
            {
                var cancelId = await scheduler.ScheduleTimerAsync(
                    SmokeTopics.Scheduler,
                    json,
                    timeProvider.GetUtcNow().AddSeconds(30),
                    CancellationToken.None).ConfigureAwait(false);

                var cancelOk = await scheduler.CancelTimerAsync(cancelId, CancellationToken.None).ConfigureAwait(false);
                var cancelAgain = await scheduler.CancelTimerAsync(cancelId, CancellationToken.None).ConfigureAwait(false);

                if (!cancelOk || cancelAgain)
                {
                    state.MarkStepFailed(
                        run,
                        SmokeStepNames.Scheduler,
                        timeProvider.GetUtcNow(),
                        $"Scheduler cancel check failed (first={cancelOk}, second={cancelAgain}, elapsed {FormatElapsed(started)}).");
                    return;
                }

                state.MarkStepSucceeded(
                    run,
                    SmokeStepNames.Scheduler,
                    timeProvider.GetUtcNow(),
                    $"{signal.Message} (elapsed {FormatElapsed(started)}).");
            }
            else
            {
                state.MarkStepFailed(
                    run,
                    SmokeStepNames.Scheduler,
                    timeProvider.GetUtcNow(),
                    $"{signal.Message} (due {dueTime:o}, timeout {FormatTimeout(timeout)}, elapsed {FormatElapsed(started)}).");
            }
        }
        catch (Exception ex)
        {
            state.MarkStepFailed(run, SmokeStepNames.Scheduler, timeProvider.GetUtcNow(), ex.ToString());
        }
    }

    private async Task RunLeaseStormAsync(SmokeRun run)
    {
        var started = timeProvider.GetUtcNow();
        state.MarkStepRunning(run, SmokeStepNames.LeaseStorm, started);

        try
        {
            const int contenders = 6;
            var tasks = Enumerable.Range(0, contenders)
                .Select(_ => leaseFactory.AcquireAsync(
                    "smoke:lease-storm",
                    TimeSpan.FromSeconds(5),
                    cancellationToken: CancellationToken.None))
                .ToArray();

            var leases = await Task.WhenAll(tasks).ConfigureAwait(false);
            var acquired = leases.Where(l => l != null).ToList();
            var acquiredCount = acquired.Count;

            foreach (var lease in acquired)
            {
                await lease!.DisposeAsync().ConfigureAwait(false);
            }

            if (acquiredCount != 1)
            {
                state.MarkStepFailed(
                    run,
                    SmokeStepNames.LeaseStorm,
                    timeProvider.GetUtcNow(),
                    $"Lease storm expected 1 winner but got {acquiredCount} (contenders {contenders}, elapsed {FormatElapsed(started)}).");
                return;
            }

            state.MarkStepSucceeded(
                run,
                SmokeStepNames.LeaseStorm,
                timeProvider.GetUtcNow(),
                $"Lease storm contention resolved (contenders {contenders}, elapsed {FormatElapsed(started)}).");
        }
        catch (Exception ex)
        {
            state.MarkStepFailed(run, SmokeStepNames.LeaseStorm, timeProvider.GetUtcNow(), ex.ToString());
        }
    }

    private async Task RunFanoutSmallAsync(SmokeRun run)
    {
        var started = timeProvider.GetUtcNow();
        state.MarkStepRunning(run, SmokeStepNames.FanoutSmall, started);

        try
        {
            var (policyRepository, cursorRepository) = await fanoutRepositories.GetAsync(CancellationToken.None).ConfigureAwait(false);
            await policyRepository.SetCadenceAsync(
                SmokeFanoutDefaults.FanoutTopic,
                SmokeFanoutDefaults.WorkKey,
                everySeconds: 1,
                jitterSeconds: 0,
                CancellationToken.None).ConfigureAwait(false);

            var payload = JsonSerializer.Serialize(new SmokeFanoutJobPayload(
                SmokeFanoutDefaults.FanoutTopic,
                SmokeFanoutDefaults.WorkKey));

            var scheduler = await platformClients.GetSchedulerAsync(CancellationToken.None).ConfigureAwait(false);
            await scheduler.CreateOrUpdateJobAsync(
                SmokeFanoutDefaults.JobName(SmokeFanoutDefaults.WorkKey),
                SmokeFanoutDefaults.JobTopic,
                SmokeFanoutDefaults.Cron,
                payload,
                CancellationToken.None).ConfigureAwait(false);

            await scheduler.TriggerJobAsync(SmokeFanoutDefaults.JobName(SmokeFanoutDefaults.WorkKey), CancellationToken.None).ConfigureAwait(false);

            var timeout = GetTimeout();
            var signal = await signals.WaitAsync(run.RunId, SmokeStepNames.FanoutSmall, timeout, CancellationToken.None).ConfigureAwait(false);
            if (!signal.IsSuccess)
            {
                var coordinatorProbe = await ProbeFanoutCoordinatorAsync(SmokeFanoutDefaults.WorkKey).ConfigureAwait(false);
                state.MarkStepFailed(
                    run,
                    SmokeStepNames.FanoutSmall,
                    timeProvider.GetUtcNow(),
                    $"{signal.Message} (timeout {FormatTimeout(timeout)}, elapsed {FormatElapsed(started)}){coordinatorProbe}.");
                return;
            }

            var lastCompleted = await cursorRepository.GetLastAsync(
                SmokeFanoutDefaults.FanoutTopic,
                SmokeFanoutDefaults.WorkKey,
                SmokeFanoutDefaults.ShardKey,
                CancellationToken.None).ConfigureAwait(false);

            if (lastCompleted == null)
            {
                state.MarkStepFailed(
                    run,
                    SmokeStepNames.FanoutSmall,
                    timeProvider.GetUtcNow(),
                    $"Fanout cursor not updated (elapsed {FormatElapsed(started)}).");
                return;
            }

            state.MarkStepSucceeded(
                run,
                SmokeStepNames.FanoutSmall,
                timeProvider.GetUtcNow(),
                $"{signal.Message} (cursor {lastCompleted:O}, elapsed {FormatElapsed(started)}).");
        }
        catch (Exception ex)
        {
            state.MarkStepFailed(run, SmokeStepNames.FanoutSmall, timeProvider.GetUtcNow(), ex.ToString());
        }
    }

    private async Task RunFanoutBurstAsync(SmokeRun run)
    {
        var started = timeProvider.GetUtcNow();
        state.MarkStepRunning(run, SmokeStepNames.FanoutBurst, started);

        try
        {
            var (policyRepository, cursorRepository) = await fanoutRepositories.GetAsync(CancellationToken.None).ConfigureAwait(false);
            await policyRepository.SetCadenceAsync(
                SmokeFanoutDefaults.FanoutTopic,
                SmokeFanoutDefaults.WorkKeyBurst,
                everySeconds: 1,
                jitterSeconds: 0,
                CancellationToken.None).ConfigureAwait(false);

            var payload = JsonSerializer.Serialize(new SmokeFanoutJobPayload(
                SmokeFanoutDefaults.FanoutTopic,
                SmokeFanoutDefaults.WorkKeyBurst));

            var scheduler = await platformClients.GetSchedulerAsync(CancellationToken.None).ConfigureAwait(false);
            await scheduler.CreateOrUpdateJobAsync(
                SmokeFanoutDefaults.JobName(SmokeFanoutDefaults.WorkKeyBurst),
                SmokeFanoutDefaults.JobTopic,
                SmokeFanoutDefaults.Cron,
                payload,
                CancellationToken.None).ConfigureAwait(false);

            await scheduler.TriggerJobAsync(SmokeFanoutDefaults.JobName(SmokeFanoutDefaults.WorkKeyBurst), CancellationToken.None).ConfigureAwait(false);

            var timeout = GetTimeout();
            var signal = await signals.WaitAsync(run.RunId, SmokeStepNames.FanoutBurst, timeout, CancellationToken.None).ConfigureAwait(false);
            if (!signal.IsSuccess)
            {
                var coordinatorProbe = await ProbeFanoutCoordinatorAsync(SmokeFanoutDefaults.WorkKeyBurst).ConfigureAwait(false);
                state.MarkStepFailed(
                    run,
                    SmokeStepNames.FanoutBurst,
                    timeProvider.GetUtcNow(),
                    $"{signal.Message} (timeout {FormatTimeout(timeout)}, elapsed {FormatElapsed(started)}){coordinatorProbe}.");
                return;
            }

            var deadline = timeProvider.GetUtcNow().Add(timeout);
            var pending = new HashSet<string>(SmokeFanoutDefaults.BurstShardKeys, StringComparer.OrdinalIgnoreCase);

            while (pending.Count > 0 && timeProvider.GetUtcNow() < deadline)
            {
                foreach (var shard in SmokeFanoutDefaults.BurstShardKeys)
                {
                    if (!pending.Contains(shard))
                    {
                        continue;
                    }

                    var lastCompleted = await cursorRepository.GetLastAsync(
                        SmokeFanoutDefaults.FanoutTopic,
                        SmokeFanoutDefaults.WorkKeyBurst,
                        shard,
                        CancellationToken.None).ConfigureAwait(false);

                    if (lastCompleted != null)
                    {
                        pending.Remove(shard);
                    }
                }

                if (pending.Count > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
                }
            }

            if (pending.Count > 0)
            {
                var coordinatorProbe = await ProbeFanoutCoordinatorAsync(SmokeFanoutDefaults.WorkKeyBurst).ConfigureAwait(false);
                state.MarkStepFailed(
                    run,
                    SmokeStepNames.FanoutBurst,
                    timeProvider.GetUtcNow(),
                    $"Fanout burst incomplete (missing {string.Join(", ", pending)}, elapsed {FormatElapsed(started)}){coordinatorProbe}.");
                return;
            }

            state.MarkStepSucceeded(
                run,
                SmokeStepNames.FanoutBurst,
                timeProvider.GetUtcNow(),
                $"{signal.Message} (shards {SmokeFanoutDefaults.BurstShardKeys.Count}, elapsed {FormatElapsed(started)}).");
        }
        catch (Exception ex)
        {
            state.MarkStepFailed(run, SmokeStepNames.FanoutBurst, timeProvider.GetUtcNow(), ex.ToString());
        }
    }

    private async Task RunIdempotencyAsync(SmokeRun run)
    {
        var started = timeProvider.GetUtcNow();
        state.MarkStepRunning(run, SmokeStepNames.Idempotency, started);

        try
        {
            var idempotencyStore = TryResolve<IIdempotencyStore>();
            if (idempotencyStore == null)
            {
                state.MarkStepSucceeded(
                    run,
                    SmokeStepNames.Idempotency,
                    timeProvider.GetUtcNow(),
                    $"Skipped (idempotency store not registered, elapsed {FormatElapsed(started)}).");
                return;
            }

            var key = $"smoke:{run.RunId}:idempotency";
            var first = await idempotencyStore.TryBeginAsync(key, CancellationToken.None).ConfigureAwait(false);
            if (!first)
            {
                state.MarkStepFailed(
                    run,
                    SmokeStepNames.Idempotency,
                    timeProvider.GetUtcNow(),
                    $"TryBegin returned false for new key (elapsed {FormatElapsed(started)}).");
                return;
            }

            await idempotencyStore.CompleteAsync(key, CancellationToken.None).ConfigureAwait(false);
            var second = await idempotencyStore.TryBeginAsync(key, CancellationToken.None).ConfigureAwait(false);
            if (second)
            {
                state.MarkStepFailed(
                    run,
                    SmokeStepNames.Idempotency,
                    timeProvider.GetUtcNow(),
                    $"Completed key was re-acquired (elapsed {FormatElapsed(started)}).");
                return;
            }

            var retryKey = $"{key}:retry";
            var retryFirst = await idempotencyStore.TryBeginAsync(retryKey, CancellationToken.None).ConfigureAwait(false);
            if (!retryFirst)
            {
                state.MarkStepFailed(
                    run,
                    SmokeStepNames.Idempotency,
                    timeProvider.GetUtcNow(),
                    $"TryBegin failed for retry key (elapsed {FormatElapsed(started)}).");
                return;
            }

            await idempotencyStore.FailAsync(retryKey, CancellationToken.None).ConfigureAwait(false);
            var retrySecond = await idempotencyStore.TryBeginAsync(retryKey, CancellationToken.None).ConfigureAwait(false);
            if (!retrySecond)
            {
                state.MarkStepFailed(
                    run,
                    SmokeStepNames.Idempotency,
                    timeProvider.GetUtcNow(),
                    $"Failed key did not reopen for retry (elapsed {FormatElapsed(started)}).");
                return;
            }

            state.MarkStepSucceeded(
                run,
                SmokeStepNames.Idempotency,
                timeProvider.GetUtcNow(),
                $"Idempotency checks OK (elapsed {FormatElapsed(started)}).");
        }
        catch (Exception ex)
        {
            state.MarkStepFailed(run, SmokeStepNames.Idempotency, timeProvider.GetUtcNow(), ex.ToString());
        }
    }

    private async Task RunOperationsAsync(SmokeRun run)
    {
        var started = timeProvider.GetUtcNow();
        state.MarkStepRunning(run, SmokeStepNames.Operations, started);

        try
        {
            var operationTracker = TryResolve<IOperationTracker>();
            if (operationTracker == null)
            {
                state.MarkStepSucceeded(
                    run,
                    SmokeStepNames.Operations,
                    timeProvider.GetUtcNow(),
                    $"Skipped (operation tracker not registered, elapsed {FormatElapsed(started)}).");
                return;
            }

            var tags = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["runId"] = run.RunId,
                ["provider"] = runtimeInfo.Provider,
            };

            var operationId = await operationTracker.StartAsync(
                "smoke-operation",
                correlationContext: null,
                parentOperationId: null,
                tags: tags,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            await operationTracker.UpdateProgressAsync(
                operationId,
                percentComplete: 42,
                message: "Smoke halfway",
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            await operationTracker.AddEventAsync(
                operationId,
                kind: "smoke",
                message: "Smoke event",
                dataJson: JsonSerializer.Serialize(new { run.RunId }),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            await operationTracker.CompleteAsync(
                operationId,
                OperationStatus.Succeeded,
                "Smoke complete",
                CancellationToken.None).ConfigureAwait(false);

            var snapshot = await operationTracker.GetSnapshotAsync(operationId, CancellationToken.None).ConfigureAwait(false);
            if (snapshot == null)
            {
                state.MarkStepFailed(
                    run,
                    SmokeStepNames.Operations,
                    timeProvider.GetUtcNow(),
                    $"Operation snapshot missing (elapsed {FormatElapsed(started)}).");
                return;
            }

            if (snapshot.Status != OperationStatus.Succeeded)
            {
                state.MarkStepFailed(
                    run,
                    SmokeStepNames.Operations,
                    timeProvider.GetUtcNow(),
                    $"Operation status {snapshot.Status} (elapsed {FormatElapsed(started)}).");
                return;
            }

            state.MarkStepSucceeded(
                run,
                SmokeStepNames.Operations,
                timeProvider.GetUtcNow(),
                $"Operation tracked (progress {snapshot.PercentComplete?.ToString("F0", CultureInfo.InvariantCulture) ?? "n/a"}%, elapsed {FormatElapsed(started)}).");
        }
        catch (Exception ex)
        {
            state.MarkStepFailed(run, SmokeStepNames.Operations, timeProvider.GetUtcNow(), ex.ToString());
        }
    }

    private async Task RunAuditAsync(SmokeRun run)
    {
        var started = timeProvider.GetUtcNow();
        state.MarkStepRunning(run, SmokeStepNames.Audit, started);

        try
        {
            var auditEventWriter = TryResolve<IAuditEventWriter>();
            var auditEventReader = TryResolve<IAuditEventReader>();
            if (auditEventWriter == null || auditEventReader == null)
            {
                state.MarkStepSucceeded(
                    run,
                    SmokeStepNames.Audit,
                    timeProvider.GetUtcNow(),
                    $"Skipped (audit services not registered, elapsed {FormatElapsed(started)}).");
                return;
            }

            var anchor = new EventAnchor("SmokeRun", run.RunId, "Subject");
            var eventName = $"smoke.audit.{run.RunId}";
            var auditEvent = new AuditEvent(
                AuditEventId.NewId(),
                timeProvider.GetUtcNow(),
                eventName,
                "Smoke audit event",
                EventOutcome.Success,
                new[] { anchor },
                dataJson: JsonSerializer.Serialize(new { run.RunId, Step = SmokeStepNames.Audit }));

            await auditEventWriter.WriteAsync(auditEvent, CancellationToken.None).ConfigureAwait(false);

            var timeout = GetTimeout();
            var deadline = timeProvider.GetUtcNow().Add(timeout);
            IReadOnlyList<AuditEvent>? events = null;

            while (timeProvider.GetUtcNow() < deadline)
            {
                events = await auditEventReader.QueryAsync(
                    new AuditQuery(
                        new[] { anchor },
                        fromUtc: started.AddMinutes(-5),
                        toUtc: timeProvider.GetUtcNow().AddMinutes(5),
                        name: eventName,
                        limit: 5),
                    CancellationToken.None).ConfigureAwait(false);

                if (events.Count > 0)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
            }

            if (events == null || events.Count == 0)
            {
                state.MarkStepFailed(
                    run,
                    SmokeStepNames.Audit,
                    timeProvider.GetUtcNow(),
                    $"Audit event not found (timeout {FormatTimeout(timeout)}, elapsed {FormatElapsed(started)}).");
                return;
            }

            state.MarkStepSucceeded(
                run,
                SmokeStepNames.Audit,
                timeProvider.GetUtcNow(),
                $"Audit write/read OK (events {events.Count}, elapsed {FormatElapsed(started)}).");
        }
        catch (Exception ex)
        {
            state.MarkStepFailed(run, SmokeStepNames.Audit, timeProvider.GetUtcNow(), ex.ToString());
        }
    }

    private TimeSpan GetTimeout()
    {
        var seconds = options.TimeoutSeconds;
        if (seconds <= 0)
        {
            seconds = 30;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private string FormatElapsed(DateTimeOffset started)
    {
        var elapsed = timeProvider.GetUtcNow() - started;
        return $"{elapsed.TotalSeconds:F1}s";
    }

    private static string FormatTimeout(TimeSpan timeout)
    {
        return $"{timeout.TotalSeconds:F0}s";
    }

    private async Task<string> ProbeFanoutCoordinatorAsync(string workKey)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var key = SmokeFanoutDefaults.CoordinatorKey(workKey);
            var coordinator = scope.ServiceProvider.GetKeyedService<IFanoutCoordinator>(key);
            if (coordinator == null)
            {
                return $"; coordinator missing for key {key}";
            }

            var count = await coordinator
                .RunAsync(SmokeFanoutDefaults.FanoutTopic, workKey, CancellationToken.None)
                .ConfigureAwait(false);
            return $"; coordinator probe dispatched {count} slice(s)";
        }
        catch (Exception ex)
        {
            return $"; coordinator probe failed: {ex.ToString()}";
        }
    }

    private T? TryResolve<T>()
        where T : class
    {
        try
        {
            return serviceProvider.GetService<T>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        runLock.Dispose();
    }
}
