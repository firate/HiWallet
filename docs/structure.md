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
├── overview.md          -- sistem: kapsam, servisler, akışlar, saga, çıkış kriteri
├── baseline.md          -- uygulamadan bağımsız 12 zorunlu katman
├── decisions.md         -- kararlar, gerekçeler, elenen alternatifler
├── ledger-schema.md     -- DDL, invariant zorlaması, settlement kayıtları
└── structure.md         -- bu dosya
```

**`overview.md` ile `baseline.md` ayrımı:** birincisi sistemin ne yaptığı, ikincisi ne
yaptığından bağımsız olarak her serviste beklenen production hijyeni. Bir şey "wallet
olduğu için" böyleyse `overview.md`'ye, "her serviste böyle olur" diyorsan `baseline.md`'ye.

`overview.md`'nin numaralı başlıkları (madde 1–10) `decisions.md` ve kod yorumlarından
atıf alıyor. Numaralandırma değiştirilmez; yeni bölüm sona eklenir.

## Adlandırma: ürün vs konsept

Marka **Hive**, ürün **HiWallet**. Solution `HiWallet.sln`, assembly ve namespace kökü
`HiWallet.*` → `HiWallet.WalletService`, `HiWallet.Shared.Contracts`.

Dokümanların ilk halinde geçen `wallet-distributed` bir konsept adıydı, kodda kullanılmaz.
Klasör adları (`src/WalletService/`) kökü tekrar etmez; kök prefix `.csproj` içindeki
`RootNamespace`/`AssemblyName` ile verilir.

## src/

```
src/
├── WalletService.Core/     -- kütüphane, host değil
├── WalletApi/              -- host
├── WalletConsumer/         -- host
├── TopupWebhook/           -- host
├── WithdrawalOrchestrator/
├── BankService.Fake/
└── Shared/
    ├── Shared.Contracts/
    └── Shared.Infrastructure/
