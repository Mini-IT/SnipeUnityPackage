# Migration from v8 to v9 (DI-first)

This version removes hidden static dependencies from most runtime paths and requires explicit
`ISnipeServices` wiring. Existing call sites that relied on `SnipeServices.Initialize(...)` or
parameterless constructors must be updated.

## Key breaking changes

1) **Config build requires services**
   - Old: `new SnipeConfigBuilder().Build(contextId)`
   - New: `new SnipeConfigBuilder().Build(contextId, services)`

2) **Bindings require services**
   - Old: `new DeviceIdBinding()`, `new AdvertisingIdBinding()`, `new FacebookBinding()`, `new AmazonBinding()`
   - New: `new DeviceIdBinding(services)`, etc.

3) **No more static service initialization**
   - Old: `SnipeServices.Initialize(new UnitySnipeServicesFactory())`
   - New: create an `ISnipeServices` instance explicitly and pass it to config/bindings/factory.

4) **Communicator/auth construction is DI**
   - `SnipeCommunicator` and `AuthSubsystem` now require `ISnipeServices`.

## Recommended setup pattern (Unity)

Create services once (per app or per test) and pass them everywhere you build configs, contexts,
or bindings.

```csharp
var services = SnipeUnityDefaults.CreateDefaultServices();
var configBuilder = new SnipeConfigBuilder();
// configBuilder.InitializeDefault(...) or app-specific config

var factory = new SnipeApiContextFactory(snipeManager, configBuilder);
snipeManager.Initialize(factory, factory);
var context = snipeManager.GetOrCreateContext(0);

context.Auth.RegisterBinding(new DeviceIdBinding(snipeManager.Services));
context.Auth.RegisterBinding(new AdvertisingIdBinding(snipeManager.Services));
```

## Recommended setup pattern (tests)

Use `NullSnipeServices` or a custom services implementation for deterministic tests.

```csharp
var services = new NullSnipeServices();
var config = new SnipeConfigBuilder().Build(0, services);
```


### Ref-/1/Network/SnipeApiService.cs (generated API)
If you own the generator, update `SnipeApiContextFactory` to accept `ISnipeServices` and pass it to
the base constructor:

Before:
```csharp
public sealed class SnipeApiContextFactory : AbstractSnipeApiContextFactory, ISnipeContextAndTablesFactory
{
    public SnipeApiContextFactory(ISnipeManager tablesProvider, SnipeConfigBuilder configBuilder)
        : base(tablesProvider, configBuilder) { }
}
```

After:
```csharp
public sealed class SnipeApiContextFactory : AbstractSnipeApiContextFactory, ISnipeContextAndTablesFactory
{
    public SnipeApiContextFactory(ISnipeManager manager, SnipeConfigBuilder configBuilder)
        : base(manager, configBuilder, manager.Services) { }
}
```


## Other common migrations

- **Analytics:** replace `SnipeServices.Analytics` with `services.Analytics` or
  `context.Communicator.Services.Analytics`.
- **Main-thread runner / HTTP / shared prefs:** access via `ISnipeServices` instead of static.
- **UnauthorizedRequest / communicator requests:** internal creation now requires services, so
  any custom request construction in app code should pass `communicator.Services`.

## Quick checklist

- [ ] Remove `SnipeServices.Initialize(...)` from app code.
- [ ] Create `ISnipeServices` once (Unity defaults or custom).
- [ ] Pass `services` to `SnipeConfigBuilder.Build(...)`.
- [ ] Pass `services` to bindings (`DeviceIdBinding`, `AmazonBinding`, etc.).
- [ ] Update custom context factories to accept `ISnipeServices`.
- [ ] Replace any remaining static service usage with `ISnipeServices`.
