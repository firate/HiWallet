# Compose'u doğrulama

Bu dosya `docker compose` kurulumunun gerçekten çalıştığını kanıtlamak için var.
Geliştirme makinesinde Docker yok, o yüzden koşturmak elle yapılıyor.

> **Beş uygulamalı stack ayağa kalkıyor, yapısal kontrolleri ve uçtan uca akışları
> geçiyor.** Top-up, çekimin mutlu yolu ve telafi yolu compose üzerinde
> doğrulandı. Açık kalanlar aşağıdaki "Hâlâ doğrulanmadı" listesinde.

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

Portların varsayılanı alışıldık portlardan bilerek kaçıyor, dokunmana gerek yok:

```
WALLET_HOST_PORT=8091        # 8080/8090 çoğu makinede dolu
TOPUP_HOST_PORT=8092
WITHDRAWAL_HOST_PORT=8093
BANK_HOST_PORT=8094          # sahte bankanın senaryo ucu
POSTGRES_HOST_PORT=5433      # 5432 mevcut Postgres'te olabilir
RABBITMQ_HOST_PORT=5673      # 5672 mevcut broker'da olabilir
RABBITMQ_MGMT_HOST_PORT=15673
```

Telemetriyi bir OTel Collector'ına göndereceksen `.env`'de şunu değiştir —
Collector container'ların dışında, Docker host tarafında çalışıyorsa:

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

Beklenen: bağlantı reddedilir (`FATAL: permission denied for database "hiwallet_wallet"`).
Bağlanabiliyorsa saga'nın anlamı kalmaz — orchestrator er ya da geç doğrudan yazmaya
başlar ve compensation gereksizleşir (decisions.md madde 7).

Tek yön yetmiyor; her rol yalnızca kendi veritabanını görmeli:

```bash
for role in wallet_app topup_app withdrawal_app bank_app; do
  for db in hiwallet_wallet hiwallet_topup hiwallet_withdrawal hiwallet_bank; do
    if docker compose exec -T postgres psql -U "$role" -d "$db" -c 'SELECT 1' >/dev/null 2>&1
      then echo "BAĞLANDI  $role -> $db"
      else echo "reddedildi $role -> $db"
    fi
  done
done
```

Beklenen tam olarak dört `BAĞLANDI`: her rol kendi veritabanına. On iki satır
`reddedildi` olmalı. Fazladan bir `BAĞLANDI` varsa `postgres-init.sql`'deki
`REVOKE CONNECT ON DATABASE ... FROM PUBLIC` satırlarından biri eksik demektir —
PostgreSQL `CONNECT`'i yeni veritabanlarında varsayılan olarak `PUBLIC`'e verir,
geri alınmazsa sınır yalnızca kâğıt üstünde kalır.

### Hesap ve cüzdan kurma

Aşağıdaki iki akış da bir cüzdan istiyor. `jq` ile kimlikleri kabuk değişkenine al:

```bash
ACCOUNT=$(curl -s -X POST localhost:8091/v1/accounts \
  -H 'Content-Type: application/json' -d '{"type":"Person"}' | jq -r .accountId)

WALLET=$(curl -s -X POST localhost:8091/v1/accounts/$ACCOUNT/wallets \
  -H 'Content-Type: application/json' -d '{"name":"Birikim","currency":"TRY"}' | jq -r .walletId)

echo "$ACCOUNT / $WALLET"
```

Cüzdan sıfır bakiyeyle açılır; para aşağıdaki top-up akışıyla girer. Doğrudan
bakiyeye yazan bir uç YOK — olsaydı zero-sum invariant'ı delerdi.

### Çekim akışını uçtan uca koşturma

Cüzdanda para olduktan sonra:

