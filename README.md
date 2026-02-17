# Snipe Unity Package


## Installation guide

* Install [External Dependency Manager for Unity](https://github.com/googlesamples/unity-jar-resolver)
* [Add](https://docs.unity3d.com/Manual/upm-ui-giturl.html) [fastJSON](https://github.com/Mini-IT/fastJSON-unity-package) package
* [Add](https://docs.unity3d.com/Manual/upm-ui-giturl.html) [unity-logger](https://github.com/Mini-IT/unity-logger) package
* [Add](https://docs.unity3d.com/Manual/upm-ui-giturl.html) <b>Snipe Client Tools</b> package to Unity Package Manager - https://github.com/Mini-IT/SnipeToolsUnityPackage.git <br />
After package import is done in Unity editor `Snipe` menu should appear.
* Click <b>`Snipe -> Install Snipe Package`</b> menu item

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
    ProjectID = "YOUR_PROJECT_ID",
    ClientKey = "YOUR_PROJECT_CLIENT_KEY",
    Mode = devMode ? SnipeProjectMode.Dev : SnipeProjectMode.Live,
};

builder.Initialize(snipeProjectInfo, snipeConfigData);

var contextFactory = new SnipeApiContextFactory(_snipe, builder);
var tablesFactory = new SnipeApiTablesFactory(_snipe.Services, builder);

_snipe.Initialize(contextFactory, tablesFactory);

var snipeContext = _snipe.GetOrCreateContext(0);

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


## Third-party libraries used

* [fastJSON](https://github.com/mgholam/fastJSON) - modified for IL2CPP compatibility
* KcpClient inspired by implementation from [Mirror](https://github.com/vis2k/Mirror)
