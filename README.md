# TaikoDive Launcher

TaikoDive のユーザーデータと起動構成を管理する、Windows ネイティブの WinUI 3 ランチャーです。

## 主な機能

- `build/Info/User.ini` の最大9ユーザーを閲覧・編集
- 名前、称号、キャラクター種別、ネームプレート種別の管理
- 選択キャラクターの `Common_NormalLoop` アニメーション表示
- `Anime.aup2` 対応ネームプレートのアニメーションプレビュー
- ユーザーごとの保存済みスコア／ベストリプレイ件数の表示
- ホームで1Pの保存済みどんちゃん・ネームプレートと、最近遊んだ楽曲（`Info/ScoreData` の `Score.dat` 更新日時順）を表示
- `build/Setting.json` の画面、音量、サウンド、メモリ関連設定の編集
- 1P／2Pのキーボードキー、USBコントローラーのボタン／方向入力の割り当て
- `build/Songs` のジャンル自動読込、楽曲ZIPのドラッグ＆ドロップ追加、ジャンル内のゲーム表示順変更
- 任意のSongsフォルダーへの切替、TaikoNauts自動検索、TaikoDive標準Songsへの復元
- `build` を作業ディレクトリにした正しい TaikoDive 起動
- ゲーム起動後にランチャーを終了して常駐RAMを削減
- ウィンドウ位置・サイズ・最大化状態の保存と安全な復元
- 起動時のバックグラウンド更新確認と、mainブランチ最新版へのアプリ内アップデート
- AES-256暗号化パッケージからのTaikoDive本体／アセット更新（ユーザーデータ保護・失敗時ロールバック）
- ランチャー設定の「ファイルの整合性」で、配信中の本体／アセットの不足・内容の違いと3Dモデルの参照先を確認し、最新版の再適用で修復

保存時は未知の設定項目や `User.ini` のコメントを残し、直前のファイルを `*.launcher.bak` にバックアップします。TaikoDive の実行中は競合を避けるため保存しません。

Songsの切替はTaikoDive側へディレクトリリンクを作成し、元のSongsを `build/Info/TaikoDiveLauncher/Songs.original` に保持します。切替先には選曲画面に必要な `box.def`、`CenterText.apt`、`Image` 内の不足ファイルだけを補完し、既存ファイルや楽曲は上書きしません。リンク作成後に「最近遊んだ曲」などのTaikoDive用ジャンルが追加された場合も、楽曲ページを開くと不足アセットを再補完します。TaikoNauts検索はデスクトップ、ドキュメント、ダウンロード、Program Filesを対象にし、複数見つかった場合は使用先を選択できます。

楽曲ページでジャンルを選ぶと、そのジャンル内のTJA譜面をドラッグまたは上下ボタンで並べ替えられます。順序はジャンル直下の `.taikodive-order` へ保存し、既存ファイルを更新する場合は `.taikodive-order.launcher.bak` を残します。譜面や楽曲フォルダーの名前は変更しません。

## 追加機能

楽曲の試聴・検査、コーデ保存／共有、プレイ記録、OBS・Discord連携、ホーム配置、セットアップ、更新プレビュー・復帰、容量整理・診断の操作は[追加機能ガイド](docs/launcher-toolkit.md)を参照してください。

## キーボードの割り当て

入力設定でプレイヤーを選び、キーボード図のキーを直接クリックすると、その場で「カッ（左）／ドン（左）／ドン（右）／カッ（右）／解除」を選べます。打面を先に選ぶ必要はありません。同じプレイヤー内では、選んだキーだけを新しい打面へ移します。他のキーやコントローラー設定は保持します。

