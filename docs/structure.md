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
├── docker-compose.proxy.yml       -- ek: API'ler ve Keycloak bir Traefik'in arkasında,
│                                     kapısı docker/gateway/ (Caddyfile ve imajı)
├── .env.example
├── .gitignore
├── .dockerignore                  -- paketler ve derleme çıktıları imaja girmiyor
├── Directory.Build.props          -- ortak TargetFramework, Nullable, LangVersion
├── Directory.Packages.props       -- merkezi paket versiyonlama
├── docs/
├── src/                           -- kimin için olduğuna göre klasörlü (aşağıda)
├── fakes/                         -- başka kurumların yerinde duran servisler; canlıda YOK
└── tests/
```

Tek repo. Servisler ayrı veritabanı kullanır (`decisions.md` madde 7) ama repo bölünmez —
bu aşamada repo ayrımı yalnızca koordinasyon maliyeti getirir.

Paket versiyonları `Directory.Packages.props`'ta merkezi. Servislerin `.csproj`
dosyalarında `Version` attribute'u YAZILMAZ, yalnızca `PackageReference Include`.

## docs/

```
docs/
├── architecture.md      -- diyagramlar: topoloji, akışlar, paranın nerede durduğu
├── overview.md          -- sistem: kapsam, servisler, akışlar, saga, çıkış kriteri
├── baseline.md          -- uygulamadan bağımsız 12 zorunlu katman
├── decisions.md         -- kararlar, gerekçeler, elenen alternatifler
├── ledger-schema.md     -- DDL, invariant zorlaması, settlement kayıtları
├── structure.md         -- bu dosya
├── api-examples.md      -- her endpoint için request ve beklenen response
└── verify-compose.md    -- compose'u ayağa kaldırma ve doğrulama
```

Klasörde alfabetik duruyorlar; okuma sırası README'nin "Hangi sırayla okunur"
bölümünde. Sıra dosya adlarına numara olarak GÖMÜLMÜYOR: aradan bir doküman
eklendiğinde bütün adlar ve onlara verilen linkler kayardı.

**`overview.md` ile `baseline.md` ayrımı:** birincisi sistemin ne yaptığı, ikincisi ne
yaptığından bağımsız olarak her serviste beklenen production hijyeni. Bir şey "wallet
olduğu için" böyleyse `overview.md`'ye, "her serviste böyle olur" diyorsan `baseline.md`'ye.

`overview.md`'nin numaralı başlıkları (madde 1–10) `decisions.md` ve kod yorumlarından
atıf alıyor. Numaralandırma değiştirilmez; yeni bölüm sona eklenir.

## Adlandırma: ürün vs konsept

Marka **Hive**, ürün **HiWallet**. Solution `HiWallet.sln`, assembly ve namespace kökü
`HiWallet.*` → `HiWallet.WalletService`, `HiWallet.Shared.Contracts`.

Dokümanların ilk halinde geçen `wallet-distributed` bir konsept adıydı, kodda kullanılmaz.
Proje klasörleri (`WalletService.Core/`) kökü tekrar etmez; kök prefix `.csproj` içindeki
`RootNamespace`/`AssemblyName` ile verilir.

## src/

```
src/
├── personal/                  -- bireysel müşteri
│   ├── edge/
│   │   ├── PersonalMobileApi/ -- ön API, public: mobil uygulama
│   │   ├── PersonalWebBff/    -- ön API, public: web uygulamasının BFF'i
│   │   └── PersonalWeb/       -- web uygulaması (tarayıcıda); PersonalWebBff'in imajına giriyor
│   └── internal/
│       └── Onboarding/        -- iç servis: kayıt ve kimlik doğrulaması, bireysel hesabı açıyor
├── business/                  -- işyeri
│   └── edge/
│       ├── BusinessApi/       -- ön API, public: işyerinin sistem entegrasyonu
│       └── BusinessWebBff/    -- ön API, public: işyeri panelinin BFF'i
├── backoffice/                -- şirketin çalışanları
│   ├── edge/
│   │   ├── BackofficeBff/     -- ön API, iç ağ: panelin BFF'i
│   │   └── BackofficeWeb/     -- panel (tarayıcıda); BackofficeBff'in imajına giriyor
│   └── internal/
│       └── StaffAdmin/        -- iç servis: personel yönetimi
├── core/                      -- paranın hareket ettiği servisler; hepsi istemciye kapalı
│   ├── wallet/                -- ledger'ın sahibi: WalletApi, WalletConsumer, WalletService.Core
│   ├── card/                  -- kartla yükleme: CardTopup, TopupWebhook
│   ├── bank/                  -- banka: BankAdapter, BankWebhook, BankIntegration.Core
│   └── withdrawal/            -- çekim saga'sı: WithdrawalOrchestrator
├── shared/                    -- birden fazla klasörün kullandığı kütüphaneler
│   ├── EdgeApi.Core/          -- ön API'lerin ortak kodu
│   ├── Shared.Contracts/
│   └── Shared.Infrastructure/
└── Directory.Build.targets    -- src/ → fakes/ referansı derleme hatası
```

İlk seviye kimin için olduğunu söylüyor: bireysel müşteri, işyeri, şirketin çalışanları.
Bir kitleye hizmet eden her şey onun klasöründe. Tek bir kitleye ait olmayan, hepsinin
parasını taşıyan servisler `core/`'da, paranın yoluna göre: cüzdan, kart, banka, çekim.

İkinci seviye erişim: istemcinin bağlandığı yüzey ile iç servis aynı seviyede DURMAZ.
`edge/` istemcinin konuştuğu her şey: ön API'ler ve tarayıcıda çalışan arayüzler.
`internal/` istemcinin doğrudan bağlanamadığı iç servisler; onlara yalnızca ön API'ler ve
öteki iç servisler ulaşıyor. Kitlenin iç servisi yoksa `internal/` açılmıyor (`business/`).
`core/`'da bu ayrım yok, çünkü orada istemcinin bağlandığı proje yok: iç servisler,
ingress'siz tüketiciler ve yalnızca kart sağlayıcısının ve bankanın çağırdığı webhook'lar.

Her **host** kendi klasöründe, kendi `Program.cs`'i ve kendi `Dockerfile`'ı ile.
`HiWallet.sln`'in klasörleri diskle aynı. Gruplama klasörleri (`personal/edge/`,
`core/wallet/`) namespace'e GİRMEZ: namespace projenin kökünden başlıyor.

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
│   ├── Deposits/              -- ProcessDepositHandler (havale: cüzdan ya da askı),
│   │                             askıdaki havalelerin listesi, MoveSuspendedDepositHandler
│   │                             (askıdan cüzdana aktarım)
│   ├── Withdrawals/           -- çekim komut handler'ları + ters kayıt
│   ├── Settlements/           -- ProcessSettlementHandler, ProcessInvoiceHandler
│   ├── Promos/                -- işyerinin promo vermesi, cüzdanın parti listesi
│   └── Abstractions/          -- IClock, IHolderIdentity
├── Domain/
│   ├── Accounts/              -- Account (müşteri hesabı), AccountType (person/business)
│   ├── Deposits/              -- SuspendedDeposit, DepositHoldReason,
│   │                             SuspendedDepositResolution
│   ├── Ledger/                -- Money, Currency, LedgerAccount, LedgerAccountType,
│   │                             LedgerTransaction, LedgerEntry, LedgerTransactionType
│   ├── Balances/              -- LedgerBalance
│   ├── Policies/              -- TransferType, LimitPolicy, CommissionPolicy, WithdrawalPolicy
│   ├── Promos/                -- PromoGrant, PromoConsumption, PromoCampaign, PromoLots (tüketim sırası)
│   └── Errors/                -- DomainException + InsufficientFunds, LimitExceeded,
│                                 UnbalancedLedgerTransaction, UnsupportedCurrency,
│                                 PromoGrantRejected;
│                                 NotFoundException + Wallet/AccountNotFound
├── Infrastructure/
│   ├── Persistence/
│   │   ├── WalletDbContext.cs
│   │   ├── Configurations/    -- IEntityTypeConfiguration<T> başına bir dosya
│   │   └── Migrations/        -- EF Core üretir, elle düzenlenmez
│   └── Jobs/                  -- ReconciliationJob, BusinessSummaryJob, PromoExpiryJob,
│                                 PromoCampaignJob + ayarları
└── Setup/
    ├── PersistenceSetup.cs    -- iki host da kullanıyor
    ├── PoliciesSetup.cs       -- iki host da kullanıyor
    └── WalletJobsSetup.cs     -- job tipleri internal, kaydı burada
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

### Ön API'ler: PersonalMobileApi, PersonalWebBff, BusinessApi, BusinessWebBff, BackofficeBff

```
PersonalMobileApi/             -- public; bireysel mobil uygulama
├── PersonalMobileApi.csproj   -- EdgeApi.Core ve Shared.Infrastructure'a referans
├── Program.cs                 -- token doğrulama, kovaların varsayılanı
├── PersonalMobileApiApp.cs    -- test giriş noktası işaretçisi
├── Controllers/               -- Accounts, Wallets, Transfers, Withdrawals
├── Dockerfile
└── appsettings.json           -- hedef kitle, iç servis zaman aşımı, rate limit

