# HiWallet

E-money cüzdan sistemi. Double-entry ledger + saga orchestration.

Referans uygulama: mimari production seviyesinde, kapsam bilinçli olarak dar. Dış
servisler fake ama her biri bir interface arkasında; katman ayrımı, transaction
sınırları, idempotency, concurrency stratejisi ve invariant zorlaması gerçekte nasıl
yapılıyorsa öyle.

## Ne gösteriyor

**Çekirdek (immediate, ACID).** Cüzdanlar arası transfer: tek serviste, tek DB,
double-entry, optimistic lock. Saga yok — dağıtık karmaşıklık tutarlılığın kritik
olduğu yere taşınmıyor.

**Kenar (eventual).** Dış dünyayla konuşan akışlar. İki hat da çalışıyor:

- **Top-up:** webhook ayrı bir serviste kendi veritabanıyla, arada RabbitMQ, tüketici
  üçüncü bir uygulamada.
- **Withdrawal saga:** orchestrator kendi veritabanında saga durumunu yürütüyor,
  wallet parayı düşüyor, banka transferi yapıyor. Banka reddederse **compensation**
  cüzdana parayı geri yazıyor — silmeyle değil, üç bacaklı ters kayıtla.

## Beş uygulama, erişim seviyesine göre ayrılmış

| deployable | ingress | Postgres | RabbitMQ |
| --- | --- | --- | --- |
| `wallet-api` | **public** — mobil/web | `hiwallet_wallet` / `wallet_app` | — |
| `topup-webhook` | **IP kısıtlı** — sağlayıcı | `hiwallet_topup` / `topup_app` | publish |
| `wallet-consumer` | **yok** | `hiwallet_wallet` / `wallet_app` | consume |
| `withdrawal-orchestrator` | public — çekim isteği | `hiwallet_withdrawal` | ikisi de |
| `bank-service` (fake) | yalnızca senaryo ucu | `hiwallet_bank` | ikisi de |

Ayrımın sebebi ağ maruziyeti: banka webhook'u belirli IP bloklarına açılacak, cüzdan
API'si herkese. IP kısıtı process seviyesinde uygulanamaz.

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
| Transfer çekirdeği (5 tip), limit ve komisyon | ✅ |
| Double-entry ledger, zero-sum invariant | ✅ DB trigger + testler |
| Optimistic lock, retry, idempotency | ✅ |
| `POST /v1/transfers`, ProblemDetails | ✅ |
| Baseline: OTel, health, rate limiting, validation | ✅ |
| Top-up hattı (webhook → inbox → relay → RabbitMQ → consumer) | ✅ |
| HMAC imza, iki kademe idempotency, dead-letter | ✅ |
| Deployable ayrımı erişim seviyesine göre | ✅ |
| Hattın gerçek bir broker'a karşı uçtan uca koşması | ✅ webhook → RabbitMQ → ledger |
| Withdrawal saga: state machine, outbox, IBAN doğrulama | ✅ |
| Compensation: üç bacaklı ters kayıt (komisyon dahil) | ✅ |
| Saga zincirinin uçtan uca koşması | ✅ API → wallet → banka → saga |
| Çekimin compose'a alınması | ⬜ adım 4.11 |
| Scheduled job'lar (mutabakat, özet, stuck saga) | ⬜ adım 5 |