```bash
curl -i -X POST localhost:8093/v1/withdrawals \
  -H 'Idempotency-Key: cekim-1' -H 'Content-Type: application/json' \
  -d "{\"accountId\":\"$ACCOUNT\",\"walletId\":\"$WALLET\",\"amount\":100,\"currency\":\"TRY\",
       \"destinationIban\":\"TR330006100519786457841326\"}"
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

`docker compose up --build` ile koşturuldu. O koşuda kanıtlananlar — imaj ve şema
tarafı değişmediği için hâlâ geçerli:

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

Sağlık ucu, Docker host'unun dışındaki bir makineden de doğrulandı.

## Doğrulama kaydı (beş uygulamalı sürüm)

`docker compose down -v --remove-orphans && docker compose up --build -d` ile
koşturuldu. Stack ayağa kalktı.

| varsayım | durum | kanıt |
| --- | --- | --- |
| init beş rolü ve dört veritabanını kurar | ✅ | `\du`: `wallet_owner`, `wallet_app`, `topup_app`, `withdrawal_app`, `bank_app` |
| dört migrator da şemasını uygular | ✅ | dördü de `Done`, `exited (0)` |
| bundle Core'u kendi startup project'i olarak üretir | ✅ | wallet migrator yedi migration'ı uyguladı |
| beş uygulama ayağa kalkar | ✅ | `docker compose ps`: beşi de `healthy` |
| `wallet-consumer` host portu olmadan da sağlıklı | ✅ | `healthy`, yalnızca `8080/tcp` |
| compose healthcheck'i alpine'de çalışır | ✅ | beş uygulama + iki altyapı `healthy` |
| `wallet-api`'nin RabbitMQ bağımlılığı yok | ✅ | `health/ready` çıktısında yalnızca `postgres` check'i var |
| sistem hesapları seed edilir | ✅ | altı satır |
| `wallet_app` `ledger_entries`'e yazamaz | ✅ | `permission denied for table ledger_entries` |
| **servis sınırı kapalı** | ✅ | ilk koşuda DÜŞTÜ, düzeltildi, ikinci koşuda geçti (aşağıda) |

**İlk koşuda düşen kontrol.** `withdrawal_app` `hiwallet_wallet`'a bağlanabiliyordu.
PostgreSQL `CONNECT`'i yeni veritabanlarında varsayılan olarak `PUBLIC`'e veriyor,
`postgres-init.sql` yalnızca `CREATE ON SCHEMA public`'i geri alıyordu.
`GRANT CONNECT ... TO wallet_app` satırı zaten vardı ama karşılığı olan `REVOKE`
hiç yazılmamıştı — o GRANT o güne kadar hiçbir şey yapmıyordu.

Veri sızmıyordu: tablo yetkileri role İSİMLE veriliyor, `PUBLIC`'e değil. Görünen
şey şemaydı — sistem kataloglarından tablo, kolon ve constraint adları.

`REVOKE CONNECT ON DATABASE ... FROM PUBLIC` eklendikten ve `down -v` ile yeniden
kurulduktan sonra matris tam köşegen:

```
BAĞLANDI  wallet_app -> hiwallet_wallet          reddedildi wallet_app -> hiwallet_topup
BAĞLANDI  topup_app -> hiwallet_topup            reddedildi topup_app -> hiwallet_wallet
BAĞLANDI  withdrawal_app -> hiwallet_withdrawal  reddedildi withdrawal_app -> hiwallet_wallet
BAĞLANDI  bank_app -> hiwallet_bank              reddedildi bank_app -> hiwallet_wallet
```

Dört bağlantı, on iki ret. Uygulamalar `REVOKE` sonrası da `healthy` — veritabanı
sahiplerinin yetkisi örtük olduğu için migrator'lar, açık `GRANT`'i olduğu için
`wallet_app` etkilenmedi.

Ders: kontrolü yazmak yetmiyor. Bu satır dokümanda "reddedilmeli" diye üç dal
boyunca durdu ve kimse koşturmadığı için yanlış varsayımla ilerlendi.

### Akışların doğrulama kaydı

Beş uygulamalı stack üzerinde, gerçek broker ve gerçek Postgres ile koşturuldu.

| varsayım | durum | kanıt |
| --- | --- | --- |
| `rabbitmq` eklentisi yükleniyor | ✅ | `rabbit_exchange_type_consistent_hash_registry` boot adımı |
| `withdrawal-orchestrator` broker'SIZ ayakta kalıyor | ✅ | `stop rabbitmq` sonrası `Degraded`, `Unhealthy` değil |
| top-up hattının tamamı | ✅ | webhook `202` → relay → broker → tüketici → bakiye `500` |
| çekim mutlu yolu | ✅ | saga `settling` üzerinden `completed`, cüzdan `398` |
| çekim settlement'ı ledger'a düşüyor | ✅ | `clearing -100`, `nostro +100`; `Invoiced` modelde gider bacağı YOK |
| **telafi yolu** | ✅ | saga `failed`, bakiye `500` → `500`, altı satır |
| ters kaydın `revenue` bacağı | ✅ | `refund / revenue / -2.0000` |
| beş migration otomatik uygulanıyor | ✅ | dört migrator, `down -v` gerekmedi |

Telafi ölçümü:

```
    type    |    hesap    |  amount
