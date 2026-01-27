# Engine contracts overview

This repository now exposes transport-agnostic engines that sit inside each module. Engines describe their contracts in a manifest and can be surfaced through adapters when a host wants to expose UI or webhook behaviors.

Modules implement `IModuleDefinition` and register themselves with `ModuleRegistry`. Hosts call `AddModuleServices` to load configuration, register health checks, and expose engine discovery.

## Core building blocks

- **Engine manifest** (`ModuleEngineManifest`)
  - Identifies the engine (`Id`, `Version`, `Description`, `Kind`, optional `FeatureArea`).
  - Declares capabilities, schemas, security, compatibility expectations, and navigation hints via strongly typed records/enums (`ModuleEngineNavigationHints`, `ModuleNavigationToken`, `NavigationTargetKind`, `ModuleSignatureAlgorithm`).
  - Declares `RequiredServices` – a list of logical capabilities (for example `IApiClient`, `ICache`, `ITelemetry`) that must be provided by the host for the engine to run. Hosts are expected to validate that every required service can be satisfied before executing an engine, typically by mapping each required service identifier to a concrete implementation in their DI container or adapter configuration. Adapters enforce this validation by consulting `IRequiredServiceValidator`; hosts should register a validator or surface a configuration error when validation fails.
  - Adds optional webhook metadata (`ModuleEngineWebhookMetadata`) for providers and event types.
- **Engine descriptor** (`ModuleEngineDescriptor<TContract>`) wraps the manifest with a strongly typed factory so hosts can resolve engines without `object` casts.
- **Discovery** (`ModuleEngineDiscoveryService`/`ModuleEngineRegistry`) stores descriptors and supports filtering by kind/feature or matching webhook provider + event type. Hosts can either enumerate engines (dynamic) or resolve known descriptors directly (static, isolation-focused deployments), and use the exposed manifests (including `RequiredServices`) to ensure all dependencies are available before wiring engines into request or worker pipelines.

## Adapter roles

Adapters remain optional: they map transport concerns to engines while keeping the engines themselves unaware of ASP.NET, MVC, or Razor. Reference integrations include:
- `UiEngineAdapter` → executes an `IUiEngine` and surfaces `UiAdapterResponse` with typed navigation tokens.
- `Bravellian.Platform.Webhooks` → provider-agnostic ingestion + processing pipeline for webhook engines.
- `Bravellian.Platform.Modularity.AspNetCore` → adds `MapUiEngineEndpoints` and `MapWebhookEngineEndpoints` for minimal API wiring based on manifest schemas (`Inputs`/`Outputs` and `WebhookMetadata`), forwarding webhook requests into the pipeline.

## Versioning and isolation

Each engine version is tracked in its manifest. Because descriptors are strongly typed and module-owned, a module can expose multiple UI or webhook engines while remaining an isolation boundary. Hosts that do not require dynamic discovery can register known descriptors directly and resolve them through the discovery service using the explicit generic overloads.
