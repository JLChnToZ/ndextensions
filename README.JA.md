# Vistanz の非破壊拡張機能 (Vistanz's Non Dsetructive Extensions)

> このReadme文書はAIで翻訳されており、不正確な場合があります。

これは、[NDMF](https://ndmf.nadena.dev/) 上に構築された非破壊ツールのセットであり、他のパッケージには存在しない機能を提供することを目指しています。

## 現在利用可能なツール

### ヒューマノイドアバター再ベイクツール

これは、[ヒューマノイドアバター](https://docs.unity3d.com/2022.3/Documentation/Manual/AvatarCreationandSetup.html) オブジェクトを再ベイクするツールで、以下の機能があります:

- **Adjust Viewpoint** (視点の自動調整)  
  人型アバターオブジェクトをリベイクした後、VRChat Avatar Descriptor でビューポイントを調整する。
- **Floor Adjustment** (床調整)  
  床を調整するための複数のモードを含む。  
  - **Disabled** (無効)  
    何もしない
  - **Bare feet snap to ground** (素足を地面につける)  
    正確な IK 測定が必要な FBT ユーザーにお勧め。これは素足が地面にあることを確認します。
    - **Renderer With Bare Feet** (素足のレンダラー)  
      素足のオフセットを測定するために、ここで割り当てられたメッシュを使用します。空の場合は、代わりに足のボーンを使用します。
  - **Ensure soles on ground** (靴底を地面につける)  
    これはアバターを上に引っ張り、靴が地面に突き刺さらないようにします。
    これは IK 測定に影響し、「アバターの高さ」を測定として使用する場合、あなたの視点からは手が短く見えるようになります。
  - **Ensure soles on ground and avoid hovering** (靴底を地面につけ、ホバリングしないようにする)  
    上記とほぼ同じですが、足のホバリングの問題を避けるために最善を尽くします。
- **Manual offset** (手動オフセット)  
  腰骨のオフセットを自分で手動で調整できるようにする。
- **Fix bone orientation** (骨の向きを修正)  
  ボーンの向きを正規化（修正）しようとするもので、Mecanim のアニメーションや IK のバグや破損を修正する可能性があります。
- **Fix crosslegs** (脚が交差する問題を修正)  
  脚のボーンのデフォルト（休息）ポーズを調整し、VR でしゃがんだときに脚が交差する問題を修正。
- **Human Description**  
  これは高度な使い方です。
  UnityのビルトインIKシステムで使用するための値を調整できます。
- **Override Bones**  
  これも高度な使い方です。
  ここで他のボーンとその回転制限を指定できます。

> [!IMPORTANT]  
> このツールはヒューマノイドアバターオブジェクトを再ベイクするため、アバターのスケルトンに対するすべての調整（ポーズを含む）が適用されます。ビルド前にアバターが Tポーズになっていることが重要です。

> [!IMPORTANT]
> 高さを調整すると、アバターの IK 測定値が変わるので注意してください。IK 2.0 を有効にして「**アバターの高さ**」を測定すると、あなたの視点では腕が短く見えます。
> FBT で正確な IK を必要とするユーザー（ダンサーなど）への提案です：
> 1. 足の高さの自動検出や調整（引き上げ）に、このツールを使用しない。
> 2. もし、このツールを使うのであれば、従来の「**アームスパン**」測定に切り替えた方がよいでしょう。  
> [IK 2.0 の詳細](https://docs.vrchat.com/docs/ik-20-features-and-options)

> [!IMPORTANT]  
> 免責事項: これらの「修正」機能は完璧ではないため、ご自身で動作確認を行ってください。問題がある場合は、トグルをオフにし、他の機能に支障をきたす場合は使用しないでください。

このツールを使用するには、VRChat アバター記述子の同じ階層（ルート）に、コンポーネント (JLChnToZ > Non-Destructive Extensions > Rebake Humanoid Avatar) を追加し、希望するオプションのトグルを切り替えるだけです。

### パラメーター値フィルター

これは、[Animated Animator Parameters (AAP)](https://vrc.school/docs/Other/AAPs) の技術を使用してパラメーター値をフィルタリングするツールです。指数平滑化による滑らかな処理が可能で、他の範囲へのリマッピングも可能です。このツールを使用すれば、ブレンドツリーを自分で作成する必要がありません。

このツールを使用するには、アバター階層下の任意のゲームオブジェクトにこのコンポーネント（JLChnToZ > Non-Destructive Extensions > Parameter Value Filter）を追加するだけです。

### パラメータコンプレッサー

これは、パラメータスロットを交互に置き換えることで同期パラメータを削減するコンポーネントです。削減したいパラメータ数に応じて、最大3パラメータで10ビット、最大7パラメータで11ビット、最大15パラメータで12ビットなど、使用量を圧縮できます。他の製作者による類似ツール（例えば、NDMFツールとの互換性が低いものなど）とは異なり、圧縮対象となるパラメータを選択可能です。他のNDMFベースのツールで作成されたパラメータもサポートされています。

このツールを使用するには、アバター階層下の任意のゲームオブジェクトにこのコンポーネント（JLChnToZ > Non-Destructive Extensions > Parameter Compressor）を追加するだけです。

### その他

今後、必要に応じて他のツールも提供される予定です。お楽しみに。

## インストール方法

0. まだ [NDMF](https://ndmf.nadena.dev/) やそれに基づく他のツール（例: [Modular Avatar](https://modular-avatar.nadena.dev/)、[Avatar Optimizer](https://vpm.anatawa12.com/avatar-optimizer/) など）をインストールしていない場合は、これらのパッケージリスト ([`https://vpm.nadena.dev/vpm.json`](vcc://vpm/addRepo?url=https://vpm.nadena.dev/vpm.json)) をお使いの [VCC](https://vcc.docs.vrchat.com/)、[ALCOM](https://vrc-get.anatawa12.com/alcom/) や [vrc-get](https://github.com/vrc-get/vrc-get) などに追加してください。  
1. まだ私のパッケージをインストールしていない場合は、[私のパッケージリスト](https://xtlcdn.github.io/vpm/) ([`https://xtlcdn.github.io/vpm/index.json`](vcc://vpm/addRepo?url=https://xtlcdn.github.io/vpm/index.json)) を追加してください。  
2. ご自身のアバタープロジェクトに **Vistanz's Non Dsetructive Extensions** パッケージを追加してください。（既にアバタープロジェクトを作成している前提です。）

## ライセンス

[MIT](LICENSE)