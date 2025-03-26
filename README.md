# Vistanz's Non Dsetructive Extensions

[日本語版](README.JA.md)

This is a set of non-destructive tools built on top of [NDMF](https://ndmf.nadena.dev/), aims to provide features that doesn't exists in packages provided by others.

## Currently Available Tools

### Humanoid Avatar Rebaker

This is a tool that will rebakes the [humanoid avatar](https://docs.unity3d.com/2022.3/Documentation/Manual/AvatarCreationandSetup.html) object, it has following features:

- **Adjust viewpoint** - Adjust the view point in VRChat avatar descriptor after rebaking the humanoid avatar object.
- **Floor adjustment** - Contains several modes to adjust height.
  - **Disabled** - Does nothing
  - **Bare feet snap to ground** - Recommended by FBT users who want precise IK measurements, which make sures bare feet is on the ground.
    - **Renderer With Bare Feet** - Will use the mesh assigned here to measure the bare feet offset, will instead use the feet bone if empty.
  - **Ensure soles on ground** - This will pull your avatar upward and make sures your shoes will not stucked into the ground. Will affects IK measurements which make your hands looks shorter in your perspective when using "avatar height" as measurement.
  - **Ensure soles on ground and avoid hovering** - Almost same as above, but it will also try the best to avoid hovering feet issues.
- **Fix bone orientation** - Attempts to normalizes (fixes) bones orientation, potentially fixes bugged/broken mecanim animations/IKs.
- **Fix crosslegs** - Attempts to adjusts legs bone default (rest) pose, fixes the cross legs issue when crouching in VR.
- **Fix hover feet** - It will also snaps the feet to the ground if it is hovering when enabled with auto calculate foot offset.
- **Manual offset** - Allow to manually adjust the offset of the hip bone yourself.
- **Human Description** - This is an advanced usage. You can adjusts values for use with Unity's built-in IK system.
- **Override Bones** - This is also an advanced usage. You can specify other bones and its rotation limits here.

> [!IMPORTANT]
> As this tool will rebakes the humanoid avatar object, so any adjustments (include poses) to your avatar skeleton will be applied to it. It is important that your avatar must be in T-Pose before build.

> [!IMPORTANT]
> Beware height adjustment will changes the IK measurements of your avatar. If you choose to enable IK 2.0 and measure with "**avatar height**", the arms will appear shorter in your perspective.
> Suggestion for users who need precise IK with FBT (dancers, for example), you may either:
> 1. Shouldn't use this tool to auto detect or adjust (pull up) feet height.
> 2. If you uses it anyway, you may want to switch to traditional "**arm span**" as measurement.  
> [Details on IK 2.0](https://docs.vrchat.com/docs/ik-20-features-and-options)

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