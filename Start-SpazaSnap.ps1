#requires -Version 7.0
param([switch]$UseExistingMySql)
$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:ASPNETCORE_ENVIRONMENT='Development'
if(-not $UseExistingMySql){
    if(-not (Test-Path .env)){
        $rootPassword=[guid]::NewGuid().ToString('N')+[guid]::NewGuid().ToString('N')
        $appPassword=[guid]::NewGuid().ToString('N')+[guid]::NewGuid().ToString('N')
        "MYSQL_ROOT_PASSWORD=$rootPassword`nMYSQL_PASSWORD=$appPassword`nDOMAIN=spazasnap.example.com" | Set-Content .env
        Write-Output 'Created local database settings in .env. Keep this file private.'
    }
    $settings=@{}
    Get-Content .env | ForEach-Object { if($_ -match '^([A-Z_]+)=(.*)$'){ $settings[$matches[1]]=$matches[2].Trim() } }
    if(-not $settings['MYSQL_PASSWORD']){throw 'Set MYSQL_PASSWORD in .env.'}
    docker compose up -d --wait
    if($LASTEXITCODE -ne 0){throw 'MySQL did not start. Open Docker Desktop and try again.'}
    $quotedPassword='"'+$settings['MYSQL_PASSWORD'].Replace('"','""')+'"'
    $env:ConnectionStrings__MySql="Server=127.0.0.1;Port=3307;Database=spazasnap;User ID=spazasnap;Password=$quotedPassword;Connection Timeout=10"
} elseif(-not $env:ConnectionStrings__MySql){throw 'Set ConnectionStrings__MySql before using -UseExistingMySql.'}
dotnet run --project SpazaSnap.csproj --no-launch-profile -- --init-db
if($LASTEXITCODE -ne 0){throw 'Database initialization failed.'}
Write-Output 'Open http://localhost:5038 and choose Create your shop. Press Ctrl+C to stop the app.'
dotnet run --project SpazaSnap.csproj --no-build --no-launch-profile --urls http://localhost:5038
