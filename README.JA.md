# Vistanz の非破壊拡張機能 (Vistanz's Non Dsetructive Extensions)

> このReadme文書はAIで翻訳されており、不正確な場合があります。

これは、[NDMF](https://ndmf.nadena.dev/) 上に構築された非破壊ツールのセットであり、他のパッケージには存在しない機能を提供することを目指しています。

## 現在利用可能なツール

### ヒューマノイドアバター再ベイクツール

これは、[ヒューマノイドアバター](https://docs.unity3d.com/2022.3/Documentation/Manual/AvatarCreationandSetup.html) オブジェクトを再ベイクするツールで、以下の機能があります:

- **Adjust viewpoint**  
  ヒューマノイドアバターの再ベイク後、VRChat アバター記述子内の視点を調整します。
- **Fix bone orientation**  
  ボーンの向きを正規化（修正）し、バグのある／動作不良の Mecanim アニメーションや IK を改善する可能性があります。
- **Fix crosslegs**  
  脚のボーンのデフォルト（レスト）ポーズを調整し、VR でしゃがむ際のクロスレッグ問題を修正します。
- **Auto calculate foot offset**  
  オリジナルアバターとは異なるヒールを持つ衣服で足が床に食い込む場合に有用です。  
  靴の下に存在するソールを形成する頂点を持つスキンメッシュを検出し、その中で最も低い点を計算、ヒップボーンをその値分だけ上方に引き上げます。
- **Manual offset**  
  ヒップボーンのオフセットを手動で調整できるようにします。
- **Override**  
  高度な使用方法です。ヒューマノイドの一部として他のボーンを指定でき、ボーンの名称変更や再作成を行った場合に有用です。

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