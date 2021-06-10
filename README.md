# Snipe Unity Package


## Installation guide

<ul>
<li> Install <a href="https://developers.facebook.com/docs/unity/">Facebook SDK for Unity</a> (direct <a href="https://origincache.facebook.com/developers/resources/?id=FacebookSDK-current.zip">download link</a>)
<li> Install <a href="https://github.com/googlesamples/unity-jar-resolver/blob/master/external-dependency-manager-latest.unitypackage">External Dependency Manager for Unity</a>. Actually it is already should be installed because it is included in the previous package.
<li> <a href="https://docs.unity3d.com/Manual/upm-ui-giturl.html">Add</a> <b>Snipe Tools</b> package to Unity Package Manager - https://github.com/Mini-IT/SnipeToolsUnityPackage.git <br />
After package import is done in Unity editor "Snipe" menu should appear.
<li> Click <b>"Snipe/Install Snipe Package"</b> menu item
</ul>

## Updating

<p>
Unity Package Manager doesn't support auto updates for git-based packages. We recommend to use <a href="https://github.com/mob-sakai/UpmGitExtension">UPM Git Extension</a>.
</p><p>
Alternatively there are some other methods:
</p>
<ul>
<li> You may add the same package again using git URL. Package manager will update an existing one.
<li> Or you may manually edit your project's Packages/packages-lock.json. Just remove "com.miniit.snipe.client" section.
</ul>

## Third-party libraries used

<ul>
<li> <a href="https://github.com/sta/websocket-sharp">websocket-sharp</a>
<li> <a href="https://github.com/gwiazdorrr/BetterStreamingAssets">BetterStreamingAssets</a>
<li> <a href="https://github.com/mgholam/fastJSON">fastJSON</a> - modified for IL2CPP compatibility
</ul>
