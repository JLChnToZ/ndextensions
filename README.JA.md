# Vistanz の非破壊拡張機能 (Vistanz's Non Dsetructive Extensions)

> このReadme文書はAIで翻訳されており、不正確な場合があります。

これは、[NDMF](https://ndmf.nadena.dev/) 上に構築された非破壊ツールのセットであり、他のパッケージには存在しない機能を提供することを目指しています。

## 現在利用可能なツール

### ヒューマノイドアバター再ベイクツール

これは、[ヒューマノイドアバター](https://docs.unity3d.com/2022.3/Documentation/Manual/AvatarCreationandSetup.html) オブジェクトを再ベイクするツールで、以下の機能があります:

- **Adjust viewpoint**  
  人型アバターオブジェクトをリベイクした後、VRChat Avatar Descriptor でビューポイントを調整する。
- **Fix bone orientation**  
  ボーンの向きを正規化（修正）しようとするもので、mecanim のアニメーションやIKのバグや破損を修正する可能性があります。
- **Fix crosslegs**  
  脚のボーンのデフォルト（休息）ポーズを調整し、VR でしゃがんだときに脚が交差する問題を修正。
- **Auto calculate foot offset**  
  元のアバターと異なるヒールを持つ服で、調整しないときに床にはまってしまう場合に便利です。
  これは、靴底を形成する頂点を持つスキンメッシュを検出し、その頂点の最低点を計算し、その値だけ腰のボーンを上に引っ張ります。
- **Fix hover feet**
  また、足のオフセットの自動計算を有効にすると、ホバリングしている場合、足が地面にスナップします。
- **Manual offset**  
  腰骨のオフセットを自分で手動で調整できるようにする。
- **Human Description**
  これは高度な使い方です。
  UnityのビルトインIKシステムで使用するための値を調整できます。
- **Override Bones**  
  これも高度な使い方です。
  ここで他のボーンとその回転制限を指定できます。

> [!IMPORTANT]  
> このツールはヒューマノイドアバターオブジェクトを再ベイクするため、アバターのスケルトンに対するすべての調整（ポーズを含む）が適用されます。ビルド前にアバターが Tポーズになっていることが重要です。

> [!IMPORTANT]  
> 免責事項: これらの「修正」機能は完璧ではないため、ご自身で動作確認を行ってください。問題がある場合は、トグルをオフにし、他の機能に支障をきたす場合は使用しないでください。

このツールを使用するには、VRChat アバター記述子の同じ階層（ルート）に、コンポーネント (JLChnToZ > Non-Destructive Extensions > Rebake Humanoid Avatar) を追加し、希望するオプションのトグルを切り替えるだけです。

### その他

今後、必要に応じて他のツールも提供される予定です。お楽しみに。

## インストール方法

0. まだ [NDMF](https://ndmf.nadena.dev/) やそれに基づく他のツール（例: [Modular Avatar](https://modular-avatar.nadena.dev/)、[Avatar Optimizer](https://vpm.anatawa12.com/avatar-optimizer/) など）をインストールしていない場合は、これらのパッケージリスト ([`https://vpm.nadena.dev/vpm.json`](vcc://vpm/addRepo?url=https://vpm.nadena.dev/vpm.json)) をお使いの [VCC](https://vcc.docs.vrchat.com/)、[ALCOM](https://vrc-get.anatawa12.com/alcom/) や [vrc-get](https://github.com/vrc-get/vrc-get) などに追加してください。  
1. まだ私のパッケージをインストールしていない場合は、[私のパッケージリスト](https://xtlcdn.github.io/vpm/) ([`https://xtlcdn.github.io/vpm/index.json`](vcc://vpm/addRepo?url=https://xtlcdn.github.io/vpm/index.json)) を追加してください。  
2. ご自身のアバタープロジェクトに **Vistanz's Non Dsetructive Extensions** パッケージを追加してください。（既にアバタープロジェクトを作成している前提です。）

## ライセンス

[MIT](LICENSE)