# Dizin yapısı

Yeni bir dosya nereye konur sorusunun tek cevabı bu dosyadır.
Kurallar için `CLAUDE.md`, gerekçeler için `docs/decisions.md`, şema için `docs/ledger-schema.md`.

## Repo kökü

```
HiWallet/
├── CLAUDE.md
├── README.md
├── HiWallet.sln
├── docker-compose.yml
├── .env.example
├── .gitignore
├── Directory.Build.props          -- ortak TargetFramework, Nullable, LangVersion
├── Directory.Packages.props       -- merkezi paket versiyonlama
├── docs/
├── src/
└── tests/
```

Tek repo. Servisler ayrı veritabanı kullanır (`decisions.md` madde 7) ama repo bölünmez —
bu aşamada repo ayrımı yalnızca koordinasyon maliyeti getirir.

Paket versiyonları `Directory.Packages.props`'ta merkezi. Servislerin `.csproj`
dosyalarında `Version` attribute'u YAZILMAZ, yalnızca `PackageReference Include`.

## docs/

```
docs/
├── decisions.md                          -- kararlar, gerekçeler, elenen alternatifler
├── ledger-schema.md                      -- DDL, invariant zorlaması, settlement kayıtları
├── structure.md                          -- bu dosya
├── Bölüm 1 - Giriş ve Harita.md          -- kapsam ve harita
├── Bölüm 2 — Baseline (ortak çatı).md    -- 12 zorunlu katman
└── Bölüm 3 — Wallet Distributed.md       -- servisler, akışlar, saga, çıkış kriteri
```

Bölüm dosyaları da repoda durur; kapsamı ve dominant temayı görmeden doğru karar verilemez.
Adları Obsidian wiki-link'leriyle (`[[Bölüm 2 — Baseline (ortak çatı)]]`) eşleştiği için
ASCII slug'a çevrilmez.

## Adlandırma: ürün vs konsept

Marka **Hive**, ürün **HiWallet**. Solution `HiWallet.sln`, assembly ve namespace kökü
`HiWallet.*` → `HiWallet.WalletService`, `HiWallet.Shared.Contracts`.

Bölüm dosyalarında geçen `wallet-distributed` referans uygulama setinin konsept adıdır,
kodda kullanılmaz. Klasör adları (`src/WalletService/`) kökü tekrar etmez; kök prefix
`.csproj` içindeki `RootNamespace`/`AssemblyName` ile verilir.

## src/

```
src/
├── WalletService/
├── WithdrawalOrchestrator/
├── TopupWebhook/
├── TopupConsumer/
├── BankService.Fake/
├── ProviderFake/
└── Shared/
    ├── Shared.Contracts/
    └── Shared.Infrastructure/
```

Her servis kendi klasöründe, kendi `Program.cs`'i ve kendi `Dockerfile`'ı ile.

### WalletService (çekirdek, en katmanlı olan)

```
WalletService/
├── WalletService.csproj
├── Program.cs
├── Dockerfile
├── appsettings.json
├── appsettings.Development.json
├── Api/
│   ├── Controllers/           -- TransfersController, WalletsController, ...
│   ├── Requests/              -- CreateTransferRequest, ...
│   ├── Responses/             -- TransferResponse, BalanceResponse, ...
│   └── Validators/            -- CreateTransferRequestValidator, ...
├── Application/
│   ├── Transfers/             -- TransferCommand + TransferHandler yan yana
│   ├── Balances/
│   ├── Topups/
│   └── Abstractions/          -- IPaymentProvider, IBankProvider, IClock
├── Domain/
│   ├── Accounts/              -- Account, AccountType, OwnerType
│   ├── Ledger/                -- LedgerTransaction, LedgerEntry, Money
│   ├── Balances/              -- WalletBalance
│   ├── Policies/              -- LimitPolicy, CommissionPolicy, TransferType
│   └── Errors/                -- InsufficientFundsException, LimitExceededException
├── Infrastructure/
│   ├── Persistence/
│   │   ├── WalletDbContext.cs
│   │   ├── Configurations/    -- IEntityTypeConfiguration<T> başına bir dosya
│   │   └── Migrations/        -- EF Core üretir, elle düzenlenmez
│   ├── Messaging/             -- publisher, consumer, outbox relay
│   ├── Providers/             -- IPaymentProvider'ın HTTP implementasyonu
│   └── Jobs/                  -- ReconciliationJob, BusinessSummaryJob
└── Setup/
    ├── ObservabilitySetup.cs  -- OTel traces/metrics/logs
    ├── HealthChecksSetup.cs
    ├── RateLimitingSetup.cs
    ├── ProblemDetailsSetup.cs
    └── ConfigValidation.cs    -- fail-fast startup kontrolü
```

