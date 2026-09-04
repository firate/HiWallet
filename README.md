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

**Kenar (eventual).** Dış dünyayla konuşan akışlar — top-up ve withdrawal. Asenkron,
idempotent, gerektiğinde compensation'lı. *Henüz yazılmadı, sırada.*

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
| Top-up hattı (webhook → inbox → relay → consumer) | ⬜ adım 3 |
| Withdrawal saga + compensation | ⬜ adım 4 |
| Scheduled job'lar (mutabakat, özet, stuck saga) | ⬜ adım 5 |

72 test: 44 unit (DB'siz), 28 integration (gerçek Postgres).

## Çalıştırma

```bash
cp .env.example .env      # <DOLDUR> yazan yerleri doldur
docker compose up --build
```

Sırayla: Postgres ayağa kalkar ve roller kurulur → `migrator` şemayı uygular →
`wallet-service` başlar.

```bash
curl http://localhost:8080/health/ready
```

Swagger: <http://localhost:8080/swagger> (Development'ta).

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
curl -X POST http://localhost:8080/v1/transfers \
  -H 'Content-Type: application/json' \
  -d '{"fromWalletId":"...","toWalletId":"...","amount":200,"currency":"TRY","type":"Payment"}'
```

Ledger'a üç satır düşer: gönderen `-204`, alan `+200`, `revenue` `+4`. Toplam sıfır.

**Idempotency.** Aynı `Idempotency-Key` ile ikinci istek yeni transfer yapmaz:

```bash
curl -X POST http://localhost:8080/v1/transfers \
  -H 'Idempotency-Key: ayni-istek' -H 'Content-Type: application/json' -d '{...}'
```

İkinci yanıt aynı `transactionId` ve `"replayed": true` döner.

**Yetersiz bakiye / limit aşımı** → `422` + `rule` alanı.
**Concurrency çakışması** (retry tükendi) → `409`. İkisi karıştırılmaz.

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
gerekmiyor.

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
| [CLAUDE.md](CLAUDE.md) | pazarlıksız kurallar |

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
