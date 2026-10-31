# Cloudflare R2の設定手順

ランチャー、TaikoDive本体、Assetの更新は、Cloudflare R2のバケット`taikodive`を`https://dl.hamaryo.dev`で公開して配布します。R2は転送量が無料で、保存容量は10GBまで無料枠に入ります。ランチャーは読み取りだけを行い、書き込み用の鍵はGitHub Actionsだけが持ちます。

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

## 5. 配布中の版をR2へ移す

`TaikoDive-Launcher`の「Actions」→「Migrate published updates to Cloudflare R2」→「Run workflow」を一度だけ実行する。GitHub Releasesにある最新のゲーム・Assetパッケージを、版を変えずにR2へコピーする。

その後、「Publish main launcher update」を「Run workflow」で実行するか`main`へpushすると、ランチャー本体がR2の`launcher/`へ公開される。

## 確認

ブラウザーで次のURLを開き、JSONが表示されれば配布できている。

- `https://dl.hamaryo.dev/launcher/update-manifest.json`
- `https://dl.hamaryo.dev/game/game-update-manifest.json`
- `https://dl.hamaryo.dev/assets/assets-update-manifest.json`
