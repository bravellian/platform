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

namespace Bravellian.Platform.Observability;
/// <summary>
/// Builder for configuring platform observability.
/// </summary>
public sealed class ObservabilityBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ObservabilityBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public ObservabilityBuilder(IServiceCollection services)
    {
        Services = services;
    }

    /// <summary>
    /// Gets the service collection.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Adds a watchdog alert sink.
    /// </summary>
    /// <param name="sink">The alert sink instance.</param>
    /// <returns>The builder for chaining.</returns>
    public ObservabilityBuilder AddWatchdogAlertSink(IWatchdogAlertSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        Services.AddSingleton(sink);
        return this;
    }

    /// <summary>
    /// Adds a watchdog alert sink using a factory.
    /// </summary>
    /// <param name="factory">A factory function to create the alert sink.</param>
    /// <returns>The builder for chaining.</returns>
    public ObservabilityBuilder AddWatchdogAlertSink(Func<IServiceProvider, IWatchdogAlertSink> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Services.AddSingleton(factory);
        return this;
    }

    /// <summary>
    /// Adds a functional watchdog alert sink.
    /// </summary>
    /// <param name="handler">The alert handler function.</param>
    /// <returns>The builder for chaining.</returns>
    public ObservabilityBuilder AddWatchdogAlertSink(Func<WatchdogAlertContext, System.Threading.CancellationToken, System.Threading.Tasks.Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Services.AddSingleton<IWatchdogAlertSink>(new DelegateAlertSink(handler));
        return this;
    }

    /// <summary>
    /// Adds a heartbeat sink.
    /// </summary>
    /// <param name="sink">The heartbeat sink instance.</param>
    /// <returns>The builder for chaining.</returns>
    public ObservabilityBuilder AddHeartbeatSink(IHeartbeatSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        Services.AddSingleton(sink);
        return this;
    }

    /// <summary>
    /// Adds a heartbeat sink using a factory.
    /// </summary>
    /// <param name="factory">A factory function to create the heartbeat sink.</param>
    /// <returns>The builder for chaining.</returns>
    public ObservabilityBuilder AddHeartbeatSink(Func<IServiceProvider, IHeartbeatSink> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Services.AddSingleton(factory);
        return this;
    }

    /// <summary>
    /// Adds a functional heartbeat sink.
    /// </summary>
    /// <param name="handler">The heartbeat handler function.</param>
    /// <returns>The builder for chaining.</returns>
    public ObservabilityBuilder AddHeartbeatSink(Func<HeartbeatContext, System.Threading.CancellationToken, System.Threading.Tasks.Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Services.AddSingleton<IHeartbeatSink>(new DelegateHeartbeatSink(handler));
        return this;
    }

    /// <summary>
    /// Adds platform health checks to the health check builder.
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    public ObservabilityBuilder AddPlatformHealthChecks()
    {
        Services.AddHealthChecks()
            .AddCheck<WatchdogHealthCheck>("watchdog", tags: new[] { "watchdog", "platform" });
        return this;
    }

    private sealed class DelegateAlertSink : IWatchdogAlertSink
    {
        private readonly Func<WatchdogAlertContext, System.Threading.CancellationToken, System.Threading.Tasks.Task> handler;

        public DelegateAlertSink(Func<WatchdogAlertContext, System.Threading.CancellationToken, System.Threading.Tasks.Task> handler)
        {
            this.handler = handler;
        }

        public System.Threading.Tasks.Task OnAlertAsync(WatchdogAlertContext context, System.Threading.CancellationToken cancellationToken)
        {
            return handler(context, cancellationToken);
        }
    }

    private sealed class DelegateHeartbeatSink : IHeartbeatSink
    {
        private readonly Func<HeartbeatContext, System.Threading.CancellationToken, System.Threading.Tasks.Task> handler;

        public DelegateHeartbeatSink(Func<HeartbeatContext, System.Threading.CancellationToken, System.Threading.Tasks.Task> handler)
        {
            this.handler = handler;
        }

        public System.Threading.Tasks.Task OnHeartbeatAsync(HeartbeatContext context, System.Threading.CancellationToken cancellationToken)
        {
            return handler(context, cancellationToken);
        }
    }
}
