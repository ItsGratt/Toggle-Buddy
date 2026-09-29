$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
$compilerArgs = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output',
    ('/out:' + (Join-Path $project 'ToggleGoat.exe')),
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')),
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Xml.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Xaml.dll',
    ('/r:' + (Join-Path $framework 'WPF\WindowsBase.dll')),
    ('/r:' + (Join-Path $framework 'WPF\PresentationCore.dll')),
    ('/r:' + (Join-Path $framework 'WPF\PresentationFramework.dll')))
foreach ($state in @('idle', 'num', 'caps', 'both')) {
    $compilerArgs += '/resource:' + (Join-Path $project ('Artwork\' + $state + '.png')) + ',ToggleGoat.' + $state + '.png'
}
$compilerArgs += Join-Path $PSScriptRoot 'ToggleGoat.cs'
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output ('Built ' + (Join-Path $project 'ToggleGoat.exe'))
