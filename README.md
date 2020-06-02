# Snipe Unity Package


## Installation guide

<ul>
<li> Download and install <a href="https://github.com/googlesamples/unity-jar-resolver/blob/master/external-dependency-manager-latest.unitypackage">External Dependency Manager for Unity</a>.<br />
Allow it to add external package manager registries. (It's optional but it helps you to stay updated)
<li> <a href="https://docs.unity3d.com/Manual/upm-ui-giturl.html">Add</a> Snipe package (this repository) to Unity's Package Manager - https://github.com/Mini-IT/SnipeUnityPackage.git
</ul>

## Updating

Unity Package Manager doesn't support auto updates for git-based packages. But there are some ways to initiate an update manually.
<ul>
<li> You may add the same package again using git URL. Package manager will update an existing one.
<li> Alternatively you may manually edit your project's Packages/manifest.json. Just remove "com.miniit.snipe.client" inside "lock" section.
</ul>
