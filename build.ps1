$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $PSScriptRoot 'MaxwellBalance.exe'
& $compiler /nologo /target:winexe /platform:x64 "/out:$exe" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'MaxwellBalance.cs')
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$test = Start-Process -FilePath $exe -ArgumentList '--test' -WindowStyle Hidden -PassThru -Wait
if ($test.ExitCode -ne 0) { throw 'Tests failed' }
Get-Content (Join-Path $PSScriptRoot 'test-result.txt')