193 test: 92 unit (DB'siz), 101 integration — gerçek Postgres ve gerçek RabbitMQ.

İki uçtan uca zincir koşuyor. Top-up: HTTP → inbox → relay → broker → tüketici →
ledger. Withdrawal: `POST /v1/withdrawals` → orchestrator → wallet-consumer →
bank-service → orchestrator, üç uygulama ayrı ayrı ayakta ve aralarında yalnızca
broker var.

## Çalıştırma

```bash
cp .env.example .env      # <DOLDUR> yazan yerleri doldur
docker compose up --build
```

Sırayla: Postgres ayağa kalkar ve roller/veritabanları kurulur → `migrator` ve
`topup-migrator` şemaları uygular → uygulamalar başlar. RabbitMQ paralel kalkar;
hiçbiri onu BEKLEMEZ.

> **Compose'da şu an üç uygulama var:** `wallet-api`, `topup-webhook`,
> `wallet-consumer`. `withdrawal-orchestrator` ve `bank-service` kodda ve testlerde
> çalışıyor ama compose'a **henüz alınmadı** (adım 4.11). Yani çekim akışını bugün
> yalnızca `dotnet test` ile görebilirsin, `docker compose up` ile değil.

```bash
curl http://localhost:8091/health/ready   # wallet-api
curl http://localhost:8092/health/ready   # topup-webhook
```

`wallet-consumer`'ın host'a açılmış portu yok — sağlık kontrolü container'ın içinden
koşuyor (`docker compose ps` ile görülür). Ingress'i olmayan bir uygulamanın port
açmasının sebebi olmazdı.

Swagger: <http://localhost:8091/swagger> (Development'ta).

Host portlarının varsayılanı homelab'a göre seçildi (`8091`, `8092`, `5433`, `5673`);
orada `8080` Keycloak'ta, `8090` dolu, `5432` ana Postgres'te ve `5672` mevcut
broker'da. Başka bir makinede `.env`'den değiştirilebilir.

> **Bu compose sürümü henüz koşturulmadı.** İki uygulamalı önceki sürüm homelab'da
> doğrulanmıştı (migration'lar uygulandı, `wallet_app` konteyner içinde de
> `ledger_entries`'i güncelleyemedi). Üçüncü uygulama, RabbitMQ ve ikinci veritabanı
> o koşuda yoktu. Adımlar ve açık uçlar:
> **[docs/verify-compose.md](docs/verify-compose.md)**

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

**Komisyonlu ödeme.** Komisyon ayrı bir transfer değil, aynı atomik işlemin ek bacağı:

```bash
curl -X POST http://localhost:8091/v1/transfers \
  -H 'Content-Type: application/json' \
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

**Top-up (dışarıdan para girişi).** Sağlayıcı webhook'u imzalayarak gönderir; imza ham
gövde baytları üzerinde HMAC-SHA256:

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

Orchestrator compose'a henüz alınmadığı için host portu yok; isteğin şekli şöyle
(`WithdrawalsApiTests` ve `WithdrawalChainTests` bunu koşturuyor):

```
POST /v1/withdrawals
Idempotency-Key: cekim-1

{"accountId":"...","walletId":"...","amount":100,"currency":"TRY",
 "destinationIban":"TR33 0006 1005 1978 6457 8413 26"}
```

Yanıt **`202 Accepted`**: döndüğünde hiçbir para hareket etmemiş durumda. IBAN sınırda
mod-97 ile doğrulanıyor; geçersizse `400` ve saga hiç başlamıyor.

Mutlu yolda ledger'a üç satır düşer: cüzdan `-102`, `clearing/bank-fake` `+100`,
`revenue` `+2`.

**Banka reddederse compensation.** Ters kayıt orijinalin aynası ve **üç bacaklı**:
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
gerekmiyor. topup-webhook, withdrawal-orchestrator ve bank-service için ayrı schema'lar
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
| [docs/overview.md](docs/overview.md) | sistem: kapsam, servisler, akışlar, saga, çıkış kriteri |
| [docs/baseline.md](docs/baseline.md) | uygulamadan bağımsız 12 zorunlu katman |
| [docs/decisions.md](docs/decisions.md) | kararlar, gerekçeler, **elenen alternatifler** |
| [docs/ledger-schema.md](docs/ledger-schema.md) | şemanın okunabilir karşılığı |
| [docs/structure.md](docs/structure.md) | yeni dosya nereye konur |
| [Claude.md](Claude.md) | pazarlıksız kurallar |

Şemanın tek kaynağı EF migration'ları; `ledger-schema.md` onları açıklar, üretmez.

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