```

Her **host** kendi klasöründe, kendi `Program.cs`'i ve kendi `Dockerfile`'ı ile.

**Servis ≠ deployable.** Wallet sınırının iki host'u var — `WalletApi` (public HTTP)
ve `WalletConsumer` (ingress'siz worker; hem top-up event'lerini hem çekim
komutlarını dinliyor) — ve ikisi de `WalletService.Core`'u kullanıyor. Ayrılma sebebi erişim seviyesi (`decisions.md` madde 28); ortak kütüphane
sebebi ise ledger'a yazan kodun tek kopya olması zorunluluğu (madde 25).

`WalletService.Core`'un `RootNamespace`'i `HiWallet.WalletService` olarak elle
sabitlenmiş: "Core" assembly adında duruyor, tip adlarında değil.

**`WalletService.Core`'a wallet sınırı DIŞINDAN referans verilmez.** `TopupWebhook`
onu görmemeli — gördüğü an ayrı veritabanı sınırı yapısal bir gerçek olmaktan çıkıp
nezaket kuralına döner. Kütüphane sınırı "altyapıya dokunuyor mu" ile değil, **veri
sahipliği** ile çizilir.

### WalletService.Core (çekirdek, en katmanlı olan)

Kütüphane. `Program.cs` yok, sadece migrator'ı üreten bir `Dockerfile` var — şema
host'ların değil, kütüphanenin.

```
WalletService.Core/
├── WalletService.Core.csproj
├── Dockerfile                 -- yalnızca migration bundle
├── Application/
│   ├── Accounts/              -- hesap ve cüzdan açma, hesap detayı
│   ├── Transfers/             -- TransferCommand + TransferHandler yan yana
│   ├── Balances/              -- cüzdan sorgulama (bakiye projeksiyondan okunur)
│   ├── Topups/                -- ProcessTopupHandler (ledger'a yazan taraf)
│   ├── Withdrawals/           -- çekim komut handler'ları + ters kayıt
│   └── Abstractions/          -- IPaymentProvider, IBankProvider, IClock
├── Domain/
│   ├── Accounts/              -- Account (müşteri hesabı), AccountType (person/business)
│   ├── Ledger/                -- Money, Currency, LedgerAccount, LedgerAccountType,
│   │                             LedgerTransaction, LedgerEntry, LedgerTransactionType
│   ├── Balances/              -- LedgerBalance
│   ├── Policies/              -- TransferType, LimitPolicy, CommissionPolicy, WithdrawalPolicy
│   └── Errors/                -- DomainException + InsufficientFunds, LimitExceeded,
│                                 UnbalancedLedgerTransaction, UnsupportedCurrency;
│                                 NotFoundException + Wallet/AccountNotFound
├── Infrastructure/
│   ├── Persistence/
│   │   ├── WalletDbContext.cs
│   │   ├── Configurations/    -- IEntityTypeConfiguration<T> başına bir dosya
│   │   └── Migrations/        -- EF Core üretir, elle düzenlenmez
│   ├── Providers/             -- IPaymentProvider'ın HTTP implementasyonu
│   └── Jobs/                  -- ReconciliationJob, BusinessSummaryJob
└── Setup/
    ├── PersistenceSetup.cs    -- iki host da kullanıyor
    └── PoliciesSetup.cs       -- iki host da kullanıyor
```

Host'a özel kurulum Core'a GİRMEZ: rate limiting, ProblemDetails, model doğrulama
ve controller kaydı yalnızca `WalletApi`'de. Ölçü basit — iki host'un da ihtiyacı
varsa Core'a, yoksa host'a.

### WalletApi (public host)

```
WalletApi/
├── WalletApi.csproj
├── Program.cs
├── Dockerfile
├── appsettings.json
├── Controllers/               -- TransfersController, ...
├── Requests/                  -- CreateTransferRequest, ...
├── Responses/                 -- TransferResponse, ...
├── Validators/                -- CreateTransferRequestValidator, ...
└── Setup/                     -- RateLimiting, ProblemDetails, Validation,
                                  HealthChecks, ConfigValidation
```

`Api/` ara klasörü YOK: proje zaten API, ikinci kez söylemenin anlamı yok.

RabbitMQ referansı yok ve eklenmez (`decisions.md` madde 28).

### WalletConsumer (ingress'siz host)

```
WalletConsumer/
├── WalletConsumer.csproj
├── Program.cs                  -- controller yok, API dokümanı yok, rate limiter yok
├── Dockerfile
├── Topups/                     -- TopupConsumerService: top-up kuyruklarını dinler
├── Withdrawals/                -- WithdrawalCommandConsumer: çekim komutlarını dinler
├── WalletConsumerSetup.cs      -- DI + sağlık kontrolleri
└── WalletConsumerApp.cs        -- test giriş noktası işaretçisi
```

İki kuyruk tek process'te: ikisi de ingress'siz ve ikisi de aynı ledger'a yazıyor,
yani ayırmanın erişim seviyesi gerekçesi yok (`decisions.md` madde 28).

`Sdk.Web` kullanıyor ama tek HTTP yüzeyi sağlık ucu. Probe olmasaydı "process ayakta
ama tüketici tıkanmış" durumu görünmezdi.

Kuyruk plumbing'i (kanal, ack/nack, dead-letter kararı) burada; ledger'a yazan
`ProcessTopupHandler` Core'da. Ayrım kasıtlı — biri taşıma, öbürü iş kuralı.

**Bağımlılık yönü:** `Host → Application → Domain`, `Infrastructure → Application`.
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
├── Application/Withdrawals/   -- saga handler'ları
├── Domain/                    -- WithdrawalSaga, WithdrawalState, Iban
├── Infrastructure/
│   ├── Persistence/           -- OrchestratorDbContext, withdrawal_sagas,
│   │                             withdrawal_outbox, kendi migration'ları
│   ├── Messaging/             -- outbox relay, event tüketicisi
│   └── Jobs/                  -- StuckSagaScanJob
└── Setup/

TopupWebhook/
├── Api/Controllers/           -- TopupWebhookController
├── Api/Requests/              -- TopupWebhookPayload
├── Application/               -- WebhookSignature, WebhookSecrets, TopupInboxWriter
├── Infrastructure/
│   ├── Persistence/           -- InboxDbContext, topup_inbox, kendi migration'ları
│   └── Messaging/             -- TopupRelay
└── Setup/

BankService.Fake/
├── Api/Controllers/           -- ScenariosController (senaryo tetikleyicileri)
├── Api/Requests/
├── Application/               -- StartBankTransferHandler, ScenarioStore
├── Infrastructure/
│   ├── Persistence/           -- BankDbContext, bank_transfers, transfer_scenarios
│   └── Messaging/             -- BankCommandConsumer
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
    ├── Messaging/             -- RabbitMQ bağlantısı, topup topolojisi, sağlık kontrolü
    ├── Jobs/                  -- PeriodicTimer tabanı, pg_try_advisory_lock kirası
    ├── Observability/         -- OTel ortak yapılandırması
    ├── OpenApi/               -- OpenAPI dokümanı + Scalar, yalnızca Development'ta
    └── HealthChecks/          -- /health/live ve /health/ready uçları
```

`Shared.Infrastructure` `FrameworkReference` ile `Microsoft.AspNetCore.App`'e bağlanıyor:
tüketicilerinin hepsi zaten ASP.NET Core uygulaması, böylece Options/DI/Logging/HealthChecks
için ayrı paket sürümü yönetmeye gerek kalmıyor.

Topoloji neden burada: hem publish eden hem tüketen taraf aynı exchange/kuyruk adlarını
ve argümanlarını kullanmak zorunda. İki yerde ayrı yazılsaydı ilk sapmada broker
`PRECONDITION_FAILED` verirdi.

**Shared kuralı:** yalnızca iki servis gerçekten aynı koda ihtiyaç duyduğunda buraya taşınır.
"İleride lazım olur" diye önden konulmaz. Domain tipi, entity veya `DbContext`
Shared'a KONULMAZ — servis sınırını delen şey budur.

`Shared.Contracts` yalnızca POCO taşır: paket referansı almaz, davranış içermez.

## tests/

```
tests/
├── UnitTests/
│   ├── Policies/              -- limit, komisyon hesapları
│   ├── Ledger/                -- zero-sum, işaret konvansiyonu
│   └── Topups/                -- webhook imzası
└── IntegrationTests/
    ├── Fixtures/              -- PostgresFixture, InboxFixture, API fabrikaları
    ├── Baseline/              -- health, rate limiting
    ├── Transfers/             -- concurrency, idempotency replay
    ├── Ledger/                -- invariant, projeksiyon, rol yetkileri
    └── Topups/                -- webhook→inbox, tüketici, uçtan uca hat
```

**Test projesi adları servise bağlı DEĞİL.** Top-up hattı iki servise yayılıyor ve
uçtan uca test ikisini birden ayağa kaldırıyor; `WalletService.IntegrationTests` adı
yanıltıcı olurdu.

**Ayrım.** Unit test DB'ye dokunmaz, saf hesaplama. Integration test gerçek Postgres
kullanır, mock DB yok — uçtan uca senaryolar da burada, ayrı bir E2E projesi yok:
`WebApplicationFactory` iki servisi de aynı süreçte kaldırabiliyor ve aralarındaki
gerçek sınır (ayrı veritabanı, ayrı uygulama, arada broker) korunuyor.

**Dış bağımlılığı olan testler atlanabilir.** RabbitMQ erişilemiyorsa uçtan uca
testler `Assert.SkipUnless` ile atlanıyor; rol kurulu değilse yetki testleri de öyle.
Kurulumu zorunlu kılmak yerine varsa doğrulanıyor — atlanan test yeşil değil "skipped"
görünüyor, hangi güvencenin ölçülmediği çıktıdan okunuyor.

**Integration test izolasyonu: koşu başına schema.** `PostgresFixture` her koşuda kendi
schema'sını açar, migration'ı oraya uygular, sonunda `DROP SCHEMA ... CASCADE` ile düşürür.
Bağlantı `ConnectionStrings__IntegrationTests`'ten gelir.

Testcontainers elenmedi ama seçilmedi: aynı izolasyonu verir, karşılığında bir Docker
daemon'a konuşmak zorunda. Schema yolu Docker'sız çalışıyor ve paralel koşuyu da
engellemiyor (schema adları farklı). Bedeli: testler bir Postgres sunucusuna erişim
istiyor, offline çalışmıyor.

**İlk yazılacak test** — çekirdek koddan önce, `IntegrationTests/Ledger/`:
500 eşzamanlı transfer sonrası tüm hesapların `amount` toplamı sıfır ve hiçbir
`user_wallet` negatif değil.

## Yerleştirme kuralları

| Yazdığın şey | Nereye |
|---|---|
| Yeni endpoint | ilgili host'un `Controllers/` |
| Request/response DTO | host'un `Requests/` veya `Responses/` |
| FluentValidation validator | host'un `Validators/`, DTO ile aynı isim + `Validator` |
| İş akışı (command + handler) | `Application/<Feature>/` |
| Dış servis arayüzü | `Application/Abstractions/` |
| Dış servis implementasyonu | `Infrastructure/Providers/` |
| Entity, value object, iş kuralı | `Domain/<Alan>/` |
| EF Core yapılandırması | `Infrastructure/Persistence/Configurations/` |
| Migration | `Infrastructure/Persistence/Migrations/` (EF üretir) |
| Background job | `Infrastructure/Jobs/` |
| Servisler arası komut/event | `Shared.Contracts/Commands` veya `Events` |
| Baseline katman kurulumu | host'un `Setup/`'ı; iki host da kullanıyorsa `WalletService.Core/Setup/` |
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