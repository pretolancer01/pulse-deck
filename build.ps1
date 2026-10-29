param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$localSdk = Join-Path $PSScriptRoot 'tools/dotnet/dotnet.exe'
$sdk = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$project = Join-Path $PSScriptRoot 'src/PulseDeck/PulseDeck.csproj'
$output = Join-Path $PSScriptRoot 'dist'
& $sdk publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o PulseDeck.' }
if (-not $SkipTests) {
    $exe = Join-Path $output 'PulseDeck.exe'
    $test = Start-Process -FilePath $exe -ArgumentList '--self-test','--output',('"' + (Join-Path $PSScriptRoot 'hardware-report.json') + '"') -WindowStyle Hidden -PassThru -Wait
    if ($test.ExitCode -ne 0) { throw "Autoteste falhou: código $($test.ExitCode). Consulte dist/diagnostic-error.txt." }
}
$ready = Join-Path $PSScriptRoot 'PulseDeck.exe'
try { Copy-Item -LiteralPath (Join-Path $output 'PulseDeck.exe') -Destination $ready -Force -ErrorAction Stop }
catch [System.IO.IOException] {
    $ready = Join-Path $output 'PulseDeck.exe'
    Write-Warning 'O PulseDeck.exe está aberto. Feche o aplicativo e execute o build novamente para atualizar o EXE da raiz.'
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-LICENSES.txt') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $output -Force
Write-Output ('Aplicativo pronto: ' + $ready)
