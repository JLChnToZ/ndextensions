# Vistanz's Non Dsetructive Extensions

[日本語版](README.JA.md)

This is a set of non-destructive tools built on top of [NDMF](https://ndmf.nadena.dev/), aims to provide features that doesn't exists in packages provided by others.

## Currently Available Tools

### Humanoid Avatar Rebaker

This is a tool that will rebakes the [humanoid avatar](https://docs.unity3d.com/2022.3/Documentation/Manual/AvatarCreationandSetup.html) object, it has following features:

- **Adjust viewpoint** - Adjust the view point in VRChat avatar descriptor after rebaking the humanoid avatar object.
- **Fix bone orientation** - Attempts to normalizes (fixes) bones orientation, potentially fixes bugged/broken mecanim animations/IKs.
- **Fix crosslegs** - Attempts to adjusts legs bone default (rest) pose, fixes the cross legs issue when crouching in VR.
- **Auto calculate foot offset** - Useful for clothing that has different heels to the original avatar and stucking in floor when not adjusting it.
  What it will do is detects the skinned mesh that has vertices forming the sole under shoes, calculates the lowest point of them and pull the hips bone upward by this value.
- **Manual offset** - Allow to manually adjust the offset of the hip bone yourself.
- **Override** - This is an advanced usage, you can specify other bones as part of humanoid, might be useful if you have renamed/recreated relavent bones etc.

> [!IMPORTANT]
> As this tool will rebakes the humanoid avatar object, so any adjustments (include poses) to your avatar skeleton will be applied to it. It is important that your avatar must be in T-Pose before build.

> [!IMPORTANT]
> Disclaimer: these "fix" features are not perfect, please double check if it works for you. Uncheck the toggle and do not use if it breaks your other gimmicks.

To use this tool, just add this component (JLChnToZ > Non-Destructive Extensions > Rebake Humanoid Avatar) to the same level (root) of your VRChat avatar descriptor, toggle the options you want and it is all set.

### Others

There will be other tools available when needed/requested. So stay tuned.

## Installation

0. If you haven't install NDMF or any other tools based on it yet (For example, [Modular Avatar](https://modular-avatar.nadena.dev/), [Avatar Optimizer](https://vpm.anatawa12.com/avatar-optimizer/), etc.), please add their package listings ([`https://vpm.nadena.dev/vpm.json`](vcc://vpm/addRepo?url=https://vpm.nadena.dev/vpm.json)) to your [VCC](https://vcc.docs.vrchat.com/), or [ALCOM](https://vrc-get.anatawa12.com/alcom/), [vrc-get](https://github.com/vrc-get/vrc-get) etc.
1. If you haven't install any of my packages before, please add [my package listings](https://xtlcdn.github.io/vpm/) ([`https://xtlcdn.github.io/vpm/index.json`](vcc://vpm/addRepo?url=https://xtlcdn.github.io/vpm/index.json)) as well.
2. Find and add the **Vistanz's Non Destructive Extensions** package to your avatar project, assume you already working on one.

## License

[MIT](LICENSE)