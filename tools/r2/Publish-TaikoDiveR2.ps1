[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('launcher', 'game', 'assets')]
    [string] $Channel,

    # マニフェストより先にアップロードするファイル（ランチャーEXEや更新パッケージ）。
    [Parameter(Mandatory)]
    [string] $PayloadPath,

    [Parameter(Mandatory)]
    [string] $ManifestPath,

    [string] $Bucket = 'taikodive'
)

$ErrorActionPreference = 'Stop'

foreach ($name in 'R2_ACCOUNT_ID', 'R2_ACCESS_KEY_ID', 'R2_SECRET_ACCESS_KEY') {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "$name secret is required."
    }
}

$env:AWS_ACCESS_KEY_ID = $env:R2_ACCESS_KEY_ID
$env:AWS_SECRET_ACCESS_KEY = $env:R2_SECRET_ACCESS_KEY
$env:AWS_DEFAULT_REGION = 'auto'
# AWS CLI 2.23以降の既定のチェックサム送信はR2で失敗することがあるため、必要なときだけにする。
$env:AWS_REQUEST_CHECKSUM_CALCULATION = 'when_required'
$env:AWS_RESPONSE_CHECKSUM_VALIDATION = 'when_required'
$endpoint = "https://$($env:R2_ACCOUNT_ID).r2.cloudflarestorage.com"

function Invoke-Aws {
    & aws @args --endpoint-url $endpoint --only-show-errors
    if ($LASTEXITCODE -ne 0) {
        throw "aws $($args[0]) $($args[1]) failed with exit code $LASTEXITCODE."
    }
}

$payload = Get-Item -LiteralPath $PayloadPath
$manifest = Get-Item -LiteralPath $ManifestPath
$payloadKey = "$Channel/$($payload.Name)"

# 更新パッケージ名はバージョンとコミットを含み中身が変わらないので長期キャッシュできる。
# ランチャーEXEとマニフェストは同じ名前で上書きするため、CDNに古い版を残さない。
$payloadCacheControl = if ($Channel -eq 'launcher') { 'no-cache' } else { 'public, max-age=31536000, immutable' }
$payloadContentType = if ($payload.Extension -eq '.exe') { 'application/vnd.microsoft.portable-executable' } else { 'application/zip' }

# マニフェストが存在しないファイルを指す時間を作らないよう、本体を先に置く。
Invoke-Aws s3 cp $payload.FullName "s3://$Bucket/$payloadKey" `
    --cache-control $payloadCacheControl `
    --content-type $payloadContentType
Invoke-Aws s3 cp $manifest.FullName "s3://$Bucket/$Channel/$($manifest.Name)" `
    --cache-control 'no-cache' `
    --content-type 'application/json; charset=utf-8'

if ($Channel -ne 'launcher') {
    # 無料枠の容量に収めるため古いパッケージを消す。直前の版は、古いマニフェストを読んだ
    # ランチャーがダウンロード中かもしれないので1つだけ残す。
    $listing = & aws s3api list-objects-v2 --bucket $Bucket --prefix "$Channel/" --endpoint-url $endpoint --output json
    if ($LASTEXITCODE -ne 0) {
        throw "aws s3api list-objects-v2 failed with exit code $LASTEXITCODE."
    }
    $objects = @(($listing | ConvertFrom-Json).Contents)
    $stale = @($objects |
        Where-Object { $_.Key -like '*.zip' -and $_.Key -ne $payloadKey } |
        Sort-Object { [DateTimeOffset]::Parse($_.LastModified) } -Descending |
        Select-Object -Skip 1)
    foreach ($object in $stale) {
        Invoke-Aws s3 rm "s3://$Bucket/$($object.Key)"
    }
}