PersonalWebBff/                -- public; bireysel web uygulamasının BFF'i
├── ...                        -- aynı dosyalar
├── Controllers/               -- Accounts, Wallets, Transfers, Withdrawals, Session (/bff),
│                                 Registrations ve Onboarding (tabanı EdgeApi.Core'da)
└── Dockerfile                 -- yanındaki PersonalWeb'i derleyip wwwroot'a koyuyor

BusinessApi/                   -- public; işyerinin sistem entegrasyonu
├── ...                        -- aynı dosyalar
└── Controllers/               -- Accounts, Wallets, Transfers, Promos, Withdrawals

BackofficeBff/                 -- iç ağ; backoffice panelinin BFF'i, girişi çalışanların Keycloak'ı
├── ...                        -- aynı dosyalar
├── Controllers/               -- Accounts, Customers, Wallets, Withdrawals, PromoCampaigns,
│                                 Session (/bff)
└── Dockerfile                 -- yanındaki BackofficeWeb'i derleyip wwwroot'a koyuyor

BusinessWebBff/                -- public; işyeri panelinin BFF'i (Controllers/ henüz yok)
```

Ön API'ler wallet sınırının dışında: `WalletService.Core`'a referans vermiyor,
veritabanına bağlanmıyor. Ledger'a giden her istek `wallet-api`'den geçiyor.
`personal-mobile-api`, `personal-web-bff` ve `business-api`'nin uçları yazıldı; kayıt ve
doğrulama uçları iki bireysel ön API'de aynı, tabanları `EdgeApi.Core/Onboarding`'de.
`backoffice-bff`'in görüntüleme uçları var. İşyeri BFF'inde sağlık uçları,
ProblemDetails, OpenAPI ve telemetri kurulu.
Tarayıcıdan kullanılan arayüzün ön API'si BFF: oturumu cookie ile tutar, token'ı
tarayıcıdaki koda vermez.

Request ve response tipleri `EdgeApi.Core`'da, iç servislerin sözleşmesiyle aynı
şekilde; iç servisin cevabı doğrudan bu tiplere okunuyor. Bir ön API'nin sözleşmesi
ayrıştığı gün o ön API kendi tipini yazıyor ve eşleme controller'a ekleniyor.

### EdgeApi.Core (kütüphane)

```
EdgeApi.Core/
├── EdgeApi.Core.csproj
├── Contracts/                 -- iç servislerin request ve response tipleri
├── InternalServices/          -- WalletApiClient, WithdrawalOrchestratorClient, OnboardingClient,
│                                 adres ayarı, resilience pipeline'ı, token iletimi,
│                                 çekim başlatma
├── RateLimiting/              -- istemci, çekim ve kayıt kovaları; varsayılanı ön API veriyor
├── Onboarding/                -- kayıt ve doğrulama uçlarının soyut tabanları; rota ve
│                                 kovayı ön API alt sınıfta veriyor
├── Sessions/                  -- BFF'lerin tarayıcı oturumu: cookie, Keycloak girişi,
│                                 token yenileme, X-CSRF başlığı, giriş/çıkış/kullanıcı
│                                 uçlarının ortak controller'ı
└── Errors/                    -- iç servisin cevabını istemciye aktaran handler
```

### Tarayıcı uygulamaları: PersonalWeb, BackofficeWeb

```
personal/edge/PersonalWeb/     -- bireysel müşteri; BFF'i yanındaki PersonalWebBff
├── package.json               -- sürümler tam, package-lock.json ile
├── vite.config.ts             -- geliştirmede API ve oturum yollarını BFF'e iletiyor
├── index.html
└── src/
    ├── api.ts                 -- BFF'e istekler: X-CSRF, Idempotency-Key, ProblemDetails
    ├── session.tsx            -- oturum yoksa giriş; kayıt (/kayit) oturumun dışında
    ├── pages/                 -- sayfa başına bir bileşen, testi yanında
    ├── components/
    └── test/                  -- sahte BFF ve render yardımcısı

backoffice/edge/BackofficeWeb/ -- çalışanın paneli; BFF'i yanındaki BackofficeBff, aynı düzen
└── src/
    ├── session.tsx            -- oturum yoksa giriş, izni yoksa (403) yalnızca çıkış
    └── pages/                 -- hesap, cüzdan, çekim incelemesi, kampanyalar;
                                  staff/ altında çalışanlar, roller, kayıtlar
```

React, TypeScript ve Vite; router React Router, veri TanStack Query, test Vitest ve
Testing Library. Uygulama token görmüyor: oturum BFF'te, uygulama yalnızca BFF'in
`/v1` ve `/bff` yollarını çağırıyor. Panel düğmeleri çalışanın rolüne göre gösteriyor;
yetkiyi iç servis kendisi kontrol ediyor.

Yalnızca ön API'ler referans veriyor; iç servisler bu kodu taşımıyor. Veritabanı ve
broker bağımlılığı yok.

### WalletConsumer (ingress'siz host)

```
WalletConsumer/
├── WalletConsumer.csproj
├── Program.cs                  -- controller yok, API dokümanı yok, rate limiter yok
├── Dockerfile
├── CardTopups/                 -- CardTopupConsumer: kartla yüklemenin kapanışlarını dinler
├── Deposits/                   -- DepositConsumer: havale kuyruğunu dinler
├── Settlements/                -- settlement ve fatura kuyruğu
├── Identity/                   -- servisin kendi token'ı, onboarding'e kimlik numarası sorusu
├── Withdrawals/                -- WithdrawalCommandConsumer: çekim komutlarını dinler
├── WalletConsumerSetup.cs      -- DI + health check'ler
└── WalletConsumerApp.cs        -- test giriş noktası işaretçisi
```

Kuyruklar tek process'te: hepsi ingress'siz ve aynı ledger'a yazıyor, yani ayırmanın
erişim seviyesi gerekçesi yok (`decisions.md` madde 28).

`Sdk.Web` kullanıyor ama tek HTTP yüzeyi health check endpoint'i. Probe olmasaydı "process ayakta
ama tüketici tıkanmış" durumu görünmezdi.

Kuyruk plumbing'i (kanal, ack/nack, dead-letter kararı) burada; ledger'a yazan
`ProcessCardTopupHandler` Core'da. Ayrım kasıtlı — biri taşıma, öbürü iş kuralı.

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

CardTopup/                     -- BİZİM; iç ağ, kartla yüklemenin ömrü
├── Api/                       -- CardTopupsController, istek, doğrulama, cevaplar,
│                                 wallet-api reddinin aynen aktarılması (Errors/)
├── Application/               -- StartCardTopupHandler, ApplyCardPaymentHandler,
│                                 CardTopupTransitions, OpenCardTopupScanner, CardTopupQueries
├── Domain/                    -- CardTopup, CardTopupState (saf: geçiş kuralları)
├── Infrastructure/
│   ├── Persistence/           -- CardTopupDbContext, card_topups, card_topup_outbox,
│   │                             kendi migration'ları
│   ├── Upstream/              -- WalletHoldClient (müşterinin token'ıyla), CardPaymentClient
│   ├── Messaging/             -- ödeme bildirimi tüketicisi, outbox relay
│   └── Jobs/                  -- OpenCardTopupScan
└── Setup/

TopupWebhook/
├── Api/Controllers/           -- TopupWebhookController
├── Api/Requests/              -- TopupWebhookPayload
├── Application/               -- WebhookSignature, WebhookSecrets, TopupInboxWriter
├── Infrastructure/
│   ├── Persistence/           -- InboxDbContext, topup_inbox, kendi migration'ları
│   └── Messaging/             -- TopupRelay
└── Setup/

BankIntegration.Core/          -- şema ve migration'lar; İKİ host paylaşıyor
├── Domain/                    -- BankTransferStatus
├── Persistence/               -- BankDbContext, bank_transfers, bank_callbacks, bank_deposits
└── Setup/                     -- BankPersistenceSetup

BankAdapter/                   -- BİZİM; ingress YOK, bankayı kendisi arıyor
├── Application/               -- BankClient, StartBankTransferHandler,
│                                 TransferCompleter, DepositRecorder,
│                                 BankNotificationHandler, banka HTTP sözleşmesi
├── Infrastructure/
│   ├── Messaging/             -- BankCommandConsumer, ReplyRelay, DepositRelay
│   ├── Callbacks/             -- CallbackRelay (inbox'ı işler)
│   └── Jobs/                  -- ReconciliationScan, DepositScan
└── Setup/

BankWebhook/                   -- BİZİM; IP kısıtlı, tek işi doğrula-yaz-202
├── Api/Controllers/           -- BankWebhookController
├── Application/               -- BankCallbackSignature, BankSecrets, BankCallbackWriter
└── Setup/

fakes/Bank.Fake/               -- BANKANIN YERİNDE; canlıda YOK, `src/` ALTINDA DEĞİL
├── Api/Controllers/           -- TransfersController, ScenariosController,
│                                 IncomingTransfersController (gelen havale, hesap hareketleri)
├── Api/Requests/
├── Api/Responses/
├── Application/               -- AcceptTransferHandler, TransferQueries,
│                                 ScenarioStore, TransferResolution
├── Infrastructure/
│   ├── Storage/               -- BankFakeStore (bellekte; veritabanı YOK)
│   └── Callbacks/             -- CallbackDispatcher (sonucu ve gelen havaleyi bize POST eder)
└── Setup/

fakes/Stripe.Fake/             -- KART SAĞLAYICISI; canlıda YOK, veritabanı YOK
├── Api/Controllers/           -- PaymentsController (ödeme API'si), CheckoutController
│                                 (ödeme sayfası)
├── Payments/                  -- CardPayment, PaymentStore (bellekte)
└── Webhooks/                  -- sonucun imzalı webhook'u, ayarlar

Onboarding/                    -- BİZİM; iç ağ, kayıt ve kimlik doğrulaması
├── Domain/                    -- Registration, PhoneVerification, PhoneChange, Customer,
│                                 Consent, NationalId, PhoneNumber, VerificationCode
├── Application/               -- RegistrationService, VerificationService,
│                                 PhoneChangeService, HolderCheckService,
│                                 CustomerLookupService, dış servislerin arayüzleri
│                                 (Abstractions/)
├── Infrastructure/
│   ├── Persistence/           -- OnboardingDbContext, migration'lar; kendi Postgres sunucusu
│   ├── Keycloak/              -- yönetim API'si ve servisin kendi token'ı
│   ├── Wallet/                -- WalletAccountsClient: hesabı aç, seviyeyi yükselt,
│   │                             çekimi beklet
│   ├── Messaging/             -- SMTP e-posta, SMS sağlayıcısı
│   └── PopulationRegistry/    -- nüfus kaydı
├── Api/                       -- Registrations (kimliksiz), Me (müşterinin token'ıyla),
│                                 HolderChecks (wallet-consumer), Customers ve
│                                 CustomerSearches (çalışanın token'ıyla)
└── Setup/

StaffAdmin/                    -- BİZİM; iç ağ, personel yönetimi
├── Domain/                    -- StaffMember, StaffRole, StaffRoleAssignment,
│                                 StaffAuditEvent (değişmeyen kayıt), StaffRoleRules
├── Application/               -- RoleService, StaffService, StaffAccess (çalışanın o anki
│                                 izinleri), StaffAudit, açılış kurulumu (StaffAdminBootstrap);
│                                 kimlik sağlayıcının arayüzü (Abstractions/IStaffDirectory)
├── Infrastructure/
│   ├── Persistence/           -- StaffAdminDbContext, migration'lar; kendi Postgres sunucusu
│   └── Keycloak/              -- çalışanların Keycloak'ının yönetim API'si: kullanıcı, davet
├── Api/                       -- Me (her çalışan, kendi izni); Permissions, Roles, Staff,
│                                 AuditEvents (staff.manage)
└── Setup/

fakes/Sms.Fake/                -- SMS SAĞLAYICISI; canlıda YOK, mesajlar bellekte
fakes/Nvi.Fake/                -- NÜFUS KAYDI; canlıda YOK, senaryolar bellekte
```

### `fakes/` — canlıda olmayan servisler

**Sahte servisler `src/` altında DEĞİL, kökte ayrı bir klasörde.** Sınır dizin
seviyesinde görünüyor: canlıda deploy edilen hiçbir şey `fakes/`'ten çıkmıyor.

```
fakes/
├── Bank.Fake/               -- bankanın API'si: para girişi VE çıkışı
│   └── bank-fake.http
├── Stripe.Fake/             -- kart sağlayıcısı: ödeme API'si ve sayfası, veritabanı YOK
│   └── stripe-fake.http
├── Sms.Fake/                -- SMS sağlayıcısı: mesajı kutusunda tutuyor
├── Nvi.Fake/                -- nüfus kaydı: kimlik eşleşiyor mu
├── http-client.env.json     -- Rider ortamı: local
└── http-client.private.env.json.example
                             -- stack başka bir makinedeyse: kopyala, .example'ı at,
                                STACK-HOST'u doldur. Kopya gitignore'da.
```

`.http` dosyaları elle deneme için: senaryoyu kur, bizim tarafın tepkisini gör.
Adresler `http-client.env.json`'daki ortamdan geliyor; depoda yalnızca `local` var.
Stack başka bir makinede koşuyorsa ya da gizli bir değer gerekiyorsa
`http-client.private.env.json` kullanılır — örneği yanında, kendisi `.gitignore`'da.

Sahtelerin HTTP sözleşmesi bizim tarafla PAYLAŞILMIYOR (madde 35): `Stripe.Fake`'in ödeme
API'sinin tipleri orada, `card-topup`'ın gördüğü tipler `card-topup`'ta ayrı yazılı.

**`src/` → `fakes/` referansı DERLEME HATASI.** `src/Directory.Build.targets`
içindeki `HIW001` kontrolü engelliyor. Yorumda yazmak yetmezdi: bu proje aynı
gerekçeyle veritabanı sınırlarını da Postgres yetkileriyle zorluyor — sınır
nezaket kuralıysa baskı altında ilk delinen şey olur.

Ters yön serbest: `fakes/` → `src/shared`. Sahte servis de log ve trace üretmeli,
yoksa uçtan uca trace kopar. Bu yüzden `Setup/` klasörü onlarda da var.

`fakes/`'i yalnızca iki şey çağırır: `tests/` ve `docker-compose.yml`.

`Stripe.Fake`'in `Setup/` klasörü yok: kurulumu `Program.cs`'e sığıyor ve veritabanı
hiç yok.

**`.Fake` son ekinin ölçütü** "test amaçlı mı" değil, **"başka bir kurumun yerine mi
duruyor"** (`decisions.md` madde 35). `BankAdapter` da bugün yalnızca compose ve
testlerde koşuyor ama canlıda da koşacak — son ek almıyor. `Bank.Fake` canlıda
yok, alıyor.

`BankAdapter` ile `BankWebhook` ayrı klasörler çünkü ayrı deployable'lar: birinin
IP kısıtlı ingress'i var, öbürünün hiç ingress'i yok (madde 28). Ortak şemaları
`BankIntegration.Core`'da — `WalletApi` / `WalletConsumer` / `WalletService.Core`
üçlüsüyle aynı kalıp.

### shared/: Shared.Contracts, Shared.Infrastructure

```
shared/
├── Shared.Contracts/          -- servisler arası mesajlar, akışa göre klasörlü
│   ├── CardPayments/          -- CardPaymentUpdated (topup-webhook → card-topup)
│   ├── CardTopups/            -- CardTopupClosed (card-topup → wallet)
│   ├── Deposits/              -- BankDepositReceived
│   ├── Settlements/           -- SettlementReceived, ProviderInvoiceReceived
│   ├── Withdrawals/           -- çekim komutları ve event'leri
│   └── Actors/                -- CommandActor: ledger'a yazdıran komutun aktörü
└── Shared.Infrastructure/
    ├── Messaging/             -- RabbitMQ bağlantısı, topolojiler, health check
    ├── Jobs/                  -- PeriodicTimer tabanı, pg_try_advisory_lock kirası
    ├── Observability/         -- OTel ortak yapılandırması
    ├── OpenApi/               -- OpenAPI dokümanı + Scalar, yalnızca Development'ta
    ├── RateLimiting/          -- 429 gövdesi + Retry-After, token bucket ayarı
    ├── Authentication/        -- token doğrulama; varsayılan politika kimlik istiyor;
    │                             çalışanın izni her istekte personel yönetiminden
    └── HealthChecks/          -- /health/live ve /health/ready endpoint'leri
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
    ├── EdgeApis/              -- ön API, arkasında gerçek iç servislerle
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
Bağlantı `ConnectionStrings__IntegrationTests`'ten gelir. Broker'da karşılığı koşu başına
ön ek (`it-xxxxxxxx.`): koşu bitince `BrokerCleanup` o ön ekle açılan bütün kuyruk ve
exchange'leri siliyor. Adları topoloji sınıflarından okuyor; yeni topoloji kendiliğinden
temizleniyor. Süreç çökerse o koşunun artığı broker'da kalıyor.

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
- Namespace = projenin kök namespace'i + proje içindeki yol:
  `WalletService.Core/Application/Transfers/` → `HiWallet.WalletService.Application.Transfers`.
- Command: `<Fiil><Nesne>Command` → `CreateTransferCommand`. Handler: `<Command adı>Handler`.
- Event geçmiş zaman: `BankTransferSucceeded`, `CardTopupClosed`.
- Tablo adları `snake_case` ve çoğul (`ledger_entries`), C# tarafı `PascalCase` tekil.
  Eşleme `Configurations/` altında açıkça yazılır, global convention'a bırakılmaz.
- Test metodu: `Metot_Durum_BeklenenSonuc`.

## Migration komutları

Her servisin kendi migration klasörü var, `--project` ve `--startup-project` zorunlu.
Mac'te, repo kökünde:

```bash
dotnet ef migrations add <Ad> \
  --project src/core/wallet/WalletService.Core \
  --output-dir Infrastructure/Persistence/Migrations
```

`--output-dir` ZORUNLU: verilmezse EF dosyaları projenin kökündeki `Migrations/`
klasörüne yazar ve namespace kayar.

Migration'lar elle düzenlenmez. Trigger ve `REVOKE` gibi ham SQL gereken yerler
migration içinde `migrationBuilder.Sql(...)` ile eklenir — ayrı `.sql` dosyası
tutulmaz, versiyonlama kopmasın.

Her şema değişikliği kendi migration'ı olarak ekleniyor: verisi silinmeyen kurulumlar
migration'ları sırayla uyguluyor. Mevcut bir migration geri alınıp yeniden üretilmez.