# wallet-distributed

E-money cüzdan sistemi. Double-entry ledger + saga orchestration.
Detaylı gerekçeler: `docs/decisions.md`. Şema: `docs/ledger-schema.md`.
Dosya yerleşimi ve adlandırma: `docs/structure.md`.

## Pazarlıksız kurallar

**Stack**
- Controller-based ASP.NET Core Web API. Minimal API YOK.
- Wolverine, yalnızca in-process handler/mediator olarak. MediatR YOK.
- Wolverine'in RabbitMQ transport'u, durable inbox/outbox'ı ve `Saga` persistence'ı KULLANILMIYOR.
  Inbox, relay, outbox ve saga state machine elle yazılır.
- FluentValidation. DataAnnotations validation YOK.
- `ILogger<T>` + OpenTelemetry. Serilog YOK.
- EF Core + Npgsql. Migration ile şema versiyonlama.

**Ledger**
- `ledger_entries` append-only. UPDATE ve DELETE YOK — düzeltme ters kayıtla yapılır.
- Her `ledger_transaction` içindeki entry'lerin `amount` toplamı sıfır olmak zorunda.
- İşaret konvansiyonu: credit `+`, debit `-`. Hiçbir yerde tersine çevrilmez.
- Para: `numeric(19,4)` + ayrı `currency` kolonu. `float`/`double` YOK.
- Bakiye asla ledger'a yazmadan güncellenmez.

**Sağlayıcı ücretleri**
- `provider_fees` tablosu ledger DEĞİL. `expected_amount` ledger'a asla yazılmaz.
- Ücret kolonları `ledger_transactions` veya `ledger_entries` üzerine EKLENMEZ.
- `revenue` (gelir) ve `provider_expense` (gider) ayrı hesaplardır, netleştirilmez.
- Compensation'da `revenue` ters kayıtla iade edilir, `provider_expense` edilmez.
- Fatura ile `expected_amount` toplamı tolerans dışı sapıyorsa ledger'a HİÇBİR ŞEY yazılmaz.

**Concurrency**
- Optimistic lock `wallet_balances.version` üzerinde. `ledger_entries` üzerinde lock YOK.
- EF Core'da `IsConcurrencyToken()` yalnızca `WalletBalance` entity'sinde.
- Bir transaction içinde birden fazla `wallet_balances` satırı güncelleniyorsa
  her zaman `account_id` artan sırayla güncellenir (deadlock önleme).
- Redis distributed lock YOK. Background job tekilliği `pg_try_advisory_lock`
  veya `SELECT ... FOR UPDATE SKIP LOCKED` ile çözülür.

**Idempotency**
- Ayrı `idempotency_keys` tablosu YOK.
- `ledger_transactions(account_id, idempotency_key)` UNIQUE.
- `withdrawal_sagas(account_id, idempotency_key)` UNIQUE.
- Kalıp: `INSERT ... ON CONFLICT DO NOTHING`, 0 satır ise mevcut kaydı oku ve onu dön.
  "Önce SELECT sonra INSERT" YOK.

**API**
- `/v1` prefix. Liste endpoint'lerinde pagination, unbounded query YOK.
- Hata gövdesi RFC 7807 ProblemDetails.
- Yetersiz bakiye / limit aşımı → `422`. Concurrency çakışması → `409`. İkisi karıştırılmaz.

**Servis sınırı**
- wallet-service ve withdrawal-orchestrator ayrı veritabanı (en azından ayrı schema).
- Orchestrator wallet tablolarına doğrudan yazmaz, yalnızca komut gönderir.

## Çalışma tarzı

- Yeni bir katman/akış eklerken önce testi yaz, sonra implementasyonu.
- Bir kural burada yazılıysa gerekçesini tartışma, uygula. Kural eksikse
  `docs/decisions.md`'ye bak; orada da yoksa varsayımını kodda yorum olarak belirt.
- Kapsam dışı: Vault, Kubernetes, gerçek ödeme sağlayıcısı, multi-tenancy, caching.