- 図はTabで入り、矢印で移動、Enter／Spaceで編集します。メニューはEscで閉じられます。
- ゲームパッド・太鼓型コントローラーも、図のボタンをクリックして同じメニューから割り当て・解除できます。接続機器を選ぶと、その機器のボタン番号・方向入力を表示します。未接続の保存済み機器も編集できます。
- コントローラーのボタン名は Windows の `RawGameController.GetButtonLabel` で取得します。名前を取得できないボタンは入力番号を模式的な位置に表示します。画像の機種と実機のボタン配置が異なる場合は、表示された入力番号で確認してください。
- 他のプレイヤーが使っているキーは上書きせず、プレイヤーを切り替えて編集します。
- メニューを閉じるだけでは変更しません。変更は「保存」で反映し、「保存済みに戻す」で破棄できます。

## 今後の作業

次回に予定している入力設定の画像サイズ調整と、ホームバナー・ランチャー外観の拡張は [次回の開発内容](docs/next-steps.md) に記録しています。

## 技術構成

- C# / .NET 10
- WinUI 3 / Windows App SDK 2.4
- x64 / アンパッケージ・自己完結型の単一EXE発行
- 追加のUI・MVVM依存なし
- ダーク／ホワイトテーマとレスポンシブレイアウト
- Mica背景とダーク系のホーム、各ページ上部のページ移動ボタン、ホバーで浮くショートカット、ページ切替スライド（Windowsのアニメーション効果設定に追従）

## 開発

前提: Windows 10 1809 以降、.NET 10 SDK、Developer Mode。

```powershell
dotnet restore TaikoDiveLauncher.csproj
dotnet build TaikoDiveLauncher.csproj -c Debug -p:Platform=x64
$env:TAIKODIVE_LAUNCHER_DEV_DIRECTORY = "C:\path\to\TaikoDive\build"
dotnet run --project TaikoDiveLauncher.csproj -c Debug -p:Platform=x64
dotnet build TaikoDiveLauncher.csproj -c Release -p:Platform=x64
```

Releaseビルド後、`bin/x64/Release/net10.0-windows10.0.26100.0/win-x64` には配布用の `TaikoDive.Launcher.exe` だけが生成されます。このEXEを `TaikoDive.exe` と同じフォルダーへ配置してください。通常ビルドに必要な展開済みDLL群は `obj/release-bin` 側へ分離され、配布フォルダーには含まれません。ランチャーは自分自身の配置先だけをゲームフォルダーとして使用し、フォルダー選択は行いません。自己完結型なので実行環境へ .NET 10 Desktop Runtime や Windows App Runtimeを別途導入する必要はありません。開発時だけ `TAIKODIVE_LAUNCHER_DEV_DIRECTORY` でゲームフォルダーを指定できます。ランチャー固有設定は `%LOCALAPPDATA%/TaikoDiveLauncher/launcher.json` に保存されます。

このリポジトリはprivateで、配布物はCloudflare R2（`https://dl.hamaryo.dev`）にだけ置きます。`main`へpushされるとGitHub Actionsが`launcher/`へ単一EXEとSHA-256付きマニフェストを更新します。ランチャーは認証情報を使わずこのURLを確認し、ダウンロードしたEXEのサイズとSHA-256が一致した場合だけ自己更新します。

ゲーム本体の更新は、privateのTaikoDiveリポジトリでビルドした`TaikoDive_Update_v{major}.{minor}.{patch}_win-x64_{commit7}.zip`をR2の`game/`へ配置します。ランチャーは外側のAES-256 ZIP、外部・内部マニフェスト、全ファイルのサイズとSHA-256、保護対象パスを検証してから適用します。構築方法と必要なGitHub Secretsは[ゲーム更新の配布手順](docs/game-update-distribution.md)を参照してください。

Asset更新は、privateの`TaikoDive-Assets`にある`src/`の中身を同じ方式で暗号化し、R2の`assets/`へ配置します。`src`相対パスをゲームの`build`相対パスとして適用し、`Texture`と`Sound`はリポジトリにないファイルを削除します。`Songs`と`Info`は比較・削除せず、`Info/User.ini`などのユーザーデータを保持します。詳細は[Asset更新の配布手順](docs/assets-update-distribution.md)を参照してください。R2の初期設定は[Cloudflare R2の設定手順](docs/cloudflare-r2-setup.md)にまとめています。
