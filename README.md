# Snipe Unity Package

[Snipe](https://snipe.dev/) client

## Installation

Use [Snipe Installer](https://github.com/Mini-IT/unity-snipe-installer)

## Updating

Unity Package Manager doesn't support auto updates for git-based packages. That is why Snipe Client Tools comes with its own Updater (<b>`Snipe -> Updater`</b> menu item).

Alternatively there are some other methods:
* You may use [UPM Git Extension](https://github.com/mob-sakai/UpmGitExtension).
* You may add the same package again using git URL. Package manager will update an existing one.
* Or you may manually edit your project's `Packages/packages-lock.json`. Just remove `"com.miniit.snipe.client"` section.

## Quick start

Setup the project in the server editor. Get the API key.
* Click <b>`Snipe -> Download SnipeApi ...`</b> menu item.

Enter the API key, specify a directory and download the `SnipeApiService.cs`

DI registration
```cs
using MiniIT.Snipe;
using MiniIT.Snipe.Unity;

builder.RegisterSingleton<ISnipeManager>(c => new SnipeManager(new UnitySnipeServicesFactory()));
```

Configure `SnipeOptions` with the keys you get in the server editor.
Note that `ProjectID` should be specified **without** ending (e.g. without `_dev` or `_live`)

```cs
private readonly ISnipeManager _snipe;

var builder = new SnipeOptionsBuilder();

var snipeProjectInfo = new SnipeProjectInfo()
{
    ProjectID = "YOUR_PROJECT_ID",  // Without `_dev` or `_live` ending
    ClientKey = "YOUR_PROJECT_CLIENT_KEY",
    Mode = devMode ? SnipeProjectMode.Dev : SnipeProjectMode.Live,
};

builder.Initialize(snipeProjectInfo, snipeConfigData);

var contextFactory = new SnipeApiContextFactory(_snipe, builder);
var tablesFactory = new SnipeApiTablesFactory(_snipe.Services, builder);

_snipe.Initialize(contextFactory, tablesFactory);

var snipeContext = _snipe.GetOrCreateContext();

snipeContext.Auth.RegisterDefaultBindings();
snipeContext.Auth.LoginSucceeded += OnLoginSucceeded;
snipeContext.Communicator.ConnectionClosed += OnConnectionClosed;

await _snipe.GetTables().Load();

snipeContext.Communicator.Start();

private void OnLoginSucceeded(int userId)
{
    Debug.Log("OnLoginSucceeded. userId: " + userId);
}

private void OnConnectionClosed()
{
    Debug.Log("OnConnectionClosed");
}
```


## Android restore credentials

Install `com.miniit.android.credentials` to enable the optional
`MiniIT.Snipe.Integrations.AndroidCredentials` assembly. Register the binding after
`RegisterDefaultBindings()` and before starting the communicator:

```csharp
#if UNITY_ANDROID && !AMAZON_STORE
var restoreBinding = snipeContext.Auth.RegisterBinding(
    new AndroidRestoreCredentialsBinding(snipeContext.Auth.Services));
#endif
```

Configure `google.restoreKey.certFingerprints` with the application's SHA-256 signing
certificate fingerprints on the Snipe server. See the
[Google module documentation](https://docs.snipe.dev/docs/server/google).

The binding runs after the normal login. If a restore credential is found, it transfers the
current `dvid` and uses `ConnectAndRelogin` to enter the restored account. This produces two
`LoginSucceeded` events: one for the temporary account and one for the restored account.
Account-dependent game services must handle that change.

After registration succeeds, the binding keeps the account marker in Android's
`noBackupFilesDir` and skips repeated key creation for that account. Native operations run
only on Android outside the Editor and Amazon builds.

Before signing out, switching accounts, or changing the Snipe project or Dev/Live cluster,
clear the credential while the binding is still alive:

```csharp
bool cleared = await restoreBinding.ClearAsync();
```

Check `cleared` before completing the account change. Ordinary disconnects keep the credential.
Android stores one restore credential per application; use one active Snipe context.

## Third-party libraries used

* [fastJSON](https://github.com/mgholam/fastJSON) - modified for IL2CPP compatibility
* KcpClient inspired by implementation from [Mirror](https://github.com/vis2k/Mirror)
