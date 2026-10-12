# TaikoDive Asset更新の配布

`TaikoDive-Assets`はprivateのまま維持し、`src/`から生成した暗号化パッケージだけを公開側の`TaikoDive-Launcher` Releaseへ配置します。ランチャーにGitHubトークンは持たせません。

## 適用ルール

- `src/`自体は含めず、その中の相対パスをTaikoDiveの`build/`直下へ適用する
- 例: `src/Texture/Title.png` → `build/Texture/Title.png`
- `src/Info/Chara/`以下は配布して上書きする。`src/Info/Chara3D.json`、`src/Info/User.ini`、`Setting.json`、スコア、リプレイ、スクリーンショット、ログ、ランチャー本体はパッケージから除外する
- Assetリポジトリが管理する`src/Songs/`内の公式ファイルは`build/Songs/`へ適用するが、それ以外の既存曲ファイルは削除しない
- 更新時は内部マニフェストと`build/Texture/`、`build/Sound/`を比較し、Assetリポジトリに存在しないファイルをバックアップ後に削除する
- `build/Songs/`と`build/Info/`は比較・削除の対象にしない
- 既存ファイルは更新前にバックアップし、適用途中の失敗時はロールバックする
- Asset更新のバージョンとインストール状態はゲーム本体の更新とは別に管理する

パッケージ名は`TaikoDive_Assets_v{major}.{minor}.{patch}_win-x64_{commit7}.zip`、公開Releaseタグは`assets-stable`です。外側はWinZip AES-256、内側はファイル一覧・サイズ・SHA-256を持つ通常ZIPで、本体更新と同じ`GAME_PACKAGE_KEY`を使います。

## privateリポジトリの設定

1. `docs/examples/publish-assets-update.yml`を`TaikoDive-Assets/.github/workflows/publish-assets-update.yml`へコピーする。
2. `TaikoDive-Assets`へ`GAME_PACKAGE_KEY`、`RELEASE_APP_ID`、`RELEASE_APP_PRIVATE_KEY`をSecretとして登録する。
3. 公開側のランチャーを同じ`GAME_PACKAGE_KEY`を埋め込んで先に公開する。
4. `TaikoDive-Assets`のActionsを手動実行するか、`src/**`を`main`へpushする。
5. 別のゲーム配置でAsset更新と`Info/User.ini`保持、TaikoDive起動を確認する。

リポジトリ直下の`RELEASE_NOTES.md`には、利用者へ公開してよい今回の更新内容だけを記載します。この内容は公開Releaseのマニフェストに含まれ、ランチャーのAsset更新カードへそのまま表示されます。空欄または4,000文字を超える場合は配布を停止し、privateリポジトリ内のコミット件名は自動公開しません。

更新用バージョンはActionsのrun numberから`0.<run_number>.0`として生成します。

## 整合性チェック

ランチャー設定の「ファイルの整合性」から、本体とAssetのファイルを確認できます。

- 更新の適用時に、パッケージのファイル一覧（パス・サイズ・SHA-256）をランチャーの更新状態 `installed.json` に保存する。チェックはこの一覧とローカルの `build/` を比較し、ネットワークを使わない
- 一覧を保存していない環境（この機能より前の版で適用した環境など）では、最新パッケージをダウンロードして比較する。ダウンロードしたパッケージは比較後に削除する
- 不足しているファイルと、サイズまたはSHA-256が一致しないファイルを報告する。一覧に無い追加ファイルは報告しない
- `Info/Chara3D.json` の `modelsPath` が未設定、またはその先に `animations.glb` が無い場合も報告する
- 「修復」は問題のあったチャンネルの最新版を通常の更新と同じ手順（バックアップ・ロールバック付き）で適用し直し、`modelsPath` を配置済みのモデルフォルダーに設定し直す。モデルフォルダー自体が無い場合はAssetも適用し直す。修復後は自動でもう一度チェックする
