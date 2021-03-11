# Snipe Unity Package


## Installation guide

<ul>
<li> Install <a href="https://developers.facebook.com/docs/unity/">Facebook SDK for Unity</a> (direct <a href="https://origincache.facebook.com/developers/resources/?id=FacebookSDK-current.zip">download link</a>) <br />
Don't install "Examples" directory or remove it if it is already installed.
<li> Install <a href="https://github.com/playgameservices/play-games-plugin-for-unity">Google Play Games for Unity</a>
<li> Install <a href="https://github.com/googlesamples/unity-jar-resolver/blob/master/external-dependency-manager-latest.unitypackage">External Dependency Manager for Unity</a>. Actually it is already should be installed because it is included in the previous packages.<br />
Allow it to add external package manager registries. (It's optional but it helps you to stay updated)
<li> <a href="https://docs.unity3d.com/Manual/upm-ui-giturl.html">Add</a> <b>Snipe Tools</b> package to Unity Package Manager - https://github.com/Mini-IT/SnipeToolsUnityPackage.git <br />
After package import is done in Unity editor "Snipe" menu should appear.
<li> Click <b>"Snipe/Initialize Assembly Definitions"</b> menu item. MiniIT.GooglePlay.XXXX.asmdef files should appear in GooglePlayGames and GooglePlayGames/Editor directories.
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
<li> Ionic.Zlib
<li> <a href="https://github.com/sta/websocket-sharp">websocket-sharp</a>
<li> WebSocket.jslib - for WebGL build target  (not fully supported yet)
</ul>
