<#
PowerShell helper to stop node, remove locked esbuild binary or node_modules, clear npm cache and reinstall dependencies.
Run from the extension folder (powershell):
  cd src\Velo.Extension
  .\scripts\clean-install.ps1

If you still get EPERM for esbuild.exe, run PowerShell as Administrator and re-run the script.
#>

$ErrorActionPreference = 'Stop'

if ($PSScriptRoot -and (Test-Path $PSScriptRoot)) {
    $root = $PSScriptRoot
} else {
    $root = Split-Path -Parent $MyInvocation.MyCommand.Definition
}
Set-Location $root

Write-Host "[velo] running clean-install in $root"

Write-Host "[velo] stopping node processes..."
Get-Process -Name node -ErrorAction SilentlyContinue | ForEach-Object {
    try { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue; Write-Host "Stopped node pid=$($_.Id)" } catch {}
}

$esbuild = Join-Path $root 'node_modules\@esbuild\win32-x64\esbuild.exe'
if (Test-Path $esbuild) {
    Write-Host "[velo] removing locked esbuild binary: $esbuild"
    try {
        Remove-Item $esbuild -Force -ErrorAction Stop
        Write-Host "[velo] removed esbuild.exe"
    } catch {
        Write-Warning "[velo] failed to remove esbuild.exe: $($_.Exception.Message)"
    }
}

if (Test-Path (Join-Path $root 'node_modules')) {
    Write-Host "[velo] removing node_modules folder"
    try { Remove-Item -Recurse -Force .\node_modules -ErrorAction Stop; Write-Host "[velo] node_modules removed" } catch { Write-Warning "[velo] failed to remove node_modules: $($_.Exception.Message)" }
}

Write-Host "[velo] cleaning npm cache"
npm cache clean --force

Write-Host "[velo] running npm ci"
npm ci

Write-Host "[velo] install complete. Run 'npm run build' or 'npx ng build' to build the extension."
