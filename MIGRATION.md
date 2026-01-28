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

var factory = new SnipeApiContextFactory(snipeManager, configBuilder, services);
snipeManager.Initialize(factory, factory);
var context = snipeManager.GetOrCreateContext(0);

context.Auth.RegisterBinding(new DeviceIdBinding(services));
context.Auth.RegisterBinding(new AdvertisingIdBinding(services));
```

## Recommended setup pattern (tests)

Use `NullSnipeServices` or a custom services implementation for deterministic tests.

```csharp
var services = new NullSnipeServices();
var config = new SnipeConfigBuilder().Build(0, services);
```

## Project-specific notes from Ref- samples

### Ref-/2/Network/ServerService.cs
Before:
```csharp
if (!SnipeServices.IsInitialized)
{
    SnipeServices.Initialize(new UnitySnipeServicesFactory());
}

var snipeConfigBuilder = new SnipeConfigBuilder();
// initialize config as before

var factory = new SnipeApiContextFactory(_snipe, snipeConfigBuilder);
_snipe.Initialize(factory, factory);
var snipeContext = _snipe.GetOrCreateContext(0);

snipeContext.Auth.RegisterBinding(new DeviceIdBinding());
snipeContext.Auth.RegisterBinding(new AmazonBinding());
```

After:
```csharp
var services = SnipeUnityDefaults.CreateDefaultServices();
var snipeConfigBuilder = new SnipeConfigBuilder();
// initialize config as before

var factory = new SnipeApiContextFactory(_snipe, snipeConfigBuilder, services);
_snipe.Initialize(factory, factory);
var snipeContext = _snipe.GetOrCreateContext(0);

snipeContext.Auth.RegisterBinding(new DeviceIdBinding(services));
snipeContext.Auth.RegisterBinding(new AmazonBinding(services));
```

Remove `SnipeServices.Initialize(...)` completely.

### Ref-/1/Network/Server.cs and Ref-/4/Network/Server.cs
Before:
```csharp
var snipeConfigBuilder = new SnipeConfigBuilder();
// Services.Config.InitializeSnipeConfig(snipeConfigBuilder);

var factory = new SnipeApiContextFactory(_snipe, snipeConfigBuilder);
_snipe.Initialize(factory);
_snipeContext = _snipe.GetOrCreateContext();

_snipeContext.Auth.RegisterBinding(new DeviceIdBinding());
_snipeContext.Auth.RegisterBinding(new AdvertisingIdBinding());
_snipeContext.Auth.RegisterBinding(new FacebookBinding());
```

After:
```csharp
var services = SnipeUnityDefaults.CreateDefaultServices();
var snipeConfigBuilder = new SnipeConfigBuilder();
// Services.Config.InitializeSnipeConfig(snipeConfigBuilder);

var factory = new SnipeApiContextFactory(_snipe, snipeConfigBuilder, services);
_snipe.Initialize(factory);
_snipeContext = _snipe.GetOrCreateContext();

_snipeContext.Auth.RegisterBinding(new DeviceIdBinding(services));
_snipeContext.Auth.RegisterBinding(new AdvertisingIdBinding(services));
_snipeContext.Auth.RegisterBinding(new FacebookBinding(services));
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
    public SnipeApiContextFactory(ISnipeManager tablesProvider, SnipeConfigBuilder configBuilder, ISnipeServices services)
        : base(tablesProvider, configBuilder, services) { }
}
```

If you cannot update the generator yet, you can pass `services` via the base optional parameter
in your handwritten constructor (if you keep a thin wrapper around generated code).

### Ref-/2/Network/ServerContext.cs (commented legacy)
Before:
```csharp
//SnipeServices.Initialize(new AppSnipeServicesFactory());
//_snipeContext = new SnipeApiContextFactory(_snipeConfigBuilder).CreateContext(0) as SnipeApiContext;
var factory = new SnipeApiContextFactory(_snipeConfigBuilder);
SnipeManager.Instance.Initialize(factory, factory);
```

After:
```csharp
var services = SnipeUnityDefaults.CreateDefaultServices();
var factory = new SnipeApiContextFactory(_snipeConfigBuilder, services);
SnipeManager.Instance.Initialize(factory, factory);
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
