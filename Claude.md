# HiWallet

E-money cüzdan sistemi. Double-entry ledger + saga orchestration.
Marka Hive, ürün HiWallet; `wallet-distributed` konsept dokümanlarının adıdır, kodda geçmez.
Detaylı gerekçeler: `docs/decisions.md`. Şema: `docs/ledger-schema.md`.
Dosya yerleşimi ve adlandırma: `docs/structure.md`.

## Pazarlıksız kurallar

**Stack**
- `net10.0`, tek TFM. `TargetFramework` yalnızca `Directory.Build.props`'ta.
- Paket versiyonları `Directory.Packages.props`'ta. `.csproj`'da `Version` attribute'u YOK.
- Assembly ve namespace kökü `HiWallet.*`.
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
- İki seviye: `accounts` müşteri hesabı, `ledger_accounts` bakiye tutabilen her şey
  (cüzdanlar + sistem hesapları). Cüzdan = `ledger_accounts.type = 'user_wallet'`.
  Ledger tarafı ayrı tablolara BÖLÜNMEZ — `ledger_entries` tek FK hedefi istiyor.
- Sistem hesapları `ledger_accounts.provider` ile ayrışır (`clearing`, `nostro`,
  `provider_expense`). Cüzdanda `provider` NULL, sistem hesabında `account_id`/`name` NULL.
- Bir hesabın aynı para biriminde birden fazla cüzdanı olabilir; `(account_id, currency)`
  UNIQUE YOK. Bu yüzden **günlük limit hesap bazında uygulanır, cüzdan bazında DEĞİL** —
  aksi halde ikinci cüzdan açarak aşılır.
- person/business yalnızca `accounts.type`'ta durur, cüzdana kopyalanmaz.
- `ledger_transactions.account_id` NOT NULL — iç işlemlerde de dolar (idempotency kapsamı).
  Nullable YAPILMAZ: unique index'te NULL'lar eşleşmez, fatura iki kez yazılır.

**Sağlayıcı ücretleri**
- `provider_fees` tablosu ledger DEĞİL. `expected_amount` ledger'a asla yazılmaz.
- Ücret kolonları `ledger_transactions` veya `ledger_entries` üzerine EKLENMEZ.
- `revenue` (gelir) ve `provider_expense` (gider) ayrı hesaplardır, netleştirilmez.
- Compensation'da `revenue` ters kayıtla iade edilir, `provider_expense` edilmez.
  `revenue` iadesi KOŞULSUZ — konfigüre edilmez, atlanamaz. Başarısız denemenin
  sağlayıcı ücretini kimin yüklendiği ise sağlayıcı bazında konfigüre edilir:
  `FeeOnFailure: Charged | Waived`. `Charged`'da `provider_fees` satırı denemeye
  bağlı yazılır, başarıya değil.
- Fatura ile `expected_amount` toplamı tolerans dışı sapıyorsa ledger'a HİÇBİR ŞEY yazılmaz.

**Concurrency**
- wallet-service'te optimistic lock `ledger_balances.version` üzerinde.
  `ledger_entries` üzerinde lock YOK.
- `IsConcurrencyToken()` yalnızca GERÇEKTEN UPDATE edilen ve birden fazla yazarı olan
  satırlarda: wallet'ta `LedgerBalance`, orchestrator'da `WithdrawalSaga`. Başka
  entity'ye EKLENMEZ — append-only tabloda anlamsız (`decisions.md` madde 2).
- Bir transaction içinde birden fazla `ledger_balances` satırı güncelleniyorsa
  her zaman `account_id` artan sırayla güncellenir (deadlock önleme).
- Redis distributed lock YOK. Background job tekilliği `pg_try_advisory_lock`
  veya `SELECT ... FOR UPDATE SKIP LOCKED` ile çözülür.

**Idempotency**
- Ayrı `idempotency_keys` tablosu YOK.
- `ledger_transactions(account_id, idempotency_key)` UNIQUE.
- `withdrawal_sagas(account_id, idempotency_key)` UNIQUE.
- Kalıp: `INSERT ... ON CONFLICT DO NOTHING`, 0 satır ise mevcut kaydı oku ve onu dön.
  "Önce SELECT sonra INSERT" YOK.

**Deployable'lar**
- Üç uygulama, üç erişim seviyesi (`decisions.md` madde 28):
  `wallet-api` public, `topup-webhook` IP kısıtlı, `topup-consumer` ingress'siz.
  Farklı ağ maruziyeti aynı process'te BİRLEŞTİRİLMEZ.
- `wallet-api` ve `topup-consumer` ortak kütüphane `WalletService.Core` üstünde.
  Ledger'a yazan kodun tek kopyası orada; ikinci bir kopya AÇILMAZ (madde 25).
- **`WalletService.Core`'a wallet sınırı dışından referans verilmez.**
  `topup-webhook` onu görmez.
- `wallet-api`'nin RabbitMQ bağımlılığı YOK ve eklenmez.

**Top-up hattı**
- `topup-webhook` AYRI servis, AYRI veritabanı (`hiwallet_topup`), TEK rol —
  append-only zorlanacak tablosu yok.
- İmza: HAM gövde baytları üzerinde HMAC-SHA256, sabit zamanlı karşılaştırma.
  Gövde parse EDİLMEDEN önce doğrulanır. Geçersiz imza, eksik başlık ve tanınmayan
  sağlayıcı → `401`. Tanınmayan sağlayıcıya `404` DÖNÜLMEZ.
