# Compose'u doğrulama

Bu dosya `docker compose` kurulumunun gerçekten çalıştığını kanıtlamak için var.
Geliştirme makinesinde Docker yok; stack **homelab'da koşturuldu ve aşağıdaki
adımların tamamı doğrulandı** (bkz. "Doğrulama kaydı"). Başka bir makinede
tekrarlamak için adımlar olduğu gibi duruyor.

## 1. Kodu Docker'ı olan makineye al

```bash
git clone git@github.com:firate/HiWallet.git && cd HiWallet
```

## 2. `.env` hazırla

```bash
cp .env.example .env
```

Doldurulması ZORUNLU üç değer — gerisi compose için gerekmiyor:

```
POSTGRES_PASSWORD=...
WALLET_OWNER_PASSWORD=...
WALLET_APP_PASSWORD=...
```

Portların varsayılanı **homelab'a göre** seçildi, dokunmana gerek yok:

```
WALLET_HOST_PORT=8091      # 8080 Keycloak'ta, 8090 dolu
POSTGRES_HOST_PORT=5433    # 5432 ana Postgres'te
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

Beklenen sıra: `postgres` sağlıklı olur → `migrator` dört migration'ı uygulayıp
`exit 0` ile biter → `wallet-service` başlar.

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

## Doğrulama kaydı

Homelab'da (`docker compose up --build`) koşturuldu. Riskli görülen varsayımların
her biri ve nasıl kanıtlandığı:

| varsayım | durum | kanıt |
| --- | --- | --- |
| `dotnet ef migrations bundle` alpine SDK'da çalışır | ✅ | bir hata çıktı, düzeltildi (aşağıda) |
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

| ne | nasıl bakılır |
| --- | --- |
| compose healthcheck'i (alpine'de `wget` var mı) | `docker compose ps` — `wallet-service` `healthy` mi, `unhealthy` mi |
| konteynerlenmiş uygulamadan uçtan uca transfer | hesap/cüzdan endpoint'i yok; cüzdanları DB'den kurmak gerekiyor |

## Host'ta .NET gerekmiyor

`docker compose up --build` için host'un .NET sürümü kullanılmıyor; SDK ve runtime
imajın içinden geliyor. Homelab'da .NET 9 olması sorun değil, yalnızca Docker yeterli.
Doğrudan `dotnet test` / `dotnet run` çalıştıracaksan .NET 10 SDK gerekir.

## Çözülmüş hatalar

Doğrulama sırasında çıkıp düzeltilenler, tekrar görülürse diye:

**`Unable to create a 'DbContext' ... ConnectionStrings__WalletOwner ortamda yok`**
build sırasında. Design-time factory bağlantı dizesini ZORUNLU tutuyordu; oysa
`migrations bundle` yalnızca modele bakıyor, hiçbir yere bağlanmıyor ve build sırasında
ortamda `.env` yok. Factory artık yer tutucu bir dizeye düşüyor. `database update`
gerçekten bağlandığı için orada hâlâ gerçek bir dize gerekiyor — yer tutucunun host adı
(`connection-string-not-configured`) hatayı kendi kendini açıklar yapıyor.
