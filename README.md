# TaikoDive Launcher

TaikoDive のユーザーデータと起動構成を管理する、Windows ネイティブの WinUI 3 ランチャーです。

## 主な機能

- `build/Info/User.ini` の最大9ユーザーを閲覧・編集
- 名前、称号、キャラクター種別、ネームプレート種別の管理
- 選択キャラクターの `Common_NormalLoop` アニメーション表示
- `Anime.aup2` 対応ネームプレートのアニメーションプレビュー
- ユーザーごとの保存済みスコア／ベストリプレイ件数の表示
- `build/Setting.json` の画面、音量、サウンド、メモリ関連設定の編集
- 1P／2Pのキーボードキー、USBコントローラーのボタン／方向入力の割り当て
- `build/Songs` のジャンル自動読込と楽曲ZIPのドラッグ＆ドロップ追加
- 任意のSongsフォルダーへの切替、TaikoNauts自動検索、TaikoDive標準Songsへの復元
- `build` を作業ディレクトリにした正しい TaikoDive 起動
- ゲーム起動後にランチャーを終了して常駐RAMを削減
- ウィンドウ位置・サイズ・最大化状態の保存と安全な復元
- 起動時のバックグラウンド更新確認と、mainブランチ最新版へのアプリ内アップデート
- AES-256暗号化パッケージからのTaikoDive本体／アセット更新（ユーザーデータ保護・失敗時ロールバック）

保存時は未知の設定項目や `User.ini` のコメントを残し、直前のファイルを `*.launcher.bak` にバックアップします。TaikoDive の実行中は競合を避けるため保存しません。

Songsの切替はTaikoDive側へディレクトリリンクを作成し、元のSongsを `build/Info/TaikoDiveLauncher/Songs.original` に保持します。切替先には選曲画面に必要な `box.def`、`CenterText.apt`、`Image` 内の不足ファイルだけを補完し、既存ファイルや楽曲は上書きしません。リンク作成後に「最近遊んだ曲」などのTaikoDive用ジャンルが追加された場合も、楽曲ページを開くと不足アセットを再補完します。TaikoNauts検索はデスクトップ、ドキュメント、ダウンロード、Program Filesを対象にし、複数見つかった場合は使用先を選択できます。

## 技術構成

- C# / .NET 10
- WinUI 3 / Windows App SDK 2.4
- x64 / アンパッケージ・自己完結型の単一EXE発行
- 追加のUI・MVVM依存なし
- ダーク／ホワイトテーマとレスポンシブレイアウト

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

`main`へpushされるとGitHub Actionsが固定タグ`launcher-main`の公開Releaseへ単一EXEとSHA-256付きマニフェストを更新します。ランチャーは認証情報を使わずこの公開Releaseを確認し、ダウンロードしたEXEのサイズとSHA-256が一致した場合だけ自己更新します。

ゲーム本体の更新は、privateのTaikoDiveリポジトリでビルドした`TaikoDive_Update_v{major}.{minor}.{patch}_win-x64_{commit7}.zip`を公開Releaseの固定タグ`game-stable`へ配置します。ランチャーは外側のAES-256 ZIP、外部・内部マニフェスト、全ファイルのサイズとSHA-256、保護対象パスを検証してから適用します。構築方法と必要なGitHub Secretsは[ゲーム更新の配布手順](docs/game-update-distribution.md)を参照してください。

Asset更新は、privateの`TaikoDive-Assets`にある`src/`の中身を同じ方式で暗号化し、公開Releaseの固定タグ`assets-stable`へ配置します。`src`相対パスをゲームの`build`相対パスとして適用し、`Info/User.ini`などのユーザーデータは保持します。詳細は[Asset更新の配布手順](docs/assets-update-distribution.md)を参照してください。
