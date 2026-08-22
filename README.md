# TaikoDive Launcher

TaikoDive のユーザーデータと起動構成を管理する、Windows ネイティブの WinUI 3 ランチャーです。

## 主な機能

- `build/Info/User.ini` の最大9ユーザーを閲覧・編集
- 名前、称号、キャラクター種別、ネームプレート種別の管理
- 選択キャラクターの `Common_NormalLoop` アニメーション表示
- `Anime.aup2` 対応ネームプレートのアニメーションプレビュー
- ユーザーごとの保存済みスコア／ベストリプレイ件数の表示
- `build/Setting.json` の画面、音量、サウンド、メモリ関連設定の編集
- `build` を作業ディレクトリにした正しい TaikoDive 起動
- ゲーム起動後にランチャーを終了して常駐RAMを削減

保存時は未知の設定項目や `User.ini` のコメントを残し、直前のファイルを `*.launcher.bak` にバックアップします。TaikoDive の実行中は競合を避けるため保存しません。

## 技術構成

- C# / .NET 10
- WinUI 3 / Windows App SDK 2.4
- x64 / アンパッケージ・フレームワーク依存
- 追加のUI・MVVM依存なし
- ダーク／ホワイトテーマとレスポンシブレイアウト

## 開発

前提: Windows 10 1809 以降、.NET 10 SDK、Developer Mode。

```powershell
dotnet restore TaikoDiveLauncher.csproj
dotnet build TaikoDiveLauncher.csproj -c Debug -p:Platform=x64
$env:TAIKODIVE_LAUNCHER_DEV_DIRECTORY = "C:\path\to\TaikoDive\build"
dotnet run --project TaikoDiveLauncher.csproj -c Debug -p:Platform=x64
dotnet publish TaikoDiveLauncher.csproj -c Release -p:Platform=x64
```

Release publish フォルダーの内容を `TaikoDive.exe` と同じフォルダーへ配置します。ランチャーは自分自身の配置先だけをゲームフォルダーとして使用し、フォルダー選択は行いません。実行環境には .NET 10 Desktop Runtime と Windows App Runtime が必要です。開発時だけ `TAIKODIVE_LAUNCHER_DEV_DIRECTORY` でゲームフォルダーを指定できます。ランチャー固有設定は `%LOCALAPPDATA%/TaikoDiveLauncher/launcher.json` に保存されます。
