# Compose'u doğrulama

Bu dosya `docker compose` kurulumunun gerçekten çalıştığını kanıtlamak için var.
Geliştirme makinesinde Docker yok, o yüzden koşturmak elle yapılıyor.

> **Şu anki stack DOĞRULANMADI.** Homelab'da koşturulan sürüm iki uygulamalıydı
> (wallet-service + topup-webhook, RabbitMQ yok). Bugün beş uygulama, bir broker ve
> dört veritabanı var. Aşağıdaki "Doğrulama kaydı" o eski koşuya ait ve hâlâ
> geçerli olan kısımları işaretli; yeni parçalar hiç çalıştırılmadı.

## 1. Kodu Docker'ı olan makineye al

```bash
git clone git@github.com:firate/HiWallet.git && cd HiWallet
```

## 2. `.env` hazırla

```bash
cp .env.example .env
```

Doldurulması ZORUNLU değerler:

```
POSTGRES_PASSWORD=...
WALLET_OWNER_PASSWORD=...
WALLET_APP_PASSWORD=...
TOPUP_APP_PASSWORD=...
WITHDRAWAL_APP_PASSWORD=...
BANK_APP_PASSWORD=...

RabbitMq__Username=...          # compose'daki broker'ın ilk kullanıcısı olur
RabbitMq__Password=...

STRIPE_FAKE_WEBHOOK_SECRET=...  # uzun ve rastgele
BANK_FAKE_WEBHOOK_SECRET=...
```

**Elinde eski bir `.env` varsa** `cp` YAPMA — üstüne yazar. Stack her büyüdüğünde
bu listeye yeni satır ekleniyor ve compose ilk eksik değişkende durup yalnızca
onun adını söylüyor; sırayla düzeltmek uzun sürer. Hepsini birden gör:

```bash
for v in POSTGRES_PASSWORD WALLET_OWNER_PASSWORD WALLET_APP_PASSWORD \
         TOPUP_APP_PASSWORD WITHDRAWAL_APP_PASSWORD BANK_APP_PASSWORD \
         RabbitMq__Username RabbitMq__Password \
         STRIPE_FAKE_WEBHOOK_SECRET BANK_FAKE_WEBHOOK_SECRET; do
  grep -qE "^${v}=" .env || echo "eksik: $v"
done
```

Yalnızca eksik olanın ADINI yazdırır, hiçbir değeri ekrana basmaz.

Eksik değişken `docker compose down`'ı da durdurur: compose dosyayı hangi komut
için olursa olsun önce yorumluyor. Yani `down -v && up` zincirinde hata alırsan
volume DÜŞMEMİŞTİR — `.env`'i düzelttikten sonra komutu baştan çalıştır.

Portların varsayılanı **homelab'a göre** seçildi, dokunmana gerek yok:

```
WALLET_HOST_PORT=8091        # 8080 Keycloak'ta, 8090 dolu
TOPUP_HOST_PORT=8092
WITHDRAWAL_HOST_PORT=8093
BANK_HOST_PORT=8094          # sahte bankanın senaryo ucu
POSTGRES_HOST_PORT=5433      # 5432 ana Postgres'te
RABBITMQ_HOST_PORT=5673      # 5672 mevcut broker'da
RABBITMQ_MGMT_HOST_PORT=15673
```

Telemetriyi homelab Collector'ına göndereceksen `.env`'de şunu değiştir — container
içinde `homelab` adı çözülmez, collector host tarafında:

```
OTEL_EXPORTER_OTLP_ENDPOINT=http://host.docker.internal:4317
```

Boş bırakırsan exporter hiç eklenmez ve uygulama sessizce çalışır.

## 3. Ayağa kaldır

```bash
docker compose up --build
```

Beklenen sıra: `postgres` sağlıklı olur → dört migrator (`migrator`,
`topup-migrator`, `withdrawal-migrator`, `bank-migrator`) şemaları uygulayıp
`exit 0` ile biter → beş uygulama başlar. `rabbitmq` paralel kalkar; hiçbiri onu
BEKLEMEZ (broker olmadan da ayağa kalkmalılar).

Dört veritabanı kuruluyor: `hiwallet_wallet`, `hiwallet_topup`,
`hiwallet_withdrawal`, `hiwallet_bank`. Postgres healthcheck'i sonuncusuna soruyor;
o cevap verdiğinde init'in tamamı bitmiş demektir.

