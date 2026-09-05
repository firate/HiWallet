# Compose'u doğrulama

Bu dosya `docker compose` kurulumunun gerçekten çalıştığını kanıtlamak için var.
Geliştirme makinesinde Docker yok, o yüzden koşturmak elle yapılıyor.

> **Şu anki stack DOĞRULANMADI.** Homelab'da koşturulan sürüm iki uygulamalıydı
> (wallet-service + topup-webhook, RabbitMQ yok). Bugün üç uygulama, bir broker ve
> ikinci bir veritabanı var. Aşağıdaki "Doğrulama kaydı" o eski koşuya ait ve hâlâ
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

RabbitMq__Username=...          # compose'daki broker'ın ilk kullanıcısı olur
RabbitMq__Password=...

STRIPE_FAKE_WEBHOOK_SECRET=...  # uzun ve rastgele
BANK_FAKE_WEBHOOK_SECRET=...
```

Portların varsayılanı **homelab'a göre** seçildi, dokunmana gerek yok:

```
WALLET_HOST_PORT=8091        # 8080 Keycloak'ta, 8090 dolu
TOPUP_HOST_PORT=8092
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

Beklenen sıra: `postgres` sağlıklı olur → `migrator` ve `topup-migrator` şemaları
uygulayıp `exit 0` ile biter → `wallet-api`, `topup-webhook` ve `topup-consumer`
başlar. `rabbitmq` paralel kalkar; hiçbiri onu BEKLEMEZ (broker olmadan da ayağa
kalkmalılar).

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
| `topup-consumer` ayağa kalkıyor mu (host'a portu yok) | `docker compose ps topup-consumer` — `healthy` |
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
