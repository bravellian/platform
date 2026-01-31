using Bravellian.Platform;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.SmokeWeb.Smoke;

public sealed class SmokeTestRunner
{
    private readonly SmokeTestState state;
    private readonly SmokeTestSignals signals;
    private readonly SmokeFanoutRepositories fanoutRepositories;
    private readonly SmokeRuntimeInfo runtimeInfo;
    private readonly SmokePlatformClientResolver platformClients;
    private readonly ISystemLeaseFactory leaseFactory;
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
                    state.MarkStepFailed(run, SmokeStepNames.Outbox, timeProvider.GetUtcNow(), $"Schema deployment failed: {ex.Message}");
                }
            }

            await RunLeaseAsync(run).ConfigureAwait(false);
            await RunOutboxAsync(run).ConfigureAwait(false);
            await RunInboxAsync(run).ConfigureAwait(false);
            await RunSchedulerAsync(run).ConfigureAwait(false);
            await RunFanoutAsync(run).ConfigureAwait(false);
        }
        finally
        {
            state.MarkRunCompleted(run, timeProvider.GetUtcNow());
        }
    }

    private async Task RunLeaseAsync(SmokeRun run)
    {
        state.MarkStepRunning(run, SmokeStepNames.Lease, timeProvider.GetUtcNow());

        try
        {
            var lease = await leaseFactory.AcquireAsync(
                "smoke:lease",
                TimeSpan.FromSeconds(15),
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            if (lease == null)
            {
                state.MarkStepFailed(run, SmokeStepNames.Lease, timeProvider.GetUtcNow(), "Lease not acquired.");
                return;
            }

            await using (lease.ConfigureAwait(false))
            {
                var renewed = await lease.TryRenewNowAsync().ConfigureAwait(false);
                state.MarkStepSucceeded(run, SmokeStepNames.Lease, timeProvider.GetUtcNow(), renewed ? "Lease acquired and renewed." : "Lease acquired but could not renew.");
            }
        }
        catch (Exception ex)
        {
            state.MarkStepFailed(run, SmokeStepNames.Lease, timeProvider.GetUtcNow(), ex.Message);
        }
    }

    private async Task RunOutboxAsync(SmokeRun run)
    {
        state.MarkStepRunning(run, SmokeStepNames.Outbox, timeProvider.GetUtcNow());

        try
        {
            var outbox = await platformClients.GetOutboxAsync(CancellationToken.None).ConfigureAwait(false);
            var payload = new SmokePayload(run.RunId, SmokeStepNames.Outbox, timeProvider.GetUtcNow());
            var json = JsonSerializer.Serialize(payload);
            await outbox.EnqueueAsync(SmokeTopics.Outbox, json, CancellationToken.None).ConfigureAwait(false);

            var signal = await signals.WaitAsync(run.RunId, SmokeStepNames.Outbox, GetTimeout(), CancellationToken.None).ConfigureAwait(false);
            if (signal.IsSuccess)
            {
                state.MarkStepSucceeded(run, SmokeStepNames.Outbox, timeProvider.GetUtcNow(), signal.Message);
            }
            else
            {
                state.MarkStepFailed(run, SmokeStepNames.Outbox, timeProvider.GetUtcNow(), signal.Message);
            }
        }
        catch (Exception ex)
        {
            state.MarkStepFailed(run, SmokeStepNames.Outbox, timeProvider.GetUtcNow(), ex.Message);
        }
    }

    private async Task RunInboxAsync(SmokeRun run)
    {
        state.MarkStepRunning(run, SmokeStepNames.Inbox, timeProvider.GetUtcNow());

        try
        {
            var inbox = await platformClients.GetInboxAsync(CancellationToken.None).ConfigureAwait(false);
            var payload = new SmokePayload(run.RunId, SmokeStepNames.Inbox, timeProvider.GetUtcNow());
            var json = JsonSerializer.Serialize(payload);
            var messageId = Guid.NewGuid().ToString("N");

            await inbox.EnqueueAsync(
                SmokeTopics.Inbox,
                "smoke",
                messageId,
                json,
                CancellationToken.None).ConfigureAwait(false);

            var signal = await signals.WaitAsync(run.RunId, SmokeStepNames.Inbox, GetTimeout(), CancellationToken.None).ConfigureAwait(false);
            if (signal.IsSuccess)
            {
                state.MarkStepSucceeded(run, SmokeStepNames.Inbox, timeProvider.GetUtcNow(), signal.Message);
            }
            else
            {
                state.MarkStepFailed(run, SmokeStepNames.Inbox, timeProvider.GetUtcNow(), signal.Message);
            }
        }
        catch (Exception ex)
        {
            state.MarkStepFailed(run, SmokeStepNames.Inbox, timeProvider.GetUtcNow(), ex.Message);
        }
    }

    private async Task RunSchedulerAsync(SmokeRun run)
    {
        state.MarkStepRunning(run, SmokeStepNames.Scheduler, timeProvider.GetUtcNow());

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

            var signal = await signals.WaitAsync(run.RunId, SmokeStepNames.Scheduler, GetTimeout(), CancellationToken.None).ConfigureAwait(false);
            if (signal.IsSuccess)
            {
                state.MarkStepSucceeded(run, SmokeStepNames.Scheduler, timeProvider.GetUtcNow(), signal.Message);
            }
            else
            {
                state.MarkStepFailed(run, SmokeStepNames.Scheduler, timeProvider.GetUtcNow(), signal.Message);
            }
        }
        catch (Exception ex)
        {
            state.MarkStepFailed(run, SmokeStepNames.Scheduler, timeProvider.GetUtcNow(), ex.Message);
        }
    }

    private async Task RunFanoutAsync(SmokeRun run)
    {
        state.MarkStepRunning(run, SmokeStepNames.Fanout, timeProvider.GetUtcNow());

        try
        {
            var (policyRepository, _) = await fanoutRepositories.GetAsync(CancellationToken.None).ConfigureAwait(false);
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
                SmokeFanoutDefaults.JobName,
                SmokeFanoutDefaults.JobTopic,
                SmokeFanoutDefaults.Cron,
                payload,
                CancellationToken.None).ConfigureAwait(false);

            await scheduler.TriggerJobAsync(SmokeFanoutDefaults.JobName, CancellationToken.None).ConfigureAwait(false);

            var signal = await signals.WaitAsync(run.RunId, SmokeStepNames.Fanout, GetTimeout(), CancellationToken.None).ConfigureAwait(false);
            if (signal.IsSuccess)
            {
                state.MarkStepSucceeded(run, SmokeStepNames.Fanout, timeProvider.GetUtcNow(), signal.Message);
            }
            else
            {
                state.MarkStepFailed(run, SmokeStepNames.Fanout, timeProvider.GetUtcNow(), signal.Message);
            }
        }
        catch (Exception ex)
        {
            state.MarkStepFailed(run, SmokeStepNames.Fanout, timeProvider.GetUtcNow(), ex.Message);
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
}
