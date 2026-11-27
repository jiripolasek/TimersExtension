[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-PublishingFixture.ps1')

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$testRoot = Join-Path $tempRoot "Local Deployment-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $testRoot | Out-Null

function Remove-GeneratedDirectory([string] $Path, [string] $Root) {
    if ($Path -ne $deploymentPath -or $Root -ne $wapOutputPath) { throw 'Unexpected rollback target.' }
    Remove-Item -LiteralPath (Join-Path $Path 'AppxManifest.xml')
    Remove-Item -LiteralPath $Path
}

function Add-AppxPackage([string] $Register, [switch] $ForceApplicationShutdown, $ErrorAction) {
    $layout = Get-Content -LiteralPath $Register -Raw
    if ($layout -eq 'replacement') { throw 'Fixture registration failed.' }
    $state.Registered += $layout
}

function Import-Module([string] $Name, $ErrorAction) {
    if ($Name -ne 'Appx') { throw "Unexpected module '$Name'." }
}

function Get-AppxPackage([string] $Name, [string] $Publisher) {
    if ($Name -ne $config.IdentityName -or $Publisher -ne $config.Publisher) {
        throw 'Package lookup must use the configured name and publisher.'
    }
    if (-not $state.Removed) { [pscustomobject]@{ PackageFullName = 'Fixture-package' } }
}

function Remove-AppxPackage([string] $Package, $ErrorAction) {
    $state.Removed = $Package
}

try {
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        (Join-Path $PSScriptRoot 'Deploy-Package.ps1'), [ref] $null, [ref] $null)
    $transaction = $ast.Find({ param($node)
        $node -is [System.Management.Automation.Language.TryStatementAst] -and
        $node.Extent.Text.Contains('[IO.Directory]::Move($stagingPath, $deploymentPath)')
    }, $true)
    $install = [scriptblock]::Create($transaction.Extent.Text)

    foreach ($case in @('backup-move-fails', 'registration-fails')) {
        $wapOutputPath = Join-Path $testRoot $case
        $deploymentPath = Join-Path $wapOutputPath 'AppX'
        $stagingPath = Join-Path $wapOutputPath 'AppX.staging'
        $backupPath = Join-Path $wapOutputPath 'AppX.backup'
        $previousInstallLocation = $deploymentPath
        New-Item -ItemType Directory -Path $deploymentPath, $stagingPath -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $deploymentPath 'AppxManifest.xml'), 'previous')
        [IO.File]::WriteAllText((Join-Path $stagingPath 'AppxManifest.xml'), 'replacement')
        if ($case -eq 'backup-move-fails') { [IO.File]::WriteAllText($backupPath, 'obstacle') }

        $state = @{ Registered = @() }
        $layoutBackupCreated = $false
        $stagingMoved = $false
        $failed = $false
        try { & $install }
        catch { $failed = $true }
        if (-not $failed -or ($state.Registered -join ',') -ne 'previous' -or
            (Get-Content -LiteralPath (Join-Path $deploymentPath 'AppxManifest.xml') -Raw) -ne 'previous') {
            throw "Deployment rollback lost the previous layout or registration in '$case'."
        }
    }

    $configPath = New-TestWorkflowRepository -Directory $testRoot
    $config = & (Join-Path $PSScriptRoot 'Get-WorkflowConfig.ps1') -ConfigPath $configPath
    $state = @{ Removed = $null }
    & (Join-Path $PSScriptRoot 'Uninstall-Package.ps1') -ConfigPath $configPath
    if ($state.Removed -ne 'Fixture-package') { throw 'Expected the package with the configured publisher to be removed.' }
    Write-Output 'Passed local deployment rollback and package identity checks.'
}
finally {
    $resolved = (Resolve-Path -LiteralPath $testRoot).ProviderPath
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove test data outside '$tempRoot'."
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