### init ne zaman koşar

Rolleri ve veritabanlarını yaratan `docker/postgres-init.sh` yalnızca **veri dizini
boşken** çalışır — postgres imajı `initdb` gerektiğinde `/docker-entrypoint-initdb.d`
altındakileri koşuyor, gerektirmediğinde hiç bakmıyor. Veri dizini `postgres-data`
adlı volume.

| | init koşar mı |
|---|---|
| ilk `up` (volume yokken) | ✅ |
| `down -v` sonrası | ✅ |
| `down` (`-v` olmadan) sonrası | ❌ |
| `up --build` | ❌ imajları yeniler, volume'a dokunmaz |
| `restart`, container'ı silip yeniden yaratmak | ❌ |
| `postgres-init.sql` düzenlendikten sonra | ❌ |

En sinsi hali parola değişikliği: `.env`'de bir parolayı değiştirmek mevcut rolün
parolasını DEĞİŞTİRMEZ. Uygulama authentication hatası alır, sen de doğru parolayı
yazdığına emin olursun. Ya `down -v` ya elle `ALTER ROLE`.

Yarım kalma tuzağı: init ortasında bir komut patlarsa (`ON_ERROR_STOP=1`) container
ölür ama `initdb` çoktan koşmuştur — veri dizini artık boş değil. Sonraki `up` init'i
ATLAR ve elinde ilk roller olan, sonrakiler olmayan bir cluster kalır. Hatalar alakasız
görünür ("role withdrawal_app does not exist"). Tekrar denemek düzeltmez, `down -v`
düzeltir. Beş rolün de kurulduğunu doğrula:

```bash
docker compose exec postgres psql -U postgres -c '\du'
```

`wallet_owner`, `wallet_app`, `topup_app`, `withdrawal_app`, `bank_app`.

## 4. Doğrula

```bash
curl -s localhost:8091/health/ready
```

Beklenen: `{"status":"Healthy", ... "checks":[{"name":"postgres","status":"Healthy" ...`

Sistem hesapları seed edildi mi:

```bash
docker compose exec postgres psql -U postgres -d hiwallet_wallet -c "SELECT type, provider, currency FROM ledger_accounts WHERE account_id IS NULL ORDER BY type;"
```

Beklenen: altı satır — `clearing`×2, `nostro`, `provider_expense`×2, `revenue`.

Append-only gerçekten bağlayıcı mı (**asıl kritik kontrol**):

```bash
docker compose exec postgres psql -U wallet_app -d hiwallet_wallet -c "UPDATE ledger_entries SET amount = amount + 1;"
```

Beklenen: `ERROR: permission denied for table ledger_entries`. Başka bir şey
çıkarsa iki rollü kurulum çalışmıyor demektir.

Servis sınırı gerçekten kapalı mı — orchestrator wallet'ı GÖREMEMELİ:

```bash
docker compose exec postgres psql -U withdrawal_app -d hiwallet_wallet -c "SELECT 1;"
```

Beklenen: bağlantı reddedilir (`permission denied for database hiwallet_wallet`).
Bağlanabiliyorsa saga'nın anlamı kalmaz — orchestrator er ya da geç doğrudan yazmaya
başlar ve compensation gereksizleşir (decisions.md madde 7).

### Çekim akışını uçtan uca koşturma

Cüzdan kurmak için hesap/cüzdan endpoint'i yok; cüzdanı DB'den açmak gerekiyor
(top-up hattı bölümündeki gibi). Cüzdan hazırsa:

```bash
curl -i -X POST localhost:8093/v1/withdrawals \
  -H 'Idempotency-Key: cekim-1' -H 'Content-Type: application/json' \
  -d '{"accountId":"<ACCOUNT>","walletId":"<WALLET>","amount":100,"currency":"TRY",
       "destinationIban":"TR330006100519786457841326"}'
```

Beklenen: `202 Accepted` ve gövdede `withdrawalId`. Birkaç saniye sonra:

```bash
curl -s localhost:8093/v1/withdrawals/<ID>
```

`state` sırayla `initiated` → `debited` → `bank_transfer_pending` → `completed`
olmalı. Ledger'a üç bacaklı tek işlem düşer:

```bash
docker compose exec postgres psql -U postgres -d hiwallet_wallet -c \
  "SELECT la.type, e.amount FROM ledger_entries e
     JOIN ledger_accounts la ON la.id = e.ledger_account_id
     JOIN ledger_transactions t ON t.id = e.transaction_id
    WHERE t.correlation_id = '<ID>';"
```

**Telafi yolu.** Bankayı reddedici yapıp aynı akışı tekrarla:

```bash
curl -X POST localhost:8094/v1/scenarios -H 'Content-Type: application/json' \
  -d '{"sagaId":"<ID>","outcome":"PermanentFailure"}'
```

Senaryoyu çekim isteğinden ÖNCE kurmak gerekiyorsa (saga kimliğini önceden
bilemiyorsun) `.env`'de `BANK_DEFAULT_OUTCOME=PermanentFailure` yapıp
`docker compose up -d bank-service` ile yeniden başlat.

Beklenen: `state` `failed`, ledger'da İKİ işlem — orijinal düşme ve üç bacaklı ters
kayıt — ve cüzdan bakiyesi başladığı yerde. Komisyon da geri dönmüş olmalı.

## 5. Kapat

```bash
docker compose down -v
```

`-v` volume'u da siler; roller ve parolalar yalnızca veri dizini boşken kurulduğu
için, parola değiştirdiğinde bu şart.

## Doğrulama kaydı (iki uygulamalı sürüm)

Homelab'da `docker compose up --build` ile koşturuldu. O koşuda kanıtlananlar —
imaj ve şema tarafı değişmediği için hâlâ geçerli:

| varsayım | durum | kanıt |
| --- | --- | --- |
| `dotnet ef migrations bundle` alpine SDK'da çalışır | ✅ | bir hata çıktı, düzeltildi (aşağıda) |
| *(yeni)* bundle Core'u kendi startup project'i olarak üretir | ⬜ | üç uygulamalı sürümle geldi, koşturulmadı |
| `efbundle` (musl, self-contained) `runtime-deps:10.0-alpine`'de koşar | ✅ | dört migration uygulandı, seed satırları yerinde |
| `aspnet:10.0-alpine` imajında `app` kullanıcısı var | ✅ | wallet-service başladı ve istek karşılıyor |
| init script'i tam olarak bir kez koşar | ✅ | roller kuruldu, "role already exists" yok |
| `wallet_app` `ledger_entries`'e yazamaz | ✅ | `permission denied` alındı |

Ölçülen çıktılar:

```
$ curl -s localhost:8091/health/ready
{"status":"Healthy","durationMs":1.3747,"checks":[{"name":"postgres","status":"Healthy",...}]}

$ ... psql -U postgres -c "SELECT type, provider, currency FROM ledger_accounts WHERE account_id IS NULL"
 clearing         | stripe-fake | TRY
 clearing         | bank-fake   | TRY
 nostro           | bank-fake   | TRY
 provider_expense | stripe-fake | TRY
 provider_expense | bank-fake   | TRY
 revenue          |             | TRY

$ ... psql -U wallet_app -c "UPDATE ledger_entries SET amount = amount + 1;"
ERROR:  permission denied for table ledger_entries
```

Sağlık ucu Tailscale üzerinden dışarıdan da doğrulandı (`http://homelab:8091`).

### Hâlâ doğrulanmadı

Üç uygulamalı sürümün tamamı bu listede — hiç koşturulmadı.

