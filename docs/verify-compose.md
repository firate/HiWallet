# Compose'u doğrulama

Bu dosya `docker compose` kurulumunun gerçekten çalıştığını kanıtlamak için var.
Kurulum yazıldı ama **hiç koşturulmadı** — geliştirme makinesinde Docker yok.
Docker'ı olan biri aşağıdakileri koşturup sonucu bildirene kadar doğrulanmamış sayılır.

## 1. Kodu Docker'ı olan makineye al

```bash
git clone -b chore/baseline-closeout git@github.com:firate/HiWallet.git && cd HiWallet
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

Homelab'da koşturuyorsan portları da değiştir; **8080 Keycloak'ta, 5432 ana
Postgres'te**:

```
WALLET_HTTP_PORT=8090
POSTGRES_HTTP_PORT=5433
```

## 3. Ayağa kaldır

```bash
docker compose up --build
```

Beklenen sıra: `postgres` sağlıklı olur → `migrator` dört migration'ı uygulayıp
`exit 0` ile biter → `wallet-service` başlar.

## 4. Doğrula

```bash
curl -s localhost:8090/health/ready
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

## Nerelerde patlama bekliyorum

Doğrulanmamış varsayımlar — hata alırsan büyük ihtimalle bunlardan biri:

| varsayım | patlarsa belirtisi |
| --- | --- |
| `dotnet ef migrations bundle` alpine SDK'da çalışır | build `migrator-build` aşamasında durur |
| `efbundle` (musl, self-contained) `runtime-deps:10.0-alpine`'de koşar | migrator container'ı hemen exit 1 |
| `aspnet:10.0-alpine` imajında `app` kullanıcısı var | wallet-service "unable to find user app" |
| alpine'de `wget` var (healthcheck) | wallet-service sürekli `unhealthy` |
| init script'i tam olarak bir kez koşar | postgres log'unda "role already exists" |

Hata çıktısını olduğu gibi paylaş, düzeltilir.
