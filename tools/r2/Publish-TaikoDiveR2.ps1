[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('launcher', 'game', 'assets')]
    [string] $Channel,
    [Parameter(Mandatory)]
    [string] $PayloadPath,
    [Parameter(Mandatory)]
    [string] $ManifestPath,
    [string] $Bucket = 'taikodive',
    [switch] $DryRun
)
$ErrorActionPreference = 'Stop'
# Shared policy with maintenance; Python 3.11+ and AWS CLI v2 must be on PATH.
$arguments = @((Join-Path $PSScriptRoot 'r2_manager.py'), '--channel', $Channel,
    '--bucket', $Bucket, '--payload', $PayloadPath, '--manifest', $ManifestPath)
if (-not $DryRun) { $arguments += '--apply' }
& python @arguments
if ($LASTEXITCODE -ne 0) { throw "R2 publication failed (exit $LASTEXITCODE)." }
