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
- Her `ledger_transactions` satırı AKTÖR taşır: `actor_type` + `actor_id`, ikisi de
  NOT NULL ve kolon varsayılanı YOK (`decisions.md` madde 34). `LedgerTransaction.Create`'te
  aktörün varsayılanı da YOK — her yazma yolu kökenini beyan etmek zorunda.
  Ölçüt taşıma değil BAŞLATMA: kaydı hangi yol getirdi değil, hareketi kim başlattı.
  Kuyruktan gelen bir komut aktörü `system` yapmaz; müşterinin başlattığı çekimin
  düşme kaydı `customer`, saga'nın kendi kararıyla ürettiği iade `system`.
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

**Promo** (`decisions.md` madde 37)
- Her yükleme bir parti: `promo_grants` satırı. Parti UPDATE EDİLMEZ; harcama ve süre
  sonu `promo_consumptions`'a satır ekler. Kalan = tutar − tüketimler.
- Cüzdanın `promo` bakiyesi partilerin kalanlarının toplamına eşit. Parti ve tüketim,
  cüzdanın `promo` `ledger_balances` satırıyla AYNI transaction'da yazılır.
- Promo yalnızca `Payment`'ta harcanır ve yalnızca tutarı karşılar, komisyonu değil.
  İşyerine `cash` olarak geçer — madde 36'daki "tip korunur" kuralının tek istisnası.
- Parti sırası sabit: bitişi en yakın önce (süresiz en sonda), sonra kısıtlı kapsam,
  sonra eski. Konfigüre EDİLMEZ.
- Kapsam yükleme anında partiye yazılır; kampanyanın sonraki değişikliği verilmiş
  partiyi etkilemez.
- İşyeri promo'yu yalnızca `cash` kovasından fonlar ve yalnızca kendisi için verir.
- Platform fonlu parti yalnızca `accepts_promo` işaretli işyerinde geçer.
- Süre sonu kaydının bacakları partinin fonlayanından okunur: platform fonlu kalan
  `promo_breakage`'e, işyeri fonlu kalan işyerinin `cash` kovasına. `promo_expense`'e
  geri YAZILMAZ.
- Kampanyada bütçe, hesap başına günlük tavan ve hesap başına toplam tavan ZORUNLU.
- Kampanya tabanına (eşik toplamı, yüzde ödül) promo payı ve komisyon GİRMEZ.
- Kampanya değerlendirmesi id cursor'ıyla İLERLEMEZ: id sırası commit sırası değil.
  Değerlendirilen ödeme `promo_campaign_evaluations`'a işaretlenir.

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
- `ledger_transactions(account_id, idempotency_key)` UNIQUE. Index PARTIAL DEĞİL:
  `idempotency_key` NOT NULL ve anahtarsız ledger işlemi açılamıyor (madde 4).
  Filtre kalsaydı NULL yazabilen bir yol açıldığında o satırlar dedup'ın dışında
  kalır ve hata da vermezdi.
- `withdrawal_sagas(account_id, idempotency_key)` UNIQUE.
- Para hareketi başlatan HER endpoint'te `Idempotency-Key` başlığı ZORUNLU — transfer dahil.
  Yoksa `400`. Anahtarsız bir tekrar hiçbir constraint'e takılmaz ve çift harcama
  sessizce ledger'a yazılır; append-only olduğu için de geri alınamaz, yalnızca
  ters kayıtla düzeltilir.
- Kalıp: `INSERT ... ON CONFLICT DO NOTHING`, 0 satır ise mevcut kaydı oku ve onu dön.
  "Önce SELECT sonra INSERT" YOK.

**Deployable'lar**
- **`wallet-api` ve `withdrawal-orchestrator` İÇ servis;** istemci onlara doğrudan
  bağlanmaz. Orchestrator kendi sınırı ve kendi veritabanı (madde 7 ve 33).
- Dışarıya açılan her yüzey bir **ön API**. Ön API ihtiyaç doğdukça açılır, kendi
  istemcisine hizmet eder ve ya public ya da yalnızca iç ağdan erişilir:
  `personal-mobile-api`, `business-api` ve `business-web-bff` public, `backoffice-bff`
  iç ağda.
- Tarayıcıdan kullanılan arayüzün ön API'si **BFF**: token sunucuda kalır, tarayıcı
  yalnızca HttpOnly oturum cookie'si taşır. BFF'in adı `-bff` ile biter. Token taşıyan istemci (mobil uygulama,
  sistem entegrasyonu) ile cookie taşıyan arayüz aynı ön API'yi PAYLAŞMAZ.
- **Ön API veritabanına BAĞLANMAZ** ve `WalletService.Core`'a referans vermez. Ledger'a
  giden her istek iç ağdaki `wallet-api`'den geçer. Public process'te `wallet_app`
  parolası durmaz.
