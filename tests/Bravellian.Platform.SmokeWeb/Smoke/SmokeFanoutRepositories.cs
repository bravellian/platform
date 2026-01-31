using Bravellian.Platform;

namespace Bravellian.Platform.SmokeWeb.Smoke;

public sealed class SmokeFanoutRepositories
{
    private readonly IServiceProvider serviceProvider;
    private readonly IPlatformDatabaseDiscovery? discovery;
    private readonly IFanoutRouter? router;
    private string? cachedKey;

    public SmokeFanoutRepositories(
        IServiceProvider serviceProvider,
        IPlatformDatabaseDiscovery? discovery = null,
        IFanoutRouter? router = null)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        this.discovery = discovery;
        this.router = router;
    }

    public async Task<(IFanoutPolicyRepository policy, IFanoutCursorRepository cursor)> GetAsync(CancellationToken cancellationToken)
    {
        var policyRepository = serviceProvider.GetService<IFanoutPolicyRepository>();
        var cursorRepository = serviceProvider.GetService<IFanoutCursorRepository>();

        if (policyRepository != null && cursorRepository != null)
        {
            return (policyRepository, cursorRepository);
        }

        if (router == null || discovery == null)
        {
            throw new InvalidOperationException("Fanout repositories are not available. Ensure fanout services are registered.");
        }

        var key = cachedKey ??= await ResolveKeyAsync(cancellationToken).ConfigureAwait(false);
        return (router.GetPolicyRepository(key), router.GetCursorRepository(key));
    }

    private async Task<string> ResolveKeyAsync(CancellationToken cancellationToken)
    {
        var databases = await discovery!.DiscoverDatabasesAsync(cancellationToken).ConfigureAwait(false);
        var database = databases.FirstOrDefault();
        if (database == null || string.IsNullOrWhiteSpace(database.Name))
        {
            throw new InvalidOperationException("No platform databases discovered for fanout.");
        }

        return database.Name;
    }
}
