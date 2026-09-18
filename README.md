# HiWallet

E-money cüzdan sistemi. Double-entry ledger + saga orchestration.

Referans uygulama: mimari production seviyesinde, kapsam bilinçli olarak dar. Dış
kurumlar sahte ama **sınırları gerçek** — banka ayrı bir process, arada HTTP ve
callback var, wallet'ı göremiyor. Katman ayrımı, transaction sınırları, idempotency,
concurrency stratejisi ve invariant zorlaması gerçekte nasıl yapılıyorsa öyle.

## Ne gösteriyor

**Çekirdek (immediate, ACID).** Cüzdanlar arası transfer: tek serviste, tek DB,
double-entry, optimistic lock. Saga yok — dağıtık karmaşıklık tutarlılığın kritik
olduğu yere taşınmıyor.

**Kenar (eventual).** Dış dünyayla konuşan akışlar. İki hat da çalışıyor:

- **Top-up:** webhook ayrı bir serviste kendi veritabanıyla, arada RabbitMQ, tüketici
  üçüncü bir uygulamada.
- **Withdrawal saga:** orchestrator kendi veritabanında saga durumunu yürütüyor,
  wallet parayı düşüyor, adaptör bankayı HTTP ile arıyor. Banka "aldım" diyor,
  **sonuç sonra** callback ile geliyor — saga o arada gerçekten bekliyor. Banka
  reddederse **compensation** cüzdana parayı geri yazıyor: silmeyle değil, üç
  bacaklı ters kayıtla.

## Altı uygulama, erişim seviyesine göre ayrılmış

| deployable | ingress | Postgres | RabbitMQ |
| --- | --- | --- | --- |
| `wallet-api` | **public** — mobil/web | `hiwallet_wallet` / `wallet_app` | — |
| `topup-webhook` | **IP kısıtlı** — sağlayıcı | `hiwallet_topup` / `topup_app` | publish |
| `wallet-consumer` | **yok** | `hiwallet_wallet` / `wallet_app` | consume |
| `withdrawal-orchestrator` | public — çekim isteği | `hiwallet_withdrawal` | ikisi de |
| `bank-adapter` | **yok** | `hiwallet_bank` / `bank_app` | ikisi de |
| `bank-webhook` | **IP kısıtlı** — banka | `hiwallet_bank` / `bank_app` | — |

Ayrımın sebebi ağ maruziyeti: banka webhook'u belirli IP bloklarına açılacak, cüzdan
API'si herkese. IP kısıtı process seviyesinde uygulanamaz.

Bir de **bizim olmayan iki** uygulama var:

| | temsil ettiği kurum | ne yapıyor |
| --- | --- | --- |
| `bank-fake` | bankamız | para girişi **ve** çıkışı; hafızası bellekte, veritabanı yok |
| `stripe-fake` | kart sağlayıcısı | yalnızca para girişi; veritabanı yok |

İkisi de üretimde yok — yerlerine kurumların kendi uçları geçiyor. `.Fake` son ekinin
ölçütü "test amaçlı mı" değil, "başka bir kurumun yerine mi duruyor" (`decisions.md`
madde 35).

`bank-fake`'in iki yönde de çalışması tesadüf değil: aynı banka hem gelen havaleyi
bildiriyor hem giden transferi kabul ediyor. Ledger'da da öyle — `clearing/bank-fake`
iki yönde de hareket ediyor. Stripe'ın `nostro`'su yok, çünkü nostro bir banka hesabı.

Kodları da `src/` altında değil, kökteki **`fakes/`** klasöründe: üretimde deploy
edilen hiçbir şey oradan çıkmıyor. `src/` → `fakes/` referansı derleme hatası
(`HIW001`) — kural yorumda değil, derleyicide.

Ölçüt iki yöne de işliyor: farklı maruziyet aynı process'te birleşmiyor, **aynı
maruziyet de gereksiz bölünmüyor.** `wallet-consumer` iki kuyruğu birden dinliyor —
top-up event'leri ve çekim komutları — çünkü ikisi de ingress'siz ve aynı ledger'a
aynı kütüphaneyle yazıyor.