- Ingress'i olmayan ve webhook alan deployable'larda ölçüt ERİŞİM SEVİYESİ
  (`decisions.md` madde 28): `topup-webhook` IP kısıtlı, `wallet-consumer` ingress'siz.
  Farklı erişim seviyesi aynı process'te BİRLEŞTİRİLMEZ. Aynı erişim seviyesi ise ayrı
  process'e BÖLÜNMEZ — `wallet-consumer` hem top-up event'lerini hem çekim
  komutlarını dinliyor, ikisi de ingress'siz ve aynı ledger'a yazıyor.
- `wallet-api` ve `wallet-consumer` ortak kütüphane `WalletService.Core` üstünde.
  Ledger'a yazan kodun tek kopyası orada; ikinci bir kopya AÇILMAZ (madde 25).
- **`WalletService.Core`'a wallet sınırı dışından referans verilmez.**
  `topup-webhook` onu görmez.
- `wallet-api`'nin ve ön API'lerin RabbitMQ bağımlılığı YOK ve eklenmez.
- **`.Fake` son eki yalnızca BAŞKA BİR KURUMUN yerine duran servise konur**
  (`decisions.md` madde 35). Kendi yazdığımız ve canlıda da koşacak servis normal ad
  alır — bugün yalnızca testte koşuyor olması son ek sebebi DEĞİL. `bank-adapter`
  bizim, son ek almaz; `Bank.Fake` bankanın API'sinin yerine duruyor, alır.

**Top-up hattı**
- `topup-webhook` AYRI servis, AYRI veritabanı (`hiwallet_topup`), TEK rol —
  append-only zorlanacak tablosu yok.
- İmza: HAM gövde baytları üzerinde HMAC-SHA256, sabit zamanlı karşılaştırma.
  Gövde parse EDİLMEDEN önce doğrulanır. Geçersiz imza, eksik başlık ve tanınmayan
  sağlayıcı → `401`. Tanınmayan sağlayıcıya `404` DÖNÜLMEZ.
- Response `202 Accepted`, `200` DEĞİL: verilen söz "işledim" değil "kalıcı kaydettim"
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
- Top-up relay'i TEK instance: tur `pg_try_advisory_lock` ile korunur
  (`decisions.md` madde 30). İki relay ayrı batch'leri farklı hızda yayınlarsa aynı
  cüzdanın mesajları exchange'e ters sırada varır ve kuyruk içi sıra garantisi bunu
  düzeltmez. `SKIP LOCKED` yine de kalır: biri sıra için, öbürü çift yayın için.
  Withdrawal outbox relay'i kilitlenmez — bir saga'nın aynı anda birden fazla
  bekleyen komutu olamaz.

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
- Komutu tüketen tarafta `CommandId` ile deduplikasyon: wallet'ta
  `processed_messages`, `bank-adapter`'da `bank_transfers` (zaten "ne yaptık" kaydı,
  anahtarı da `CommandId` — ikinci tablo açılmaz). Orchestrator'ın event tüketiminde
  ayrı tablo YOK, saga durumu zaten cevabı taşıyor.
- İki tüketen taraf da verdiği CEVABI saklar. Tekrar teslimde iş ikinci kez
  yapılmaz ama cevap yeniden yayınlanır.
- `bank-adapter`'da geçici hata ile kalıcı hata AYRI: kalıcı hata
  `BankTransferFailed` + ack, geçici hata hiçbir cevap üretmeden requeue. Karışırsa
  her ağ kesintisi müşterinin parasını ileri geri taşır.
- Orchestrator wallet'ın `Money`/`Currency` tiplerini KULLANMAZ; `decimal` +
  `string currency`. Komisyon ve limit wallet'ın bilgisi, komutta taşınmaz.
- `RefundWithdrawal` tutar taşımaz: ters kayıt orijinalin aynası ve onu wallet yazdı.
- Ledger'a yazdıran komutlar AKTÖR taşır (`DebitForWithdrawal`, `RefundWithdrawal`).
  Taşımazsa çalışanın başlattığı bir telafi ledger'a `system` olarak düşer ve kimin
  karar verdiği kalıcı kayıtta kaybolur. Aktör sözleşmede düz string: `Actor` tipi
  fabrikayla korunuyor ve JSON deserializer fabrikaları atlıyor — sözleşme aptal,
  doğrulama sınırda (`Actor.From`).
- Wallet komuttaki aktöre KÖRÜ KÖRÜNE GÜVENMEZ: `customer` iddiası cüzdanın sahibiyle
  eşleşmek zorunda. Ledger'ın sahibi wallet; orchestrator'ın hatası başkasının adına
  kalıcı kayıt yazdıramamalı.
- Ters kayıt ÜÇ bacaklı: cüzdan, clearing, `revenue`. `revenue` bacağı atlanırsa
  kayıt yine dengeli olur ve trigger susar — ama müşteri gerçekleşmemiş işlemin
  komisyonunu ödemiş kalır. Bacak opsiyonel DEĞİL. Bu yüzden ters kayıt
  politikadan yeniden ÜRETİLMEZ: orijinal işlemin bacakları okunup negatiflenir.
