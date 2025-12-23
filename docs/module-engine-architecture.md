# Module engine architecture

This document describes the transport-agnostic module engine system and how hosts, adapters, and modules collaborate to expose UI and webhook behaviors.

## Goals and scope

- Provide a consistent contract for modules to publish engines (UI, webhook, and future kinds) without coupling to transport concerns.
- Allow hosts to compose engines dynamically or statically while enforcing dependency, security, and compatibility requirements.
- Keep adapters thin: they translate transport details into engine contracts and validate required infrastructure.

## Core concepts

### Engine manifest

A `ModuleEngineManifest` accompanies every engine and captures metadata the host can reason about before instantiating the engine:

- Identity and version: `Id`, `Version`, `Description`, `Kind`, and optional `FeatureArea` identify the engine and the UI grouping it belongs to.
- Capabilities and schemas: optional `ModuleEngineCapabilities`, `Inputs`, and `Outputs` describe the actions/events and the DTO types used at the engine boundary.
- Navigation hints: `ModuleEngineNavigationHints` publishes well-known `ModuleNavigationToken` values that adapters map to routes, dialogs, or screens.
- Required services: `RequiredServices` lists logical dependencies (for example caching, API clients, telemetry) the host must satisfy via its DI container or adapter configuration.
- Adapter hints: `ModuleEngineAdapterHints` indicate whether the adapter needs to surface raw headers/body, challenge responses, or authenticated/tenant contexts.
- Security and compatibility: webhook-oriented `ModuleEngineSecurity`, optional `ModuleEngineCompatibility`, and `ModuleEngineWebhookMetadata` describe signature validation, idempotency windows, and supported providers/events.

Manifests are purely descriptive; they can be inspected during discovery to validate that a host can satisfy the engine’s prerequisites before activation.

### Engine descriptor and factory

Modules expose engines through strongly typed `ModuleEngineDescriptor<TContract>` instances. Each descriptor:

- Binds an engine to its owning module via `ModuleKey`.
- Carries the manifest.
- Provides a factory that resolves the engine from an `IServiceProvider`, enabling adapters to obtain concrete instances without reflection or `object` casting.

### Engine kinds and contracts

Two engine kinds are currently supported:

- **UI engines** implement `IUiEngine<TInput, TViewModel>` and return `UiEngineResult<TViewModel>` containing a view model and any navigation targets/events emitted by the engine.
- **Webhook engines** implement `IWebhookEngine<TPayload>` and return a `WebhookOutcome` that directs the adapter to acknowledge, retry, or enqueue follow-on work.

Both contracts operate purely on DTOs so engines remain isolated from HTTP, MVC, Razor, or serialization concerns.

### Registration and discovery

Module registration wires engines into the shared registry:

1. Modules implement `IModuleDefinition.DescribeEngines()` to return descriptors that belong to the module’s `Key`.
2. During module initialization, `ModuleEngineRegistry.Register` validates the descriptors (including webhook metadata uniqueness) and stores them in a per-module collection.
3. `ModuleEngineDiscoveryService` provides the host and adapters with filtered access to the registry: list by kind/feature area, resolve by module/engine id, or resolve webhook engines by provider/event type.
4. Discovery also resolves engine instances using the descriptor factory, enforcing non-null instance creation.

This separation lets isolation-focused deployments register known descriptors directly, while dynamic hosts can enumerate engines to build routing tables or configuration UIs.

## Adapter responsibilities

Adapters bridge transports to the transport-agnostic engine contracts. They are optional but provide reference implementations.

### UI adapter

`UiEngineAdapter` executes a UI engine and translates the result into a transport-ready `UiAdapterResponse<TViewModel>`:

- Resolves a descriptor by module key and engine id, ensuring the engine implements the expected UI contract.
- Validates `RequiredServices` using an optional `IRequiredServiceValidator`; missing services or empty identifiers produce errors before engine execution.
- Invokes the engine with the command DTO and returns the view model plus navigation tokens/events for the host to map to routes or client behaviors.

Typical use: API or MVC controllers create the adapter once (DI singleton) and invoke `ExecuteAsync` inside endpoint handlers.

### Webhook adapter