`wallet-api` ve `wallet-consumer` aynı şemayı yazıyor ve **aynı kütüphaneyi**
(`WalletService.Core`) paylaşıyor. Ayrı deployable, tek kod tabanı — çünkü ledger
invariant'larının bir kısmı hiçbir constraint tarafından zorlanmıyor (cüzdanın negatife
düşememesi, bakiye satırlarının artan id sırasıyla güncellenmesi, `version`'ın birer
artması). İkinci bir kopya, ilk sapmada sessizce bozulurdu.

Public yüzeyin RabbitMQ bağımlılığı **yok**: tüketici ayrıldıktan sonra `wallet-api`
yalnızca Postgres'e bağlı.

Ana mesaj bu ayrımda: **tutarlılığın kritik olduğu çekirdeği tek boundary'de ACID tut,
sadece dışarıyla konuşan kenarı dağıt.**

## Şu an ne çalışıyor

| | durum |
| --- | --- |
| Hesap ve cüzdan uçları (`/v1/accounts`, `/v1/wallets`) | evet |
| Transfer çekirdeği (5 tip), limit ve komisyon | evet |
| Double-entry ledger, zero-sum invariant | evet — DB trigger + testler |
| Optimistic lock, retry, idempotency | evet |
| `POST /v1/transfers`, ProblemDetails | evet |
| Baseline: OTel, health, rate limiting, validation | evet |
| Top-up hattı (webhook → inbox → relay → RabbitMQ → consumer) | evet |
| HMAC imza, iki kademe idempotency, dead-letter | evet |
| Deployable ayrımı erişim seviyesine göre | evet |
| Hattın gerçek bir broker'a karşı uçtan uca koşması | evet — webhook → RabbitMQ → ledger |
| Withdrawal saga: state machine, outbox, IBAN doğrulama | evet |
| Compensation: üç bacaklı ters kayıt (komisyon dahil) | evet |
| Saga zincirinin uçtan uca koşması | evet — API → wallet → adaptör → banka → callback → saga |
| Banka entegrasyonu: asenkron sonuç, callback + mutabakat | evet |
| Sahte sağlayıcılar top-up'ı tetikliyor (tekrar, gecikme, **sırasız**) | evet |
| Sekiz container'ın compose'dan ayağa kalkması | evet |
| Takılmış saga taraması (job altyapısı + advisory lock) | evet |
| Business günlük özeti | evet |
| Sağlayıcı ücreti tahakkuku (`provider_fees`, Net/Invoiced) | evet |
| Settlement: clearing kapanır, `nostro` hareket eder | evet — top-up ve çekim |
| Fatura işleme, uyuşmazlıkta `PendingReview` | evet |
| Mutabakat raporu (projeksiyon, yaşlanma, fatura) | evet |
| Çekim settlement'ı (banka ücreti saga üzerinden) | evet |
| Relay tekilliği: sıra broker'a varmadan bozulmuyor | evet — advisory lock |

