# TaikoDiveゲーム更新の配布

TaikoDiveのソースリポジトリはprivateのまま、ビルド済み更新パッケージとマニフェストだけをCloudflare R2（`https://dl.hamaryo.dev`）へ配置します。ランチャーにはGitHubトークンを持たせません。

## 固定仕様

- パッケージ名: `TaikoDive_Update_v{major}.{minor}.{patch}_win-x64_{commit7}.zip`
- 例: `TaikoDive_Update_v1.4.0_win-x64_a1b2c3d.zip`
- 配置先: `https://dl.hamaryo.dev/game/`（マニフェストは`game-update-manifest.json`）
- 外側: WinZip AES-256で暗号化したZIP
- 外側の内容: `payload.bin` 1ファイルのみ
- `payload.bin`: 通常ZIP。更新ファイルと`package-files.json`を格納
- パスワード: `Base64(HMAC-SHA256(package key, canonical package filename))`
- 鍵ID: 初期値`2026-01`。鍵を変更するときはIDも変更し、対応ランチャーを先に公開する

パスワードはランチャーから解析可能なので、これはEXEやアセットをそのまま置かないための難読化です。購入者だけへ強くアクセス制御する仕組みではありません。改ざん防止は外部・内部双方のSHA-256検証で行います。

## CIに設定するSecret

privateのTaikoDiveリポジトリへ次を登録します。

- `GAME_PACKAGE_KEY`: 32バイト以上の乱数をBase64化した値（MSBuild引数で安全に扱うため`;`を含めない）
- `RELEASE_APP_ID`: privateの`TaikoDive-Launcher`から配布ツールを読めるGitHub AppのID
- `RELEASE_APP_PRIVATE_KEY`: 上記GitHub Appの秘密鍵
- `R2_ACCOUNT_ID`・`R2_ACCESS_KEY_ID`・`R2_SECRET_ACCESS_KEY`: R2へ書き込むためのAPIトークン（[Cloudflare R2の設定手順](cloudflare-r2-setup.md)）

`TaikoDive-Launcher`リポジトリにも同じ`GAME_PACKAGE_KEY`と、R2の3つのSecretを登録します。ランチャーのReleaseビルド時だけ埋め込みます。鍵をソース、ログ、マニフェスト、Releaseへ保存しないでください。

## privateリポジトリのワークフロー例

`docs/examples/publish-game-update.yml`をprivateのTaikoDiveリポジトリの`.github/workflows/publish-game-update.yml`へコピーします。`main`へのpushを検知すると、Releaseビルド、AESパッケージ生成、公開側の固定Release更新まで自動で行います。手動再実行用に`workflow_dispatch`も残します。

リポジトリ直下の`RELEASE_NOTES.md`には、利用者へ公開してよい今回の更新内容だけを記載します。この内容は公開マニフェストに含まれ、ランチャーのTaikoDive更新カードへそのまま表示されます。空欄または4,000文字を超える場合は配布を停止します。privateリポジトリ内のコミット件名は自動公開しません。

TaikoDive本体に明示的なバージョンがないため、更新用バージョンは`0.<GitHub Actionsのrun_number>.0`で自動生成します。たとえば最初の配布は`0.1.0`、次は`0.2.0`です。同じワークフローのrun numberは単調増加し、実際のソースはマニフェストとパッケージ名に含まれる7桁のコミットIDで追跡できます。連続してpushされた場合は古い実行をキャンセルし、最新のmainを配布します。

パッケージ作成スクリプトは、`Setting.json`、`Info/User.ini`、`Songs`、スコア、リプレイ、スクリーンショット、ログ、ランチャー本体を除外します。必ずクリーンなGitHub Actions runnerの`build`から作成し、普段遊んでいるローカルの`build`を入力にしないでください。

## 公開前の順序

1. ランチャー側とprivate側へ同一の`GAME_PACKAGE_KEY`とR2のSecretを設定する。
2. ランチャーをReleaseビルドしてR2の`launcher/`へ公開する。
3. 新しいランチャーが動くことを確認する。
4. private側のゲーム更新ワークフローを実行する。
5. 別のゲーム配置で、更新、ユーザーデータ保持、ゲーム起動を確認する。

マニフェストは常に最新1件を指します。R2の無料枠に収めるため、公開スクリプトは最新と直前の版以外のパッケージを削除します。
