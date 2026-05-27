# molport-payment-service

Worker service that polls Stripe and Swedbank for incoming payments and matches them to Oracle BO invoices.

## Setup

### 1. Configuration

`appsettings.json` is gitignored. Copy the template and fill in the values:

```
cp PaymentService/appsettings.template.json PaymentService/appsettings.json
```

| Setting | Where to find it |
|---|---|
| `OracleConnectionString` | Same as other internal services |
| `StripeAccounts[].ApiKey` | Stripe Dashboard → API keys → Restricted keys (one per legal entity: SIA, INC) |
| `SwedBank.ClientId` | Swedbank Developer Portal → Molport app → Production subscription |
| `SwedBank.SandboxClientId` | Swedbank Developer Portal → Molport app → Sandbox subscription |
| `SwedBank.AgreementId` | Swedbank Gateway service agreement number (see `cert/Swedbank/`) |
| `SwedBank.Iban` | Molport SIA Swedbank account IBAN |
| `SwedBank.CertificatePath` | Path to the QWAC `.p12` transport certificate (default is relative to the output dir) |
| `SwedBank.CertificatePassword` | Password for the `.p12` file |

Set `UseSandbox: true` to point at the Swedbank sandbox endpoint during development.

### 2. Certificate

The Swedbank mTLS transport certificate (`LV24_ST016.p12`) is not in the repository. Place it at:

```
cert/Swedbank/LV24_ST016.p12
```

relative to the solution root (i.e. one level above `PaymentService/`). The default `CertificatePath` in the template already points there.

### 3. Run

```
dotnet run --project PaymentService
```

## Publishing and deploying

### Publish (Visual Studio)

Open the Publish dialog for `PaymentService` and click **Publish** using the `FolderProfile` profile. This produces a single self-contained Linux x64 binary at:

```
PaymentService\bin\Release\net10.0\publish\linux-x64\PaymentService
```

### Publish (command line)

```
./publish-linux.ps1
```

Output is the same folder as above.

### Deploy

The service runs on `jaroslavs.molport.com` as user `processor` under a `screen` session named `payment-service`.

1. Connect via MobaXterm as `processor`.
2. Stop the running service:
   ```bash
   screen -S payment-service -X quit
   ```
3. Copy the `PaymentService` binary to the server (do **not** overwrite `appsettings.json` — it contains the production certificate path):
   ```
   /home/processor/dati/processes/payment-service/
   ```
4. Start the service in a new screen session:
   ```bash
   screen -S payment-service /home/processor/dati/processes/payment-service/start.sh
   ```

The `appsettings.json` on the server is not tracked in git. It uses an absolute certificate path (`/home/processor/cert/Swedbank/LV24_ST016.p12`) which differs from the development relative path in the template.