- Yanıt `202 Accepted`, `200` DEĞİL: verilen söz "işledim" değil "kalıcı kaydettim"
  (`decisions.md` madde 29). Ve ancak inbox commit'inden SONRA. Tekrar eden event de
  `202` — sağlayıcı için yeniden gönderim başarılı sonuçtur, ayrım gövdedeki
  `duplicate` alanında.
- İki kademe idempotency: inbox `(provider, event_id)` UNIQUE + tüketicide
  `processed_events`. Tüketicide kapı ile ledger AYNI transaction'da.
- Top-up'ta `ledger_transactions.idempotency_key` = `provider:event_id`
  (`decisions.md` madde 27). Yalnız `event_id` YAZILMAZ.
- Kalıcı hata (cüzdan yok, currency uyuşmuyor, tutar geçersiz) → dead-letter.
  Geçici hata (DB kapalı) → requeue. İkisi karıştırılmaz: kalıcı hatayı requeue etmek
  partition'ı süresiz tıkar.
- Routing key = cüzdan id, `x-consistent-hash` exchange, kuyruklarda
  `x-single-active-consumer`, tüketicide `prefetch=1`.
- Relay: `FOR UPDATE SKIP LOCKED` + publisher confirms. Önce publish, sonra işaretle —
  ters sıra kayıp üretir.

**Withdrawal saga**
- Saga state machine SAF: DB, mesajlaşma ve zaman bilmez. "Şimdi"yi çağıran verir.
- Geçişler üç sonuç döner: `Applied` / `Ignored` / `Conflict`. Zararsız tekrar ile
  para kaybına işaret eden çelişki AYNI kefeye konmaz (`decisions.md` madde 31).
  `Conflict`'te saga durumu DEĞİŞMEZ, alarm üretilir.
- Orchestrator'da outbox: saga geçişi ile komut gönderimi AYNI transaction'da
  (`decisions.md` madde 32). Broker'a taşımak relay'in işi.
- `withdrawal_outbox.id` AYNI ZAMANDA komutun `CommandId`'si. İkinci bir yüzey id
  üretilmez: relay aynı satırı iki kez yayınladığında alıcıya giden `CommandId` de
  aynı kalmak zorunda, yoksa tekrar deduplike edilemez.
- Komutu tüketen tarafta `CommandId` + `processed_messages`. Orchestrator'ın event
  tüketiminde ayrı tablo YOK — saga durumu zaten cevabı taşıyor.
- Orchestrator wallet'ın `Money`/`Currency` tiplerini KULLANMAZ; `decimal` +
  `string currency`. Komisyon ve limit wallet'ın bilgisi, komutta taşınmaz.
- `RefundWithdrawal` tutar taşımaz: ters kayıt orijinalin aynası ve onu wallet yazdı.
- Ters kayıt ÜÇ bacaklı: cüzdan, clearing, `revenue`. `revenue` bacağı atlanırsa
  kayıt yine dengeli olur ve trigger susar — ama müşteri gerçekleşmemiş işlemin
  komisyonunu ödemiş kalır. Bacak opsiyonel DEĞİL.
- IBAN sınırda mod-97 ile doğrulanır ve `Iban` tipine dönüşür. Bu kontrol
  "komisyon koşulsuz iade edilir" kuralının taşıyıcısı; zayıflatılamaz.
  Sınırdan sonra akışta string IBAN DOLAŞMAZ. Yanıtta maskeli döner.
- `POST /v1/withdrawals`'ta `Idempotency-Key` ZORUNLU — transfer'dekinin aksine
  opsiyonel DEĞİL. Çekim çok adımlı ve dışarıya para çıkarıyor; anahtarsız bir
  tekrar ikinci bir banka transferi başlatırdı.
- Yanıt `202`: dönüldüğünde hiçbir para hareket etmedi. Tekrar eden istek de `202`,
  ayrım gövdedeki `replayed` alanında.

**API**
- `/v1` prefix. Liste endpoint'lerinde pagination, unbounded query YOK.
- Hata gövdesi RFC 7807 ProblemDetails.
- Yetersiz bakiye / limit aşımı → `422`. Concurrency çakışması → `409`. İkisi karıştırılmaz.

**Servis sınırı**
- wallet-service ve withdrawal-orchestrator ayrı veritabanı (en azından ayrı schema).
- Orchestrator wallet tablolarına doğrudan yazmaz, yalnızca komut gönderir.
- Bu ayrımın bedeli iki veritabanı arasında ayrışma ihtimali; karşılığı takılmış saga
  taraması. O tarama opsiyonel bir iyileştirme DEĞİL, bu kararın zorunlu tamamlayıcısı
  (`decisions.md` madde 33).

## Çalışma tarzı

- Yeni bir katman/akış eklerken önce testi yaz, sonra implementasyonu.
- Bir kural burada yazılıysa gerekçesini tartışma, uygula. Kural eksikse
  `docs/decisions.md`'ye bak; orada da yoksa varsayımını kodda yorum olarak belirt.
- **Doküman koda uyar, kod dokümana değil.** `docs/` bilinçli olarak sade ve hafif
  yazıldı; kod onun ilerisine geçebilir. Kod ile doküman çeliştiğinde önce kodu doğru
  yaz, sonra dokümanı ona güncelle — dokümanı korumak için kodda taviz verme.
  İstisna: bu dosyadaki pazarlıksız kurallar ve `decisions.md`'deki kararlar.
  Onlardan sapılacaksa önce karar değiştirilir, gerekçesiyle.
- Kapsam dışı: Vault, Kubernetes, gerçek ödeme sağlayıcısı, multi-tenancy, caching.