- Wallet tarafında sıra: ledger commit → cevabı yayınla → ack. Ters sıra cevabı
  kaybeder ve saga sonsuza kadar bekler. `processed_messages` cevabı da saklar;
  tekrar teslimde ledger'a dokunulmadan aynı cevap yeniden yayınlanır.
- Yetersiz bakiye ve limit aşımı dead-letter DEĞİL: `WithdrawalDebitRejected`
  dönülüp mesaj ack'lenir. Cevapsız kalan saga müşteriyi sonsuza kadar
  "işleniyor"da bırakır.
- Çekim tarifesi (`Withdrawals` bölümü) yalnızca `wallet-consumer`'da. Bölüm
  eksikse uygulama AÇILMAZ — sessizce komisyonsuz/limitsiz çalışmaz.
- Çekim günlük limit sayımı iadeleri DÜŞER: geri dönen para hesaptan çıkmadı.
- IBAN sınırda mod-97 ile doğrulanır ve `Iban` tipine dönüşür. Bu kontrol
  "komisyon koşulsuz iade edilir" kuralının taşıyıcısı; zayıflatılamaz.
  Sınırdan sonra akışta string IBAN DOLAŞMAZ. Response'ta maskeli döner.
- `POST /v1/withdrawals`'ta `Idempotency-Key` ZORUNLU. Çekim çok adımlı ve dışarıya
  para çıkarıyor; anahtarsız bir tekrar ikinci bir banka transferi başlatırdı.
  (Transfer'de de zorunlu — bkz. "Idempotency".)
- Response `202`: dönüldüğünde hiçbir para hareket etmedi. Tekrar eden request de `202`,
  ayrım gövdedeki `replayed` alanında.

**Banka entegrasyonu** (`decisions.md` madde 35)
- Üç deployable: `bank-adapter` ingress'siz, `bank-webhook` IP kısıtlı, `Bank.Fake`
  bankanın API'sinin yerinde durur (canlıda YOK). İlk ikisi bizim ve canlıda koşar;
  ortak kütüphane `BankIntegration.Core` üstündeler. Kuyruk BİZDE biter: komutu
  adaptör tüketir, bankayı HTTP ile o çağırır.
- Transfer sonucu SENKRON DEĞİL. Adaptör çağrıyı yapar, `bank_transfers` satırını
  `pending` yazar ve HİÇBİR ŞEY yayınlamaz; saga gerçekten `bank_transfer_pending`'de
  bekler. Kesin sonuç öğrenildiğinde cevap yayınlanır.
- İki yolun rolü EŞİT DEĞİL: callback ASIL yol (sürekli), mutabakat taraması KONTROL
  (günde birkaç kez). Tarama ikinci bir teslim kanalı değil, "kaçırdık mı" sorusu.
- Tarama KAPATILAMAZ; aralığı konfigüre edilir, varlığı edilmez. Kaçırılan callback
  aksi halde kalıcı kayıp olur: satır `pending` kalır, saga asılır, para clearing'de durur.
- `Bank:Reconciliation:StaleAfter`: yalnızca bu süreden uzundur cevapsız kalanlar
  sorulur. Callback çalışırken tarama boş döner; dönmediği satır sayısı alarm sinyali.
- `bank-webhook` AYRI deployable çünkü ingress'i var, adaptörün yok (madde 28).
  Tarama mantığı değişince bankanın çağırdığı endpoint YENİDEN BAŞLATILMAZ.
- Relay `bank-adapter`'da, webhook'un içinde DEĞİL — `topup-webhook`'un şeklinden
  bilinçli sapma. `bank-webhook`'un tek işi: doğrula, inbox'a yaz, `202`.
- Webhook modu top-up kalıbının aynısı: HAM gövde üzerinde HMAC, parse etmeden önce,
  inbox'a yaz + `202`, yayını relay yapar. İkinci bir kalıp İCAT EDİLMEZ.
- Sahte bankanın veritabanı YOK: transferler ve senaryolar bellekte, yeniden
  başlatınca siliniyor. Senaryolar bankanın iç bilgisi; `bank-adapter` onlara
  erişemez — erişebilse simülasyon değerini kaybederdi.
- HTTP request/response tipleri paylaşılan assembly'de DEĞİL, iki tarafta ayrı ayrı yazılır.
  Gerçek entegrasyonda o tipler bankanın dokümanından gelir; ortak tip "karşı taraf
  sözleşmeyi değiştirdi" hatasını imkânsız gösterirdi.
- `Shared.Contracts` yalnızca BİZİM mesajlarımızı taşır: `StartBankTransfer`,
  `BankTransferSucceeded`, `BankTransferFailed`.

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
- **Hiçbir dosyada emoji YOK** — doküman, kod yorumu, commit mesajı, hiçbiri. Onay ve
  ret işaretleri (tik, çarpı, boş kutu) de girmez: tabloda `evet` / `hayır` /
  `denenmedi` yazılır. Ağaç ve akış çizimlerindeki kutu ve ok karakterleri bunun
  dışında; onlar süs değil, çizimin kendisi.
- Kapsam dışı: Vault, Kubernetes, gerçek ödeme sağlayıcısı, multi-tenancy, caching.