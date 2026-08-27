# TaikoDive Asset更新の配布

`TaikoDive-Assets`はprivateのまま維持し、`src/`から生成した暗号化パッケージだけを公開側の`TaikoDive-Launcher` Releaseへ配置します。ランチャーにGitHubトークンは持たせません。

## 適用ルール

- `src/`自体は含めず、その中の相対パスをTaikoDiveの`build/`直下へ適用する
- 例: `src/Texture/Title.png` → `build/Texture/Title.png`
- `src/Info/Chara/`以下、`src/Info/User.ini`、`Setting.json`、スコア、リプレイ、スクリーンショット、ログ、ランチャー本体はパッケージから除外する
- Assetリポジトリが管理する`src/Songs/`内の公式ファイルは`build/Songs/`へ適用するが、それ以外の既存曲ファイルは削除しない
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
