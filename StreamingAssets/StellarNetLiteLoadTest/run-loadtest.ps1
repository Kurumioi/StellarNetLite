param(
    [ValidateSet('kcp', 'tcp')]
    [string]$Transport = 'kcp',
    [string]$ServerHost = '127.0.0.1',
    [int]$Port = 7777,
    [int]$Rooms = 1,
    [int]$ClientsPerRoom = 50,
    [int]$RedundantClientsPerRoom = 0,
    [int]$ConnectRate = 10,
    [int]$Duration = 0,
    [int]$MoveRate = 8,
    [string]$RoomName = 'LoadTestRoom',
    [string]$AccountPrefix = 'bot',
    [string]$ClientVersion = '0.0.1',
    [int]$LogInterval = 5
)

$ErrorActionPreference = 'Stop'

function Resolve-Dotnet {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    $fallback = 'C:\Program Files\dotnet\dotnet.exe'
    if (Test-Path $fallback) {
        return $fallback
    }

    throw "dotnet was not found. Please install .NET SDK 8 or add dotnet to PATH."
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $scriptDir 'StellarNetLiteLoadTest.csproj'
$dotnetExe = Resolve-Dotnet
$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'StellarNetLiteLoadTest'
$stagingDir = Join-Path $stagingRoot ([Guid]::NewGuid().ToString('N'))
$dllPath = Join-Path $stagingDir 'StellarNetLiteLoadTest.dll'

Write-Host "Starting load test tool..." -ForegroundColor Cyan
Write-Host "transport=$Transport host=$ServerHost port=$Port rooms=$Rooms perRoom=$ClientsPerRoom redundant=$RedundantClientsPerRoom total=$($Rooms * $ClientsPerRoom) duration=$Duration" -ForegroundColor DarkGray

New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

try {
    Write-Host "build-output=$stagingDir" -ForegroundColor DarkGray

    & $dotnetExe build $projectPath -c Release -o $stagingDir /p:UseAppHost=false | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE"
    }

    & $dotnetExe $dllPath `
        --transport $Transport `
        --host $ServerHost `
        --port $Port `
        --rooms $Rooms `
        --clients-per-room $ClientsPerRoom `
        --redundant-clients-per-room $RedundantClientsPerRoom `
        --connect-rate $ConnectRate `
        --duration $Duration `
        --move-rate $MoveRate `
        --room-name $RoomName `
        --account-prefix $AccountPrefix `
        --client-version $ClientVersion `
        --log-interval $LogInterval

    if ($LASTEXITCODE -ne 0) {
        throw "load test exited with code $LASTEXITCODE"
    }
}
finally {
    Remove-Item -LiteralPath $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
}