**Bağımlılık yönü:** `Api → Application → Domain`, `Infrastructure → Application`.
Domain hiçbir şeye referans vermez — EF Core attribute'u, `DbContext`, `HttpClient`,
`ILogger` Domain'e girmez. EF yapılandırması `Configurations/` altında Fluent API ile yapılır.

**Feature klasörü kuralı:** `Application/` altında teknik değil işlevsel gruplama.
`Commands/`, `Queries/`, `Handlers/` diye üç ayrı klasör AÇILMAZ; `Transfers/` altında
`TransferCommand.cs`, `TransferHandler.cs`, `TransferResult.cs` yan yana durur.

**`Setup/` neden var:** `Program.cs` 300 satıra çıkmasın diye. Her baseline katmanı
kendi extension metodunda; `Program.cs` yalnızca çağırır.

### Diğer servisler

Aynı iskelet, daha az katman. Ölçüsü: bir klasör tek dosya içeriyorsa açma.

```
WithdrawalOrchestrator/
├── Api/Controllers/           -- WithdrawalsController
├── Application/Withdrawals/   -- saga handler'ları, komut/event tipleri
├── Domain/Saga/               -- WithdrawalSaga, WithdrawalState, geçiş kuralları
├── Infrastructure/
│   ├── Persistence/           -- OrchestratorDbContext, kendi migration'ları
│   ├── Messaging/
│   └── Jobs/                  -- StuckSagaScanJob
└── Setup/

TopupWebhook/
├── Api/Controllers/           -- WebhookController
├── Application/               -- imza doğrulama, inbox yazımı
├── Infrastructure/
│   ├── Persistence/           -- inbox tablosu
│   └── Messaging/             -- relay worker
└── Setup/

TopupConsumer/
├── Application/               -- topup handler
├── Infrastructure/
│   ├── Persistence/           -- processed_events
│   └── Messaging/             -- consumer
└── Setup/

BankService.Fake/
├── Api/Controllers/
├── Application/               -- komut işleme, senaryo tetikleyicileri
└── Setup/

ProviderFake/
├── Api/Controllers/           -- senaryo tetikleme endpoint'leri
├── Application/               -- webhook üretimi (duplicate, gecikmeli, sırasız)
└── Setup/
```

`BankService.Fake` ve `ProviderFake` fake olmalarına rağmen `Setup/` alır: logging,
tracing ve health check onlarda da çalışmalı, yoksa uçtan uca trace kopar.

### Shared/

```
Shared/
├── Shared.Contracts/
│   ├── Commands/              -- InitiateBankTransfer, RefundToWallet, ...
│   ├── Events/                -- BankTransferSucceeded, TopupReceived, ...
│   └── Envelope.cs            -- MessageId, CorrelationId, OccurredAt
└── Shared.Infrastructure/
    ├── Messaging/             -- RabbitMQ bağlantısı, publisher confirms, consistent hashing
    ├── Idempotency/           -- ON CONFLICT kalıbı için ortak yardımcılar
    ├── Observability/         -- OTel ortak yapılandırması
    └── ProblemDetails/        -- ortak exception → ProblemDetails eşlemesi
```