290 test: 96 unit (DB'siz), 194 integration — gerçek Postgres ve gerçek RabbitMQ.

İki uçtan uca zincir koşuyor. Top-up: HTTP → inbox → relay → broker → tüketici →
ledger. Withdrawal: `POST /v1/withdrawals` → orchestrator → wallet-consumer →
bank-adapter → (HTTP) banka → callback → bank-webhook → inbox → relay →
orchestrator. Beş host ayrı ayrı ayakta; aralarında hem broker hem gerçek HTTP var.

## Çalıştırma

```bash
cp .env.example .env      # <DOLDUR> yazan yerleri doldur
docker compose up --build
```

Sırayla: Postgres ayağa kalkar, beş rol, dört uygulama veritabanı ve integration testlerin
veritabanı (`hiwallet_schema_check`) kurulur → dört migrator
şemaları uygular → sekiz container başlar (altısı bizim, ikisi sahte kurum). RabbitMQ
paralel kalkar; hiçbiri onu BEKLEMEZ.

```bash
curl http://localhost:8091/health/ready   # wallet-api
curl http://localhost:8092/health/ready   # topup-webhook
curl http://localhost:8093/health/ready   # withdrawal-orchestrator
curl http://localhost:8094/health/ready   # bank-fake (BİZİM DEĞİL, üretimde yok)
curl http://localhost:8095/health/ready   # bank-webhook
curl http://localhost:8096/health/ready   # stripe-fake (BİZİM DEĞİL, üretimde yok)
```

`wallet-consumer`'ın host'a açılmış portu yok — sağlık kontrolü container'ın içinden
koşuyor (`docker compose ps` ile görülür). Ingress'i olmayan bir uygulamanın port
açmasının sebebi olmazdı.

API dokümanı, yalnızca Development'ta:

| | Scalar arayüzü | OpenAPI dokümanı |
| --- | --- | --- |
| `wallet-api` | <http://localhost:8091/scalar/> | `/openapi/v1.json` |
| `withdrawal-orchestrator` | <http://localhost:8093/scalar/> | `/openapi/v1.json` |

Sondaki eğik çizgi bilerek: eğik çizgisiz adres `302` ile ona yönleniyor. Tarayıcı
takip ediyor, `curl` varsayılan olarak etmiyor.

`topup-webhook`'ta yok: o sözleşmeyi sağlayıcı dayatıyor, biz belgelemiyoruz.

Host portlarının varsayılanı (`8091`–`8096`, `5433`, `5673`) alışıldık portlardan
bilerek kaçıyor: `8080`, `5432` ve `5672` geliştirme makinelerinde çoğu zaman dolu.
`.env`'den değiştirilebilir.

> **Stack compose'dan koşuyor ve uçtan uca akışları geçiyor.** Top-up, çekimin
> mutlu yolu ve telafi yolu compose üzerinde doğrulandı: banka reddettiğinde
> bakiye `500` → `500` dönüyor ve ters kaydın `revenue` bacağı yerinde. Yapısal
> tarafta uygulamalar `healthy`, migrator'lar şemaları uyguluyor, `wallet_app`
> konteyner içinde de `ledger_entries`'i güncelleyemiyor ve her rol yalnızca kendi
> veritabanına bağlanabiliyor. Adımlar, ölçülen çıktılar ve açık uçlar:
> **[docs/verify-compose.md](docs/verify-compose.md)**
>
> **Bu doğrulama bankanın SENKRON cevap verdiği sürümde yapıldı.** Asenkron hat
> (adaptör → banka → callback → webhook) testlerde koşuyor ama compose üzerinde
> henüz tekrarlanmadı; `verify-compose.md`'de açık uç olarak duruyor.

### İki veritabanı rolü

`migrator` **`wallet_owner`** ile, uygulama **`wallet_app`** ile bağlanır. Ayrım şart:
`ledger_entries` üzerindeki `REVOKE UPDATE, DELETE` yalnızca tablo sahibi **olmayan**
bir role işler — sahiplik yetkisi örtüktür ve revoke edilemez. Tek rolle çalışılsaydı
append-only kuralı tamamen süs olurdu.

Ölçülmüş hali (`AppRolePrivilegeTests`):

| rol | SELECT | UPDATE | DELETE |
| --- | --- | --- | --- |
| `wallet_app` | izin var | `42501` reddedildi | `42501` reddedildi |
| `wallet_owner` | izin var | izin var | izin var |

## Senaryolar

**Hesap ve cüzdan açma.** Diğer her şeyin başlangıcı; aşağıdaki `walletId`'ler buradan gelir.

```bash
ACCOUNT=$(curl -s -X POST http://localhost:8091/v1/accounts \
  -H 'Content-Type: application/json' -d '{"type":"Person"}' | jq -r .accountId)

WALLET=$(curl -s -X POST http://localhost:8091/v1/accounts/$ACCOUNT/wallets \
  -H 'Content-Type: application/json' -d '{"name":"Birikim","currency":"TRY"}' | jq -r .walletId)

curl -s http://localhost:8091/v1/wallets/$WALLET
```

Cüzdan sıfır bakiyeyle açılır ve **para yalnızca ledger üzerinden girer** — top-up ya da
transfer. Bakiyeye doğrudan yazan bir uç yok, olsaydı zero-sum invariant'ı delerdi.

Bir hesabın aynı para biriminde birden fazla cüzdanı olabilir (`decisions.md` madde 20);
`GET /v1/accounts/{id}` hepsini bakiyeleriyle listeler. Günlük limit bu yüzden cüzdan
değil **hesap** bazında uygulanır.

**Komisyonlu ödeme.** Komisyon ayrı bir transfer değil, aynı atomik işlemin ek bacağı:

```bash
curl -X POST http://localhost:8091/v1/transfers \
  -H 'Content-Type: application/json' \
  -H "Idempotency-Key: $(uuidgen)" \
  -d '{"fromWalletId":"...","toWalletId":"...","amount":200,"currency":"TRY","type":"Payment"}'
```

Ledger'a üç satır düşer: gönderen `-204`, alan `+200`, `revenue` `+4`. Toplam sıfır.

**Idempotency.** Aynı `Idempotency-Key` ile ikinci istek yeni transfer yapmaz:

```bash
curl -X POST http://localhost:8091/v1/transfers \
  -H 'Idempotency-Key: ayni-istek' -H 'Content-Type: application/json' -d '{...}'
```

İkinci yanıt aynı `transactionId` ve `"replayed": true` döner.

**Yetersiz bakiye / limit aşımı** → `422` + `rule` alanı.
**Concurrency çakışması** (retry tükendi) → `409`. İkisi karıştırılmaz.

**Elle denemenin en kolay yolu:** Rider'da `fakes/akislar.http`,
`fakes/Bank.Fake/bank-fake.http` ve `fakes/Stripe.Fake/stripe-fake.http`. Sağ
üstten ortam seç (`homelab` / `local`), istekleri sırayla koş; kimlikler bir
sonrakine kendiliğinden taşınıyor. Aşağıdaki `curl` örnekleri aynı işi yapıyor.

**Top-up (dışarıdan para girişi).** En kolayı sahte sağlayıcıya söylemek — imzayı
o hesaplıyor:

```bash
curl -X POST http://localhost:8096/v1/topups -H 'Content-Type: application/json' \
  -d "{\"walletId\":\"$WALLET\",\"amount\":100,\"currency\":\"TRY\",\"mode\":\"Normal\"}"
```

`mode`: `Normal` | `Duplicate` | `Delayed` | `OutOfOrder`. `Duplicate` aynı event'i
iki kez gönderiyor — bakiye **bir kez** artmalı. `OutOfOrder` aynı cüzdana N event'i
ters sırada gönderiyor.

Bankadan yükleme için aynı uç `8094`'te (`bank-fake`), `clearing/bank-fake`'e yazar.

Elle göndermek istersen imza ham gövde baytları üzerinde HMAC-SHA256:

```bash
BODY='{"eventId":"evt_1","walletId":"...","amount":100.00,"currency":"TRY","reference":"pi_1","occurredAt":"2026-03-01T10:00:00+00:00"}'
SIG=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$STRIPE_FAKE_WEBHOOK_SECRET" -hex | awk '{print $2}')
curl -X POST http://localhost:8092/v1/webhooks/topup/stripe-fake -H 'Content-Type: application/json' -H "X-Hive-Signature: sha256=$SIG" --data "$BODY"
```

Yanıt **`202 Accepted`** — `200` değil, bilerek: verilen söz "işledim" değil "kalıcı
kaydettim". Para yanıt döndüğünde henüz cüzdanda değil.

Yol: **202 (inbox commit'inden sonra) → relay → RabbitMQ → tüketici → ledger.**
Ledger'a iki satır düşer: cüzdan `+100`, `clearing/stripe-fake` `-100` (sağlayıcıdan
alacak). Toplam sıfır.

Aynı webhook ikinci kez gelirse yine `202` döner ama `"duplicate": true` ve bakiye
değişmez. İki kademe de devrede: inbox `(provider, event_id)` UNIQUE onu kuyruğa hiç
koymaz, koysa bile tüketicideki `processed_events` yutar.

İmza tutmazsa `401` ve inbox'a **hiçbir şey** yazılmaz.

**Withdrawal (dışarıya para çıkışı).** Saga'nın evi. `Idempotency-Key` burada
**zorunlu** — transfer'dekinin aksine: çekim çok adımlı ve dışarıya para çıkarıyor,
anahtarsız bir tekrar ikinci bir banka transferi başlatırdı.

```bash
curl -X POST http://localhost:8093/v1/withdrawals \
  -H 'Idempotency-Key: cekim-1' -H 'Content-Type: application/json' \
  -d '{"accountId":"...","walletId":"...","amount":100,"currency":"TRY",
       "destinationIban":"TR33 0006 1005 1978 6457 8413 26"}'
```

Yanıt **`202 Accepted`**: döndüğünde hiçbir para hareket etmemiş durumda. IBAN sınırda
mod-97 ile doğrulanıyor; geçersizse `400` ve saga hiç başlamıyor.

Mutlu yolda ledger'a üç satır düşer: cüzdan `-102`, `clearing/bank-fake` `+100`,
`revenue` `+2`.

Durumu `GET /v1/withdrawals/{id}` ile izleyebilirsin: `initiated` → `debited` →
`bank_transfer_pending` → `settling` → `completed`.

`bank_transfer_pending`'de birkaç saniye takılı görmen normal ve **istenen şey**:
banka çağrısı "aldım" diyor, sonuç callback ile sonra geliyor (`decisions.md`
madde 35). Süreyi `BANK_SETTLEMENT_DELAY` belirliyor.

**Banka reddederse compensation.** Sahte bankaya söyleyerek tetiklenir — anahtar
saga kimliği, alan adı `clientReference` (bankanın gözünde bizim referansımız):

```bash
curl -X POST http://localhost:8094/v1/scenarios -H 'Content-Type: application/json' \
  -d '{"clientReference":"<ID>","outcome":"Failure"}'
```

Ters kayıt orijinalin aynası ve **üç bacaklı**:
cüzdan `+102`, clearing `-100`, `revenue` `-2`. Komisyon iadesi koşulsuz — başarısız
bir çekimin sebebi ya bizde ya bankada.

`revenue` bacağının kritikliği şurada: atlansaydı kayıt **yine dengeli** olurdu,
zero-sum trigger'ı susardı ve müşteri gerçekleşmemiş bir işlemin komisyonunu ödemiş
kalırdı. Bu yüzden ters kayıt politikadan yeniden üretilmiyor — orijinal işlemin
bacakları okunup negatifleniyor.

Orijinal kayıt **silinmiyor**; ledger append-only, saga başına iki işlem kalıyor.

**Zero-sum, yük altında.** `EszamanliTransferler_ZeroSumKorunur_VeHicbirCuzdanNegatifDusmez`
500 eşzamanlı transfer atıyor ve dört şeyi doğruluyor: her transaction'ın toplamı sıfır,
sistem genelinde toplam sıfır, hiçbir cüzdan negatif değil, projeksiyon ledger'dan
sapmamış. Testin iddiası "her transfer başarılı olur" değil — çakışanlar `409` alıyor —
**"ne olursa olsun invariant korunur."**

## Testler

```bash
dotnet test
```

Integration testler bir Postgres sunucusu ister; bağlantı
`ConnectionStrings__IntegrationTests`'ten gelir. Her koşu kendi schema'sını açar,
migration'ı oraya uygular, sonunda düşürür — izolasyon böyle sağlanıyor, Docker
gerekmiyor. topup-webhook, withdrawal-orchestrator, banka entegrasyonu ve sahte banka için ayrı schema'lar
açılıyor: üretimdeki ayrı veritabanı sınırları testte de korunuyor, servisler
birbirinin tablosunu göremiyor.

Uçtan uca testler ayrıca bir RabbitMQ ister (`RabbitMq__*`). Top-up hattı bir de
`rabbitmq_consistent_hash_exchange` eklentisi istiyor — routing key cüzdan kimliği ve
partition'ı o exchange seçiyor. Withdrawal zinciri düz bir direct exchange kullanıyor,
eklenti istemiyor:

```bash
docker exec <rabbitmq> rabbitmq-plugins enable rabbitmq_consistent_hash_exchange
```

İkisinden biri eksikse test `Assert.SkipUnless` ile atlanıyor ve mesajda eksiğin ne
olduğu yazıyor — kurulumu zorunlu kılmak yerine varsa doğrulanıyor, atlanan test yeşil
değil "skipped" görünüyor.

Her koşu kendine özel bir exchange/kuyruk ön eki kullanıyor (`RabbitMq:NamePrefix`) ve
sonunda topolojiyi siliyor: aynı broker'a bakan iki koşu birbirinin kuyruğundan mesaj
çekmiyor, broker'da da çöp birikmiyor.

```bash
set -a; . ./.env; set +a
dotnet test
```

## Dokümanlar

| dosya | ne anlatır |
| --- | --- |
| [docs/architecture.md](docs/architecture.md) | **diyagramlar**: topoloji, akışlar, paranın nerede durduğu |
| [docs/overview.md](docs/overview.md) | sistem: kapsam, servisler, akışlar, saga, çıkış kriteri |
| [docs/baseline.md](docs/baseline.md) | uygulamadan bağımsız 12 zorunlu katman |
| [docs/decisions.md](docs/decisions.md) | kararlar, gerekçeler, **elenen alternatifler** |
| [docs/ledger-schema.md](docs/ledger-schema.md) | şemanın okunabilir karşılığı |
| [docs/structure.md](docs/structure.md) | yeni dosya nereye konur |
| [docs/api-examples.md](docs/api-examples.md) | her uç için istek ve beklenen yanıt |
| [docs/verify-compose.md](docs/verify-compose.md) | compose'u ayağa kaldırma ve doğrulama |
| [CLAUDE.md](CLAUDE.md) | pazarlıksız kurallar |

Şemanın tek kaynağı EF migration'ları; `ledger-schema.md` onları açıklar, üretmez.

### Hangi sırayla okunur

**Baştan sona okunacak tek doküman yok.** Hangisini açacağın ne yaptığına bağlı:

**Sistemi tanımak ya da hatırlamak için:** `architecture.md` → `overview.md` →
`ledger-schema.md`. İlki en hızlı resmi veriyor, hepsi diyagram. `overview.md`'nin
1–10 numaralı maddeleri sistemi anlatıyor; o numaralara `decisions.md` ve kod
yorumları atıf yapıyor, bu yüzden sabitler. Üçüncüsünü ancak tablo yapısı
gerektiğinde aç.

**Kod yazarken:** `CLAUDE.md` → `structure.md` → `decisions.md`'de ilgili madde.
İlki kuralların listesi, ikincisi yeni dosyanın nereye konacağı. `decisions.md`
baştan sona OKUNMAZ — 1400 satır ve referans niteliğinde; merak ettiğin maddeyi ara
(banka entegrasyonu 35, servis sınırı 7 ve 33, aktör 34).

**Elle denerken:** `fakes/` altındaki `.http` dosyaları → `api-examples.md` →
`verify-compose.md`. İlki Rider'da en hızlı yol, ikincisi bir şey bozulduğunda
karşılaştırman için beklenen yanıtları veriyor.

**Yeni karar alırken:** `decisions.md`. Karar oraya gerekçesiyle yazılıyor, sonra
`CLAUDE.md`'ye ve ilgili dokümanlara yayılıyor.

`baseline.md` nadiren açılır: uygulamadan bağımsız ve neredeyse hiç değişmiyor.

## Bilinçli sınırlamalar

Eksik değil, **elenmiş** — gerekçeleri `decisions.md` madde 12'de:

- **Rate limiting in-memory.** Çok instance'ta efektif limit instance başınadır.
- **Secret yönetimi `.env` + User Secrets.** Vault yok.
- **Authn/authz yok.** Endpoint'ler açık.
- **Caching yok.** Bakiye projeksiyonu cache değil, kalıcı read tablosu.
- **Multi-tenancy yok.** Person/business ayrımı hesap tipidir, tenancy değil.

Ayrıca kayda geçirilmiş, henüz sorun olmayan bir darboğaz var: komisyonlu her transfer
tek `revenue` satırını güncelliyor ve eşzamanlı komisyonlu transferler cüzdanları farklı
olsa bile çakışıyor (`decisions.md` madde 23).

## Lisans

MIT — [LICENSE](LICENSE).

Bu bir **referans uygulamasıdır**, üretime hazır bir e-para sistemi değil. Yukarıdaki
bilinçli sınırlamalar okunmadan üretim amacıyla kullanılmamalı. `stripe-fake` ve
`bank-fake` yerel simülatörlerdir; hiçbir ödeme sağlayıcısıyla ilişkisi yoktur.
