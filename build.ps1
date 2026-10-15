param(
    [string]$BricsCADDir = '',
    [ValidateSet(24,25,26)][int]$CadVersion = 24,
    [switch]$TestsOnly,
    [string]$OutputDirectory = 'bin\v1.4',
    [switch]$BuildCadTests
)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$binPath = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $projectRoot $OutputDirectory }
New-Item -ItemType Directory -Force -Path $binPath | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$corePath = Join-Path $projectRoot 'src\Core.cs'
$alignmentPath = Join-Path $projectRoot 'src\Alignment.cs'
$editingPath = Join-Path $projectRoot 'src\AlignmentEditing.cs'
$testPath = Join-Path $projectRoot 'tests\CoreTests.cs'
$testExe = Join-Path $binPath 'CoreTests.exe'
if (Test-Path -LiteralPath $compiler) {
    & $compiler /nologo /target:exe /r:System.Core.dll "/out:$testExe" $corePath $alignmentPath $editingPath $testPath (Join-Path $projectRoot 'tests\AlignmentTests.cs') (Join-Path $projectRoot 'tests\EditingTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Prevajanje testov ni uspelo.' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Testi niso uspeli.' }
} else { throw 'Manjka prevajalnik .NET Framework 4.8 za teste. Namesti .NET Framework 4.8 Developer Pack.' }
$uiExe = Join-Path $binPath 'UiSmoke.exe'
& $compiler /nologo /target:exe /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$uiExe" $corePath $alignmentPath $editingPath (Join-Path $projectRoot 'src\Window.cs') (Join-Path $projectRoot 'src\AlignmentEditor.cs') (Join-Path $projectRoot 'tests\UiSmoke.cs')
if ($LASTEXITCODE -ne 0) { throw 'Prevajanje testa vmesnika ni uspelo.' }
Push-Location $projectRoot
try {
    & $uiExe $binPath
    if ($LASTEXITCODE -ne 0) { throw 'Test vmesnika ni uspel.' }
} finally { Pop-Location }
if ($TestsOnly) { exit 0 }
if (!$BricsCADDir) {
    $candidates = Get-ChildItem -LiteralPath 'C:\Program Files\Bricsys' -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "BricsCAD V$CadVersion*" }
    $BricsCADDir = ($candidates | Select-Object -First 1).FullName
}
if (!$BricsCADDir) { throw 'Navedi -BricsCADDir z mapo svoje namestitve BricsCAD.' }
foreach ($dll in @('BrxMgd.dll','TD_Mgd.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $BricsCADDir $dll))) { throw "Manjka $dll v $BricsCADDir" }
}
if ($CadVersion -eq 26) {
    & dotnet build (Join-Path $projectRoot 'Vozisce.csproj') -c Release "-p:CadVersion=26" "-p:BricsCADDir=$BricsCADDir" -o $binPath
} else {
    $sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
    & $compiler /nologo /target:library /platform:x64 /optimize+ /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "/r:$BricsCADDir\BrxMgd.dll" "/r:$BricsCADDir\TD_Mgd.dll" "/out:$binPath\Vozisce.dll" $sources
}
if ($LASTEXITCODE -ne 0) { throw 'Prevajanje dodatka ni uspelo.' }
if ($BuildCadTests) {
    if ($CadVersion -eq 26) { throw 'CAD testna knjižnica trenutno podpira le V24/V25.' }
    & $compiler /nologo /target:library /platform:x64 /r:System.Core.dll "/r:$BricsCADDir\BrxMgd.dll" "/r:$BricsCADDir\TD_Mgd.dll" "/r:$binPath\Vozisce.dll" "/out:$binPath\CadSmoke.dll" (Join-Path $projectRoot 'tests\CadSmoke.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Prevajanje CAD testov ni uspelo.' }
}
Write-Host "NETLOAD -> $binPath\Vozisce.dll; nato VR_RAZSIRITEV"

