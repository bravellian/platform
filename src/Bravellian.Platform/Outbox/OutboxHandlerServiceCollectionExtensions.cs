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

namespace Bravellian.Platform;

/// <summary>
/// Service collection extensions for registering outbox and inbox handlers.
/// </summary>
public static class OutboxHandlerServiceCollectionExtensions
{
    /// <summary>
    /// Registers an outbox handler for a specific topic.
    /// </summary>
    /// <typeparam name="THandler">The outbox handler implementation type.</typeparam>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddOutboxHandler<THandler>(this IServiceCollection services)
        where THandler : class, IOutboxHandler
    {
        services.AddSingleton<IOutboxHandler, THandler>();
        return services;
    }

    /// <summary>
    /// Registers an outbox handler using a factory function.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="factory">Factory function to create the handler instance.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddOutboxHandler(this IServiceCollection services, Func<IServiceProvider, IOutboxHandler> factory)
    {
        services.AddSingleton(factory);
        return services;
    }

    /// <summary>
    /// Registers an inbox handler for a specific topic.
    /// </summary>
    /// <typeparam name="THandler">The inbox handler implementation type.</typeparam>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddInboxHandler<THandler>(this IServiceCollection services)
        where THandler : class, IInboxHandler
    {
        services.AddSingleton<IInboxHandler, THandler>();
        return services;
    }

    /// <summary>
    /// Registers an inbox handler using a factory function.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="factory">Factory function to create the handler instance.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddInboxHandler(this IServiceCollection services, Func<IServiceProvider, IInboxHandler> factory)
    {
        services.AddSingleton(factory);
        return services;
    }
}
