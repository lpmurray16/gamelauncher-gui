#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory = $true)][string]$ProgramPath,
    [ValidateRange(1024, 65535)][int]$Port = 5180,
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$program = [IO.Path]::GetFullPath($ProgramPath)
if ([IO.Path]::GetFileName($program) -ine 'GameLauncher.exe' -or $program.StartsWith('\\')) {
    throw 'Specify the full local path to GameLauncher.exe.'
}
if (-not $Remove -and -not (Test-Path -LiteralPath $program -PathType Leaf)) {
    throw 'Build or install GameLauncher.exe first, then pass its exact path.'
}
$sha = [Security.Cryptography.SHA256]::Create()
try { $hash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($program.ToLowerInvariant()))).Replace('-', '').Substring(0, 16) }
finally { $sha.Dispose() }
$name = "GameLauncher.Companion.$hash.$Port"
$group = 'GameLauncher Companion (explicit opt-in)'
$existing = Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue
if ($existing -and $existing.Group -ne $group) { throw 'An unrelated firewall rule has the same name; no changes made.' }

if ($Remove) {
    if ($existing -and $PSCmdlet.ShouldProcess($name, 'Remove companion firewall rule')) {
        $existing | Remove-NetFirewallRule
        if (Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue) { throw 'Firewall rule removal was not retained.' }
        Write-Host 'Companion firewall rule removed.'
    }
    return
}
if ($PSCmdlet.ShouldProcess("$program TCP $Port, Private profile, LocalSubnet only", 'Create or replace companion firewall rule')) {
    if ($existing) { $existing | Remove-NetFirewallRule }
    New-NetFirewallRule -Name $name -DisplayName "Game Launcher companion (TCP $Port)" -Group $group `
        -Direction Inbound -Action Allow -Enabled True -Profile Private -Program $program `
        -Protocol TCP -LocalPort $Port -RemoteAddress LocalSubnet -EdgeTraversalPolicy Block | Out-Null
    $rule = Get-NetFirewallRule -Name $name
    $application = $rule | Get-NetFirewallApplicationFilter
    $ports = $rule | Get-NetFirewallPortFilter
    $addresses = $rule | Get-NetFirewallAddressFilter
    if ($rule.Profile -ne 'Private' -or $rule.Direction -ne 'Inbound' -or $rule.Action -ne 'Allow' -or
        $application.Program -ine $program -or [string]$ports.LocalPort -ne [string]$Port -or
        [string]$addresses.RemoteAddress -ne 'LocalSubnet') {
        throw 'Firewall rule verification failed. Review the rule in Windows Firewall.'
    }
    Write-Host "Verified private-subnet rule: $name"
    Write-Host 'Other existing firewall rules are unchanged. Remove any broader GameLauncher rules separately.'
    Write-Host 'Re-run with -Remove and the same ProgramPath/Port before uninstalling or moving this executable.'
}
