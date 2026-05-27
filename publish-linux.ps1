#!/usr/bin/env pwsh
# Publishes a self-contained single-file Linux x64 binary.
# Upload PaymentService + appsettings.json to /home/processor/dati/processes/payment-service/ on the server.

dotnet publish PaymentService/PaymentService.csproj `
    -c Release `
    -r linux-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -o PaymentService/bin/Release/net10.0/linux-x64

Write-Host ""
Write-Host "Done. Output: PaymentService/bin/Release/net10.0/linux-x64/"
Write-Host "Upload to: processor@jaroslavs.molport.com:/home/processor/dati/processes/payment-service/"
