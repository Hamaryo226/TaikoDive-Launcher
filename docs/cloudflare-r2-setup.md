# Cloudflare R2の設定手順

ランチャー、TaikoDive本体、Assetの更新は、Cloudflare R2のバケット`taikodive`を`https://dl.hamaryo.dev`で公開して配布します。R2のStandard無料枠は10 GB-month/月・Class A 100万回/月・Class B 1,000万回/月で、転送量は無料です。保存料金は日ごとのピーク容量の月平均で計算され、無料枠はアカウント内で共有されます。単に古いファイルを消すだけでは操作回数の超過は防げません。ランチャーは読み取りだけを行い、書き込み用の鍵はGitHub Actionsだけが持ちます。

| URL | 中身 | アップロード元 |
|---|---|---|
| `https://dl.hamaryo.dev/launcher/` | `TaikoDive.Launcher.exe`、`update-manifest.json` | このリポジトリの`publish-main-update.yml` |
| `https://dl.hamaryo.dev/game/` | ゲーム更新パッケージ、`game-update-manifest.json` | privateのTaikoDiveリポジトリ |
| `https://dl.hamaryo.dev/assets/` | Asset更新パッケージ、`assets-update-manifest.json` | privateの`TaikoDive-Assets` |

以下の画面の名前は2026年時点のCloudflareダッシュボードを想定している。表記が変わっている場合は近い名前の項目を選ぶ。

## 1. バケットを作る

1. Cloudflareのダッシュボードで「R2 Object Storage」を開く。初回は支払い方法の登録を求められるが、無料枠の範囲では請求されない。
2. 「バケットを作成する」で名前を`taikodive`にして作成する。ロケーションは自動のままでよい。

## 2. 独自ドメインで公開する

1. 作成した`taikodive`バケットの「設定」タブを開く。
2. 「カスタム ドメイン」の「追加」で`dl.hamaryo.dev`を入力し、表示されるDNSレコードの追加を確定する。
3. 状態が「アクティブ」になるまで待つ（数分）。
4. 同じ画面の「r2.dev サブドメイン」は無効のままにする。

## 3. 書き込み用のAPIトークンを作る

1. R2の概要ページ右側の「API」から「APIトークンを管理」を開き、「APIトークンを作成する」を選ぶ。
2. 権限は「オブジェクト読み取りと書き込み」、対象は「特定のバケットのみ」で`taikodive`だけを選ぶ。
3. 作成後に表示される次の3つを控える。画面を閉じると「シークレット アクセス キー」は二度と表示されない。
   - アカウントID（エンドポイントURL `https://<アカウントID>.r2.cloudflarestorage.com` の`<アカウントID>`部分）
   - アクセス キー ID
   - シークレット アクセス キー

## 4. GitHubへ登録する

`TaikoDive-Launcher`、privateのTaikoDiveリポジトリ、`TaikoDive-Assets`の3つそれぞれで、「Settings」→「Secrets and variables」→「Actions」→「New repository secret」から次の3つを登録する。VariablesタブやCodespaces・Dependabot用には登録しない。

| Name | Secret |
|---|---|
| `R2_ACCOUNT_ID` | アカウントID |
| `R2_ACCESS_KEY_ID` | アクセス キー ID |
| `R2_SECRET_ACCESS_KEY` | シークレット アクセス キー |

## 5. 配布と自動容量管理

初回のR2移行は完了しています。旧移行ワークフローは古いマニフェストで現行配布を上書きするため退役しています。再実行しないでください。[自動容量管理](r2-storage-management.md)の定期ジョブと公開前ガードを使用します。

「Publish main launcher update」を「Run workflow」で実行するか`main`へpushすると、ランチャー本体がR2の`launcher/`へ公開される。

## 確認

ブラウザーで次のURLを開き、JSONが表示されれば配布できている。

- `https://dl.hamaryo.dev/launcher/update-manifest.json`
- `https://dl.hamaryo.dev/game/game-update-manifest.json`
- `https://dl.hamaryo.dev/assets/assets-update-manifest.json`