------------+-------------+-----------
 withdrawal | user_wallet | -102.0000
 withdrawal | revenue     |    2.0000
 withdrawal | clearing    |  100.0000
 refund     | clearing    | -100.0000
 refund     | revenue     |   -2.0000
 refund     | user_wallet |  102.0000
```

`refund / revenue / -2.0000` bu tablonun en kritik satırı. O bacak yazılmasaydı kayıt
YİNE dengeli olurdu, trigger susardı ve testler geçerdi — ama müşteri gerçekleşmemiş
bir işlemin komisyonunu ödemiş kalırdı. Ters kayıt bu yüzden politikadan yeniden
üretilmiyor, orijinalin bacakları okunup negatifleniyor.

Mutlu yol ölçümü — `withdrawal` üçlüsü artı `settlement` ikilisi:

```
 withdrawal | user_wallet |           | -102.0000
 withdrawal | revenue     |           |    2.0000
 withdrawal | clearing    | bank-fake |  100.0000
 settlement | clearing    | bank-fake | -100.0000
 settlement | nostro      | bank-fake |  100.0000
```

`provider_expense` bacağı YOK, olmamalı: banka `Invoiced` modelde ve ücreti dönem
sonu faturasıyla alıyor (`decisions.md` madde 10). Ücret `provider_fees`'te
`actual_amount = NULL` ile faturayı bekliyor.

### Hâlâ doğrulanmadı

| ne | nasıl bakılır |
| --- | --- |
| konteynerlenmiş uygulamadan uçtan uca transfer | yukarıdaki "Hesap ve cüzdan kurma" ile iki cüzdan aç, birine top-up yap, transfer et |
| settlement ve fatura uçları (5.5–5.6) | webhook'lar yazıldı ve testlerde yeşil; compose'dan hiç çağrılmadı |
| scheduled job'lar (5.1–5.3, 5.7) | ilk turları dakikalar/saatler sonra koşuyor; log'dan izlenir |

### Top-up hattını doğrulama

Cüzdan kurulduktan sonra ("Hesap ve cüzdan kurma"), webhook'u imzalayıp gönder.
Secret `.env`'den geliyor: `set -a; . ./.env; set +a`.

```bash
BODY="{\"eventId\":\"evt_manuel_1\",\"walletId\":\"$WALLET\",\"amount\":100.00,\"currency\":\"TRY\",\"reference\":\"pi_1\",\"occurredAt\":\"2026-03-01T10:00:00+00:00\"}"
SIG=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$STRIPE_FAKE_WEBHOOK_SECRET" -hex | awk '{print $2}')
curl -s -X POST http://localhost:8092/v1/webhooks/topup/stripe-fake -H 'Content-Type: application/json' -H "X-Hive-Signature: sha256=$SIG" --data "$BODY"
```

Paranın gerçekten geldiğini `psql` yerine uçtan görebilirsin — hat asenkron,
birkaç saniye sürebilir:

```bash
curl -s localhost:8091/v1/wallets/$WALLET
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
imajın içinden geliyor. Host'ta .NET 9 olması sorun değil, yalnızca Docker yeterli.
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