| ne | nasıl bakılır |
| --- | --- |
| `rabbitmq` ayağa kalkıyor ve eklenti yükleniyor mu | `docker compose logs rabbitmq \| grep consistent_hash` |
| `topup-migrator` inbox şemasını uyguluyor mu | `docker compose ps -a topup-migrator` — `exited (0)` |
| `wallet-consumer` ayağa kalkıyor mu (host'a portu yok) | `docker compose ps wallet-consumer` — `healthy` |
| `withdrawal-migrator` ve `bank-migrator` şemaları uyguluyor mu | `docker compose ps -a` — ikisi de `exited (0)` |
| `withdrawal-orchestrator` broker'sız ayağa kalkıyor mu | `curl localhost:8093/health/ready` — `Degraded` beklenir, `Unhealthy` değil |
| `bank-service` ayağa kalkıyor mu | `curl localhost:8094/health/ready` |
| servis sınırı kapalı mı | `psql -U withdrawal_app -d hiwallet_wallet` — reddedilmeli |
| çekim akışının tamamı | aşağıdaki adım |
| `wallet-api` broker'sız da sağlıklı mı | `curl localhost:8091/health/ready` — çıktıda `rabbitmq` OLMAMALI |
| compose healthcheck'i (alpine'de `wget` var mı) | `docker compose ps` — servisler `healthy` mi |
| top-up hattının tamamı | aşağıdaki adım |
| konteynerlenmiş uygulamadan uçtan uca transfer | hesap/cüzdan endpoint'i yok; cüzdanları DB'den kurmak gerekiyor |

### Top-up hattını doğrulama

Cüzdan kurulduktan sonra (transfer doğrulamasındaki `psql` komutu), webhook'u imzalayıp
gönder:

```bash
BODY='{"eventId":"evt_manuel_1","walletId":"<CUZDAN_ID>","amount":100.00,"currency":"TRY","reference":"pi_1","occurredAt":"2026-03-01T10:00:00+00:00"}'
SIG=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$STRIPE_FAKE_WEBHOOK_SECRET" -hex | awk '{print $2}')
curl -s -X POST http://localhost:8092/v1/webhooks/topup/stripe-fake -H 'Content-Type: application/json' -H "X-Hive-Signature: sha256=$SIG" --data "$BODY"
```

Beklenen: `202 Accepted` + `{"accepted":true,"duplicate":false}`.

Birkaç saniye sonra bakiye artmış olmalı:

```bash
docker compose exec postgres psql -U postgres -d hiwallet_wallet -c "SELECT balance FROM ledger_balances WHERE ledger_account_id = '<CUZDAN_ID>';"
```

Aynı komutu ikinci kez çalıştır: yine `202`, ama `"duplicate":true` ve bakiye
DEĞİŞMEMELİ.

İmzayı bozup dene (`SIG` sonuna bir karakter ekle): `401` dönmeli ve inbox'a hiçbir şey
yazılmamalı:

```bash
docker compose exec postgres psql -U topup_app -d hiwallet_topup -c "SELECT event_id, published_at, publish_attempts FROM topup_inbox ORDER BY received_at;"
```

## Host'ta .NET gerekmiyor

`docker compose up --build` için host'un .NET sürümü kullanılmıyor; SDK ve runtime
imajın içinden geliyor. Homelab'da .NET 9 olması sorun değil, yalnızca Docker yeterli.
Doğrudan `dotnet test` / `dotnet run` çalıştıracaksan .NET 10 SDK gerekir.

## Çözülmüş hatalar

Doğrulama sırasında çıkıp düzeltilenler, tekrar görülürse diye:

**`PRECONDITION_FAILED - unknown exchange type 'x-consistent-hash'`.** Topoloji bu
exchange tipine dayanıyor ama o RabbitMQ çekirdeğinde değil, eklentiyle geliyor ve
varsayılan olarak KAPALI. Compose'daki broker `docker/rabbitmq/enabled_plugins` ile
açık geliyor; mevcut bir broker'a karşı koşturacaksan elle açman gerekiyor:

```bash
docker exec <rabbitmq> rabbitmq-plugins enable rabbitmq_consistent_hash_exchange
```

Yeniden başlatma gerekmiyor. Testlerin atlama koşulu artık bunu da kontrol ediyor —
önce yalnızca bağlantıya bakıyordu ve eklenti yokken testler atlanmak yerine bu
hatayla düşüyordu.

**`Unable to create a 'DbContext' ... ConnectionStrings__WalletOwner ortamda yok`**
build sırasında. Design-time factory bağlantı dizesini ZORUNLU tutuyordu; oysa
`migrations bundle` yalnızca modele bakıyor, hiçbir yere bağlanmıyor ve build sırasında
ortamda `.env` yok. Factory artık yer tutucu bir dizeye düşüyor. `database update`
gerçekten bağlandığı için orada hâlâ gerçek bir dize gerekiyor — yer tutucunun host adı
(`connection-string-not-configured`) hatayı kendi kendini açıklar yapıyor.
