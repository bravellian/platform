using Bravellian.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace Bravellian.Platform.SmokeWeb.Smoke;

public sealed class SmokePlatformClientResolver
{
    private readonly IServiceProvider serviceProvider;
    private string? cachedKey;

    public SmokePlatformClientResolver(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public async Task<IOutbox> GetOutboxAsync(CancellationToken cancellationToken)
    {
        var outbox = serviceProvider.GetService<IOutbox>();
        if (outbox != null)
        {
            return outbox;
        }

        var router = serviceProvider.GetRequiredService<IOutboxRouter>();
        var key = await ResolveDefaultKeyAsync(cancellationToken).ConfigureAwait(false);
        return router.GetOutbox(key);
    }

    public async Task<IInbox> GetInboxAsync(CancellationToken cancellationToken)
    {
        var inbox = serviceProvider.GetService<IInbox>();
        if (inbox != null)
        {
            return inbox;
        }

        var router = serviceProvider.GetRequiredService<IInboxRouter>();
        var key = await ResolveDefaultKeyAsync(cancellationToken).ConfigureAwait(false);
        return router.GetInbox(key);
    }

    public async Task<ISchedulerClient> GetSchedulerAsync(CancellationToken cancellationToken)
    {
        var scheduler = serviceProvider.GetService<ISchedulerClient>();
        if (scheduler != null)
        {
            return scheduler;
        }

        var router = serviceProvider.GetRequiredService<ISchedulerRouter>();
        var key = await ResolveDefaultKeyAsync(cancellationToken).ConfigureAwait(false);
        return router.GetSchedulerClient(key);
    }

    private async Task<string> ResolveDefaultKeyAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(cachedKey))
        {
            return cachedKey;
        }

        var discovery = serviceProvider.GetService<IPlatformDatabaseDiscovery>();
        if (discovery == null)
        {
            throw new InvalidOperationException("IPlatformDatabaseDiscovery is not registered.");
        }

        var databases = await discovery.DiscoverDatabasesAsync(cancellationToken).ConfigureAwait(false);
        var database = databases.FirstOrDefault();
        if (database == null || string.IsNullOrWhiteSpace(database.Name))
        {
            throw new InvalidOperationException("No platform databases discovered for routing.");
        }

        cachedKey = database.Name;
        return cachedKey;
    }
}
