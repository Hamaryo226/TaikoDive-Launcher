[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SourceDirectory,

    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')]
    [string] $Version,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $Revision,

    [Parameter(Mandatory)]
    [string] $PackageKey,

    [string] $KeyId = '2026-01',
    [string] $PublicRepository = 'Hamaryo226/TaikoDive-Launcher',

    [Parameter(Mandatory)]
    [string] $OutputDirectory,

    [string] $SevenZipPath
)

$ErrorActionPreference = 'Stop'
$sourceRoot = (Resolve-Path -LiteralPath $SourceDirectory).Path
if ([string]::IsNullOrWhiteSpace($PackageKey) -or $PackageKey.Length -lt 24) {
    throw 'PackageKeyは24文字以上にしてください。'
}
if ($KeyId -notmatch '^[A-Za-z0-9._-]{1,32}$') {
    throw 'KeyIdは英数字、ピリオド、アンダースコア、ハイフンの1～32文字にしてください。'
}

if ([string]::IsNullOrWhiteSpace($SevenZipPath)) {
    $sevenZipCommand = Get-Command 7z.exe -ErrorAction SilentlyContinue
    if ($null -ne $sevenZipCommand) {
        $SevenZipPath = $sevenZipCommand.Source
    }
    else {
        foreach ($programFilesRoot in @($env:ProgramW6432, $env:ProgramFiles)) {
            if ([string]::IsNullOrWhiteSpace($programFilesRoot)) { continue }
            $candidate = Join-Path $programFilesRoot '7-Zip\7z.exe'
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $SevenZipPath = $candidate
                break
            }
        }
    }
}
if ([string]::IsNullOrWhiteSpace($SevenZipPath) -or -not (Test-Path -LiteralPath $SevenZipPath -PathType Leaf)) {
    throw 'AES-256 ZIPの作成に7-Zipが必要です。7z.exeをPATHへ追加するか-SevenZipPathで指定してください。'
}

$revisionLower = $Revision.ToLowerInvariant()
$packageName = "TaikoDive_Assets_v${Version}_win-x64_$($revisionLower.Substring(0, 7)).zip"
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$packagePath = Join-Path $outputRoot $packageName
$manifestPath = Join-Path $outputRoot 'assets-update-manifest.json'
if (Test-Path -LiteralPath $packagePath) {
    throw "出力先に同名パッケージがすでにあります: $packagePath"
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("TaikoDiveAssetsUpdate-" + [Guid]::NewGuid().ToString('N'))
$payloadRoot = Join-Path $temporaryRoot 'payload'
$payloadPath = Join-Path $temporaryRoot 'payload.bin'
[System.IO.Directory]::CreateDirectory($payloadRoot) | Out-Null

$protectedFiles = @(
    'Setting.json',
    'Info/User.ini',
    'TaikoDive.Launcher.exe',
    'Log.txt'
)
$protectedPrefixes = @(
    'Info/ScoreData/',
    'Info/TaikoDiveLauncher/',
    'Replay/',
    'Replays/',
    'Screenshot/',
    'Screenshots/',
    'Log/'
)

try {
    $packageFiles = [System.Collections.Generic.List[object]]::new()
    foreach ($sourceFile in Get-ChildItem -LiteralPath $sourceRoot -File -Recurse) {
        if (($sourceFile.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "再解析ポイントはパッケージに含められません: $($sourceFile.FullName)"
        }

        $relativePath = [System.IO.Path]::GetRelativePath($sourceRoot, $sourceFile.FullName).Replace('\', '/')
        $isProtected = $protectedFiles -icontains $relativePath
        if (-not $isProtected) {
            foreach ($prefix in $protectedPrefixes) {
                if ($relativePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
                    $isProtected = $true
                    break
                }
            }
        }
        if ($isProtected -or $relativePath.EndsWith('.launcher.bak', [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $destination = Join-Path $payloadRoot $relativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($destination)) | Out-Null
        [System.IO.File]::Copy($sourceFile.FullName, $destination, $false)
        $packageFiles.Add([ordered]@{
            path = $relativePath
            sha256 = (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash
            size = $sourceFile.Length
        })
    }

    if ($packageFiles.Count -eq 0) { throw '更新対象ファイルがありません。' }
    $internalManifest = [ordered]@{
        version = $Version
        revision = $revisionLower
        files = @($packageFiles | Sort-Object { $_.path })
    }
    [System.IO.File]::WriteAllText(
        (Join-Path $payloadRoot 'package-files.json'),
        ($internalManifest | ConvertTo-Json -Depth 5) + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))

    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $payloadRoot,
        $payloadPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)

    $hmac = [System.Security.Cryptography.HMACSHA256]::new([System.Text.Encoding]::UTF8.GetBytes($PackageKey))
    try {
        $passwordBytes = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($packageName))
        $password = [Convert]::ToBase64String($passwordBytes)
    }
    finally {
        $hmac.Dispose()
    }

    & $SevenZipPath a -tzip -mx=9 -mem=AES256 "-p$password" -bd -bso0 -bsp0 -- $packagePath $payloadPath
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
        throw "7-Zipによる暗号化に失敗しました（exit $LASTEXITCODE）。"
    }

    $packageFile = Get-Item -LiteralPath $packagePath
    $externalManifest = [ordered]@{
        version = $Version
        revision = $revisionLower
        packageFileName = $packageName
        packageUrl = "https://github.com/$PublicRepository/releases/download/assets-stable/$packageName"
        sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
        size = $packageFile.Length
        publishedAt = [DateTimeOffset]::UtcNow.ToString('O')
        archive = [ordered]@{
            format = 'zip'
            encryption = 'winzip-aes-256'
            payload = 'payload.bin'
            keyId = $KeyId
        }
    }
    [System.IO.File]::WriteAllText(
        $manifestPath,
        ($externalManifest | ConvertTo-Json -Depth 5) + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))

    [pscustomobject]@{
        PackagePath = $packagePath
        ManifestPath = $manifestPath
        Version = $Version
        Revision = $revisionLower
        FileCount = $packageFiles.Count
        Size = $packageFile.Length
    }
}
catch {
    if (Test-Path -LiteralPath $packagePath) { Remove-Item -LiteralPath $packagePath -Force }
    throw
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
