# Snipe Unity Package


## Installation guide

* Install [External Dependency Manager for Unity](https://github.com/googlesamples/unity-jar-resolver)
* [Add](https://docs.unity3d.com/Manual/upm-ui-giturl.html) [fastJSON](https://github.com/Mini-IT/fastJSON-unity-package) package
* [Add](https://docs.unity3d.com/Manual/upm-ui-giturl.html) [unity-logger](https://github.com/Mini-IT/unity-logger) package
* [Add](https://docs.unity3d.com/Manual/upm-ui-giturl.html) <b>Snipe Client Tools</b> package to Unity Package Manager - https://github.com/Mini-IT/SnipeToolsUnityPackage.git <br />
After package import is done in Unity editor `Snipe` menu should appear.
* Click <b>`Snipe/Install Snipe Package`</b> menu item

### Install managed DLLs from NuGet
The dependency managed DLL are not included to avoid possible duplication. You need to add them to the project manually. You can extract the needed dlls from NuGet packages (either [manually](https://stackoverflow.com/a/61187711) or using a tool like [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity))
* [System.Buffers](https://www.nuget.org/packages/System.Buffers/4.5.1)
* [System.Memory](https://www.nuget.org/packages/System.Memory/4.5.5)
* [Microsoft.Extensions.Logging.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/7.0.1)

## Updating

Unity Package Manager doesn't support auto updates for git-based packages. That is why Snipe Client Tools comes with its own Updater (<b>`Snipe/Updater`</b> menu item).

Alternatively there are some other methods:
* You may use [UPM Git Extension](https://github.com/mob-sakai/UpmGitExtension).
* You may add the same package again using git URL. Package manager will update an existing one.
* Or you may manually edit your project's `Packages/packages-lock.json`. Just remove `"com.miniit.snipe.client"` section.

## DI-friendly setup

The package exposes services as interfaces to make them pluggable and testable.

- Default Unity wiring: `SnipeUnityDefaults.CreateDefaultServices()`
- Null/test-friendly services: `NullSnipeServices`

## Migration notes

- Prefer constructors that accept `ISnipeServices`.
- `SnipeOptionsBuilder.Build(int, ISnipeServices)` should be used instead of the old `Build(int)`.
- For context factories, pass an explicit `ISnipeServices` (e.g. via `SnipeUnityDefaults.CreateDefaultServices()`).
- For tests, use `NullSnipeServices` or provide custom implementations via its constructor.

## Third-party libraries used

* [fastJSON](https://github.com/mgholam/fastJSON) - modified for IL2CPP compatibility
* KcpClient inspired by implementation from [Mirror](https://github.com/vis2k/Mirror)