`WebhookEngineAdapter` connects webhook transports to webhook engines:

- Uses provider + event type to find the matching engine via discovery and validates `RequiredServices` combined from manifest-level and event-level declarations.
- Validates signatures using `IWebhookSignatureValidator` when `ModuleEngineSecurity` specifies an algorithm, and enforces idempotency keys when configured.
- Dispatches to the engine with a `WebhookRequest<TPayload>` and returns a `WebhookAdapterResponse` encapsulating the outcome (`Acknowledge`, `Retry`, or enqueue instructions).

Typical use: HTTP endpoints deserialize the webhook payload and raw metadata into `WebhookAdapterRequest<TPayload>`, then forward to `DispatchAsync`.

## Common host setups

### UI routing surface

- Register modules in DI with `AddModuleServices`, then add `UiEngineAdapter` as a singleton.
- Create endpoint handlers (e.g., minimal APIs or controllers) that accept module/engine identifiers and command DTOs, delegate to the adapter, and translate navigation tokens to routes/pages.
- Optionally use `Bravellian.Platform.Modularity.AspNetCore` and `MapUiEngineEndpoints` to wire a generic endpoint that deserializes inputs based on manifest schemas.
- Optionally expose manifest metadata (capabilities, navigation hints) to client apps to build menus or deep links dynamically.

### Razor Pages adapter

- Add `Bravellian.Platform.Modularity.Razor` and implement `IRazorModule` for modules that ship Razor Pages.
- Call `services.AddRazorPages().ConfigureRazorModulePages()` to register Razor conventions and application parts.

### Webhook intake surface

- Add `WebhookEngineAdapter` and a concrete `IWebhookSignatureValidator` to DI.
- Define HTTP endpoints that capture provider, event type, headers, raw body, payload DTO, signature, idempotency key, and attempt number into a `WebhookAdapterRequest<TPayload>`, or use `MapWebhookEngineEndpoints` from `Bravellian.Platform.Modularity.AspNetCore`.
- Let the adapter enforce signature and idempotency rules, check required services, and return an adapter response that the host maps to HTTP status codes or retry instructions.

### Mixed module deployments

- Modules can expose multiple engines across kinds; hosts choose whether to expose all engines dynamically (by listing manifests) or to pin a curated set by resolving specific descriptors.
- Feature areas in manifests allow hosts to group UI engines into navigation sections without inspecting implementation details.
- Because descriptors are strongly typed and per-module, hosts maintain isolation even when running multiple versions side-by-side.

## When to use which piece

- Use **manifests** to drive configuration UIs, host capability validation, and routing decisions before instantiating engines.
- Use **descriptors** when you want type-safe factories and module/engine identity during registration or resolution.
- Use **discovery service** in adapters and hosts that need to enumerate or look up engines at runtime.
- Use **UI adapter** when surfacing engines through HTTP/MVC/Blazor endpoints or other UI transports; it centralizes required-service checks and navigation translation.
- Use **webhook adapter** for inbound webhook endpoints that must validate signatures, idempotency, and required services before invoking business logic.

## Operational considerations

- **Required services**: declare logical dependencies in manifests (and webhook metadata) so hosts can validate availability up front. Provide an `IRequiredServiceValidator` implementation that maps logical names to registered services.
- **Security**: configure `ModuleEngineSecurity` with signature algorithm and idempotency window for webhook engines; the adapter will enforce them using the provided validator.
- **Compatibility**: leverage `ModuleEngineCompatibility` to document supported versions/hosts and surface deprecation notes without changing engine code.
- **Testing**: module and engine registries expose `Reset` helpers for isolated tests; avoid parallel tests that mutate global registry state.

## Next steps for new applications

1. Define module(s) implementing `IModuleDefinition` with clear `Key` values and manifest-rich engine descriptors.
2. Register module types with `ModuleRegistry.RegisterModule<T>()` so initialization loads configuration, registers engines, and wires health checks.
3. Choose adapter surfaces (UI, webhook, or both), register them with DI, and map HTTP routes or background triggers to adapter calls.
4. Validate required services and security settings during startup, optionally exposing manifest metadata to operators or client applications for discovery and governance.

