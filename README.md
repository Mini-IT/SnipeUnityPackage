# Snipe Unity Package


## Installation guide

<ul>
<li> Install <a href="https://developers.facebook.com/docs/unity/">Facebook SDK for Unity</a> (direct <a href="https://origincache.facebook.com/developers/resources/?id=FacebookSDK-current.zip">download link</a>)
<li> Install <a href="https://github.com/playgameservices/play-games-plugin-for-unity">Google Play Games for Unity</a>
<li> Install <a href="https://github.com/googlesamples/unity-jar-resolver/blob/master/external-dependency-manager-latest.unitypackage">External Dependency Manager for Unity</a>.(Actually it is already should be installed because it is included in the previous packages)<br />
Allow it to add external package manager registries. (It's optional but it helps you to stay updated)
<li> <a href="https://docs.unity3d.com/Manual/upm-ui-giturl.html">Add</a> Snipe package (this repository) to Unity's Package Manager - https://github.com/Mini-IT/SnipeUnityPackage.git
<li> After package import is done in Unity editor "Snipe" menu should appear. Click "Snipe/Initialize Assembly Definitions" menu item
</ul>

## Updating

Unity Package Manager doesn't support auto updates for git-based packages. But there are some ways to initiate an update manually.
<ul>
<li> You may add the same package again using git URL. Package manager will update an existing one.
<li> Alternatively you may manually edit your project's Packages/manifest.json. Just remove "com.miniit.snipe.client" inside "lock" section.
</ul>

## Third-party libraries used

<ul>
<li> Ionic.Zlib
<li> [websocket-sharp](https://github.com/sta/websocket-sharp)
<li> WebSocket.jslib - for WebGL build target  (not fully supported yet)
<li> [MPack](https://github.com/caesay/MPack)
</ul>