**Shared kuralı:** yalnızca iki servis gerçekten aynı koda ihtiyaç duyduğunda buraya taşınır.
"İleride lazım olur" diye önden konulmaz. Domain tipi, entity veya `DbContext`
Shared'a KONULMAZ — servis sınırını delen şey budur.

`Shared.Contracts` yalnızca POCO taşır: paket referansı almaz, davranış içermez.

## tests/

```
tests/
├── WalletService.UnitTests/
│   ├── Policies/              -- limit, komisyon hesapları
│   └── Ledger/                -- zero-sum, işaret konvansiyonu
├── WalletService.IntegrationTests/
│   ├── Fixtures/              -- PostgresFixture (Testcontainers)
│   ├── Transfers/             -- concurrency, idempotency replay
│   └── Ledger/                -- invariant, projeksiyon tutarlılığı
├── WithdrawalOrchestrator.IntegrationTests/
│   └── Saga/                  -- happy path, compensation, retry
└── EndToEnd.Tests/
    └── Scenarios/             -- webhook → kuyruk → consumer → ledger
```

**Ayrım.** Unit test DB'ye dokunmaz, saf hesaplama. Integration test gerçek Postgres
(Testcontainers) kullanır, mock DB yok. E2E test `docker compose` ile tüm stack'i kaldırır.

**İlk yazılacak test** — çekirdek koddan önce, `WalletService.IntegrationTests/Ledger/`:
500 eşzamanlı transfer sonrası tüm hesapların `amount` toplamı sıfır ve hiçbir
`user_wallet` negatif değil.

## Yerleştirme kuralları

| Yazdığın şey | Nereye |
|---|---|
| Yeni endpoint | ilgili servisin `Api/Controllers/` |
| Request/response DTO | `Api/Requests/` veya `Api/Responses/` |
| FluentValidation validator | `Api/Validators/`, DTO ile aynı isim + `Validator` |
| İş akışı (command + handler) | `Application/<Feature>/` |
| Dış servis arayüzü | `Application/Abstractions/` |
| Dış servis implementasyonu | `Infrastructure/Providers/` |
| Entity, value object, iş kuralı | `Domain/<Alan>/` |
| EF Core yapılandırması | `Infrastructure/Persistence/Configurations/` |
| Migration | `Infrastructure/Persistence/Migrations/` (EF üretir) |
| Background job | `Infrastructure/Jobs/` |
| Servisler arası komut/event | `Shared.Contracts/Commands` veya `Events` |
| Baseline katman kurulumu | `Setup/` |
| Karar ve gerekçe | `docs/decisions.md` |
| Pazarlıksız kural | `CLAUDE.md` |

## Adlandırma

- Klasör ve namespace çoğul (`Transfers`, `Accounts`), tip tekil (`Transfer`, `Account`).
- Namespace = `HiWallet.` + dizin yolu: `src/WalletService/Application/Transfers/` →
  `HiWallet.WalletService.Application.Transfers`.
- Command: `<Fiil><Nesne>Command` → `CreateTransferCommand`. Handler: `<Command adı>Handler`.
- Event geçmiş zaman: `BankTransferSucceeded`, `TopupReceived`.
- Tablo adları `snake_case` ve çoğul (`ledger_entries`), C# tarafı `PascalCase` tekil.
  Eşleme `Configurations/` altında açıkça yazılır, global convention'a bırakılmaz.
- Test metodu: `Metot_Durum_BeklenenSonuc`.

## Migration komutları

Her servisin kendi migration klasörü var, `--project` ve `--startup-project` zorunlu.
Mac'te, repo kökünde:

```bash
dotnet ef migrations add InitialLedger \
  --project src/WalletService \
  --startup-project src/WalletService \
  --output-dir Infrastructure/Persistence/Migrations
```

Migration'lar elle düzenlenmez. Trigger ve `REVOKE` gibi ham SQL gereken yerler
migration içinde `migrationBuilder.Sql(...)` ile eklenir — ayrı `.sql` dosyası
tutulmaz, versiyonlama kopmasın.