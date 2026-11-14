[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('launcher', 'game', 'assets')]
    [string] $Channel,
    [string] $Bucket = 'taikodive',
    [switch] $Apply
)
$ErrorActionPreference = 'Stop'
# Python 3.11+ and AWS CLI v2 must be on PATH. Secrets stay in environment variables.
$arguments = @((Join-Path $PSScriptRoot 'r2_manager.py'), '--channel', $Channel, '--bucket', $Bucket)
if ($Apply) { $arguments += '--apply' }
& python @arguments
if ($LASTEXITCODE -ne 0) { throw "R2 maintenance failed (exit $LASTEXITCODE)." }
