# HiWallet — sistem

E-money cüzdan sistemi. Topoloji: bölünmüş servisler, asenkron koordinasyon,
**saga orchestration**.

Bu dosya sistemin ne olduğunu ve nasıl çalıştığını anlatır. Uygulamadan bağımsız
production katmanları `baseline.md`'de, kararların gerekçesi `decisions.md`'de,
şema `ledger-schema.md`'de, dosya yerleşimi `structure.md`'de.

**Dominant tema:** in-process consistency + optimistic lock + double-entry ledger.
Üstüne servisler arası consistency, saga orchestration, webhook delivery ve
scheduled raporlar.

**Teknik baz:** .NET controller-based Web API, PostgreSQL, RabbitMQ, Docker Compose.
Redis yok — bakiye tek Postgres'te ve korunacak kaynak tek transactional sınırın
içinde (`decisions.md` madde 3).

## Felsefe: mimari production seviyesi, kapsam sınırlı

Amaç, mimari ve yaklaşımları gerçekte nasıl yapılıyorsa öyle kurmak: katman ayrımı,
transaction sınırları, idempotency, concurrency stratejisi, error handling,
double-entry invariant'ı, saga compensation, observability — bunlarda taviz yok,
production kalitesinde.

Sınırlı olması yalnızca **kapsamı ve dış bağımlılıkları** kısaltır, mimariyi değil:

- Dış kurumların yerinde sahte servisler duruyor: `bank-fake` bankanın API'sinin,
  `stripe-fake` kart sağlayıcısının (`fakes/` altında, `decisions.md` madde 35).
  Aradaki sınır gerçek HTTP; `bank-adapter` bankaya `Bank__BaseUrl` ile bağlanıyor ve
  canlıda o ayar kurumun kendi adresini gösteriyor. Sahte servislerin HTTP sözleşmesi
  gerçeğinin şeklinde: transfer `202 pending` döner, sonuç callback ile gelir.
- Kapsam daraltılır: tek para birimi, tek tenant, tek instance yeter.
- Her katman "gösterilebilir en sade hali" ile alınır — ama varlığı ve doğru kurgusu görünür.

## Kapsam dışı

- **Webhook delivery ayrı bir uygulama değil**; top-up akışının içinde yaşıyor (madde 5).
- **Background job'lar** (mutabakat, business özeti, stuck saga taraması) buraya
  iliştirilmiştir, ayrı servis değildir (madde 7).
- **Distributed lock ve CQRS yok.** Bakiye tek Postgres'te; üstüne Redis lock koymak
  aynı garantiyi daha zayıf bir mekanizmayla tekrarlamak olurdu (`decisions.md` madde 3).
- **Multi-tenancy yok.** Sistem tek-tenant: bir e-money şirketinin iç sistemi.
  İçindeki person/business ayrımı tenancy değil, hesap tipidir.
- **Caching yok.** Bakiye projeksiyonu cache değil, kalıcı bir read tablosudur.

Bilinçli olarak eksik bırakılanların tam listesi ve gerekçeleri: `decisions.md` madde 12.

---

## 1. Sistem Karakteri

Elektronik para şirketinin cüzdan altyapısı. Kullanıcılar (person) ve işletmeler (business) cüzdan sahibidir; aralarında para hareketi olur. Para sisteme dışarıdan girer (kart yükleme, banka transferi) ve dışarıya çıkar (para çekme).

İki bölge var, ana öğretici mesaj bu ayrım:

- **Çekirdek (immediate, ACID):** Cüzdanlar arası para hareketi. Tek serviste, tek DB, double-entry, optimistic lock. Saga gerektirmez.
- **Kenar (eventual, saga/consumer):** Dış dünyayla konuşan akışlar (top-up, withdrawal). Asenkron, idempotent, gerektiğinde compensation'lı.

Mesaj: _Dağıtık karmaşıklığı her yere yayma. Tutarlılığın kritik olduğu çekirdeği tek boundary'de ACID tut; sadece dışarıyla konuşan kenarı dağıt._

## 2. Servisler

| Servis                  | Sorumluluk                                                              | Tutarlılık      |
| ----------------------- | ----------------------------------------------------------------------- | --------------- |
| wallet-service          | Cüzdanlar, double-entry ledger, transfer çekirdeği, bakiye projeksiyonu | Lokal ACID      |
| ↳ wallet-api            | Yukarıdakinin HTTP host'u, iç ağda; ön API'ler çağırıyor                | —               |
| ↳ wallet-consumer        | Yukarıdakinin ingress'siz worker host'u; kuyruktan okuyup ledger'a yazar | Idempotent     |
| personal-mobile-api     | Ön API: bireysel mobil; cüzdan, transfer, çekim; veritabanı yok         | —               |
| personal-web-bff        | Ön API: bireysel web uygulamasının BFF'i, sayfaları da sunuyor          | —               |
| onboarding              | Kayıt ve kimlik doğrulaması; kişisel veri kendi Postgres sunucusunda    | Tekrar edilebilir adımlar |
| business-api            | Ön API: işyerinin sistem entegrasyonu; veritabanı yok                   | —               |
| business-web-bff        | Ön API: işyeri panelinin BFF'i; veritabanı yok, iskelet                 | —               |
| backoffice-bff          | Ön API, iç ağ: backoffice panelinin BFF'i; çalışanın oturumu            | —               |
| withdrawal-orchestrator | Para çekme saga'sının state machine'i                                   | Eventual (saga) |
| card-topup              | Kartla yüklemenin ömrü: limit payı, ödeme, kapanış; iç ağda             | Eventual        |
| bank-adapter            | Bankayı HTTP ile arar, sonucu saga'ya yayınlar                          | Idempotent      |
| bank-webhook            | Bankanın sonuç callback'ini doğrular, inbox'a yazar                     | Idempotent      |
| bank-fake (BİZİM DEĞİL) | Bankanın API'sinin yerinde durur; **canlıda YOK**                      | —               |
| topup-webhook           | Kart sağlayıcısının ödeme bildirimini alır (imza doğrulama + inbox)    | —               |
| stripe-fake (BİZİM DEĞİL) | Kart sağlayıcısının yerinde durur; **canlıda YOK**                    | —               |
| hiwallet-keycloak       | Kimlik sağlayıcı: token'ı imzalıyor; kendi Postgres'i                   | —               |

Broker: RabbitMQ. Komut/event taşıma ve saga koordinasyonu burada.

Dış kurumlar (stripe-fake, bank-fake) **ağ sınırının** arkasında durur — bir C# interface'inin değil. Banka ayrı bir process, arada HTTP ve callback var, hafızası kendi belleğinde ve wallet'ı göremiyor. Bizim tarafımızdaki karşılığı `bank-adapter`: gerçek bankaya geçerken değişen tek şey `Bank:BaseUrl`, kod değil (`decisions.md` madde 35).

Bu, "interface arkasına al" yaklaşımından bilinçli bir sapma. Bir interface yalnızca derleme zamanı sınırıdır; taklit edilen tarafın gerçekten ayrı bir process olması, süreç ölümünü, ağ hatasını, kısmi başarıyı ve asenkron sonucu da sınanabilir kılıyor. Entegrasyonlarda kırılan şeyler bunlar, metot imzaları değil.

## 3. Double-Entry Ledger

Bakiye bir sayı olarak "güncellenmez"; ledger'a iki satır yazılır, bakiye bunlardan türetilir (gerektiğinde bir projeksiyon tablosuna materialize edilir).

**Invariant:** Her işlemde debit + credit toplamı sıfır. Para yoktan var olmaz, yok olmaz; taşınır.

**İşaret konvansiyonu:** Tek bir konvansiyon seçilir ve her yerde tutarlı uygulanır (konvansiyonu yarı yolda değiştirmek hataların ana kaynağıdır). Bu uygulamada: credit = `+`, debit = `-`; her ledger entry'si `(account_id, amount, direction)` taşır.

**Clearing hesabı:** Dış para giriş/çıkışının ledger içindeki karşı bacağı. Para dış dünyadan geldiğinde/gittiğinde bu teknik hesap dengeyi sağlar. Bakiyesi = "yolda olan / henüz settle olmamış" tutar; dış sağlayıcı (Stripe/banka) ile mutabakatın (reconciliation) dayanağıdır.

- Yükleme: kullanıcı cüzdanı `+100`, clearing `-100`.
- Çekme: kullanıcı cüzdanı `-100`, clearing `+100`.
- Settlement geldiğinde clearing dengelenip sıfıra çekilir.

## 4. Transfer Çekirdeği (immediate, ACID — saga DEĞİL)

Beş transfer tipi — **p2p, p2b, b2p, b2b, payment** — aynı mekanizmadır. Gönderen cüzdanı debit, alan cüzdanı credit, tek ACID transaction içinde, optimistic lock ile.

```
gönderen:  -X   (debit)
alan:      +X   (credit)
toplam:     0
```

Transfer tipi yalnızca **politika katmanını** değiştirir, çekirdeği değil:

- **Limit kontrolü:** Tipe göre farklı limitler (örn. p2p günlük limit, b2b daha yüksek tavan). Transfer'den ÖNCE, aynı transaction'ın parçası olarak kontrol edilir; limit aşılırsa transfer hiç başlamaz (immediate red). Limit aşımı bir iş kuralı reddidir, bir hata değil — `422`/ProblemDetails ile döner.
- **Komisyon:** Bazı tiplerde (özellikle `payment` ve `b2b`) komisyon kesilir. Komisyon, transfer'in **aynı double-entry işlemi içinde ek bacak** olarak modellenir: gönderenden komisyon tutarı düşülür, bir **komisyon/gelir hesabına** credit yazılır. Yani komisyon ayrı bir transfer değil, aynı atomik işlemin parçası.

```
payment örneği (100 ödeme, 2 komisyon):
  gönderen (person):     -102
  alan (business):       +100
  komisyon geliri hesabı:  +2
  toplam:                   0
```

Limit ve komisyon kuralları çekirdeğin dışında bir **policy** bileşeninde tutulur; çekirdek `transfer(from, to, amount, type)` alır, policy tipe göre limit/komisyonu uygular, sonuç tek transaction olarak ledger'a yazılır.

## 5. Kartla Yükleme Akışı (eventual — saga DEĞİL)

Para sisteme kart sağlayıcısından girer. Kart limitte öncelikli: limit yetmiyorsa ödeme hiç
açılmaz. Müşteri ödeme sayfasına gider, sonuç sağlayıcının bildirimiyle gelir. Telafi yok:
ödeme olmazsa hiçbir para hareket etmemiştir, yalnızca limit payı serbest kalır.

```
Müşteri (ön API üzerinden)
  → card-topup  POST /v1/card-topups, Idempotency-Key zorunlu:
       1. Yüklemeyi kaydet (created).
       2. wallet-api'den limit payı iste (müşterinin token'ıyla).
          Limit yetmiyorsa 422 → yükleme rejected, ödeme açılmaz.
       3. Pay ayrıldı (pending) → sağlayıcıda ödeme aç, referans yüklemenin kimliği.
       4. 202 + paymentUrl. Müşteri ödeme sayfasına gider.
Sağlayıcı (`stripe-fake`)
  → topup-webhook:
       İmzayı doğrula (HMAC, ham gövde), inbox'a yaz ((provider, event_id) UNIQUE),
       commit sonrası 202 (decisions.md madde 29). Relay RabbitMQ'ya taşır.
  → card-topup (hiwallet.card-payments):
       Bildirimi yüklemeyle eşleştir: paid ya da failed. Kapanışı geçişle aynı
       transaction'da outbox'a yaz; relay wallet'a taşır.
  → wallet-consumer (hiwallet.card-topups):
       Kapanış satırı + ledger TEK ACID transaction:
         paid:   kullanıcı cüzdanı +X, sağlayıcının clearing'i -X, pay kapanır
         failed: ledger'a hiçbir şey yazılmaz, pay kapanır
```

**Süresi dolan ödeme.** Sağlayıcı oturumu kapanan ödemeyi bildirmiyor. card-topup'ın taraması
oturumu kapanmış yüklemeleri sağlayıcıya sorup kapatıyor; bildirimi kaçırılmış ödemeyi de
böyle buluyor. Pay saatle düşmüyor, yalnızca sağlayıcının kesin cevabıyla kapanıyor.

**İdempotency her adımda.** Başlatma `card_topups(subject, idempotency_key)` UNIQUE; pay ve
ödeme yüklemenin kimliğiyle tekil, bu yüzden tekrar eden istek yarım kalan adımı tamamlıyor.
Bildirim inbox'ta `(provider, event_id)` UNIQUE, aynı sonucun ikinci bildirimi yüklemenin
durumunda `Ignored`. Wallet'ta kapanış satırı ledger'la aynı transaction'da; ledger anahtarı
`provider:card_topup_id` (decisions.md madde 27).

Havale ile yükleme ayrı bir yoldan geliyor: bankanın bildirimi `bank-webhook`'a, oradan
`bank-adapter` üzerinden wallet'a (`CLAUDE.md` "Havale ile yükleme").

## 6. Withdrawal Akışı (eventual, SAGA orchestration — compensation burada)

Para çekme, saga'nın evidir: çekirdekte ACID düşme + dış banka adımı eventual + banka fail olursa compensation. State machine `withdrawal-orchestrator`'da, tek yerde okunur.

```
X = çekilen tutar, k = müşteriden alınan komisyon (yoksa k = 0).

[Initiated]
  → Girdi doğrulaması: IBAN mod-97 checksum'ı SINIRDA kontrol edilir (baseline.md
    madde 6). Geçersizse 400; saga başlamaz, bankaya request gitmez, ücret doğmaz.
  → Limit kontrolü (hesap bazında günlük çekim limiti). Aşılırsa → [Rejected] (hiç para hareketi olmaz).
  → wallet-service: cüzdandan X+k düş (lokal ACID)
       cüzdan -(X+k), clearing +X, revenue +k    (para "yolda", komisyon tahakkuk etti)
  → [Debited]

[Debited]
  → tutar inceleme eşiğinin üstündeyse → [UnderReview]
  → değilse bank-adapter'a komut: "X banka transferi başlat" (CommandId'li, idempotent)
  → [BankTransferPending]

[UnderReview]  (bir çalışanın kararını bekliyor; takılmış sayılmaz)
  + serbest bırakıldı → banka komutu → [BankTransferPending]
  + iptal edildi → RefundToWallet komutu, aktörü iptal eden çalışan → [Cancelling]
       → ters kayıt yazıldı → [Cancelled]

[BankTransferPending]
  + BankTransferSucceeded → [Completed]
       (settlement; clearing kapanır)
  + BankTransferFailed (transient retry'lar tükendi → kalıcı fail)
       → RefundToWallet komutu (idempotent + retry'lı)
       → [Compensating]

[Compensating]
  → wallet-service: TAM ters kayıt (dengeleyen kayıt, SİLME değil)
       cüzdan +(X+k), clearing -X, revenue -k
  + RefundSucceeded → [Failed]
```

**Komisyon müşteriye koşulsuz iade edilir.** Yukarıdaki compensation üç bacaklı; `revenue -k`
bacağını atlamak iki bacakla da dengeli bir kayıt üretir (toplam yine sıfır, trigger susar) ama
müşteri gerçekleşmemiş bir işlemin komisyonunu ödemiş olur ve `revenue`'da vermediğimiz bir
hizmetin geliri kalır. Sessiz ve müşteri parası kaybettiren bir kusur — o yüzden bacak
opsiyonel değil.

Koşulsuz olmasının gerekçesi: **başarısızlığın sebebi ya bizde ya bankadadır.** Müşteri
kaynaklı tek gerçekçi senaryo yanlış IBAN'dır, o da `[Initiated]` adımındaki mod-97
doğrulamasıyla saga başlamadan eleniyor. Geriye kalan (yapısal olarak geçerli ama kapalı
hesap) o kadar nadir ki bir konfigürasyon kolunu hak etmiyor. Bu yüzden IBAN doğrulaması
"olsa iyi olur" bir validation değil, bu kuralın taşıyıcısı — kalkarsa kural yalan olur.

**Sağlayıcı ücreti ayrı bir hesaptır, müşteriye yansımaz.** Banka başarısız denemeye ücret
kesmişse bu bizimle banka arasındaki bir meseledir; `revenue` ters kayıtla iade edilir,
`provider_expense` edilmez (`decisions.md` madde 6). İki hesabın ayrı durmasının en net
gerekçesi bu.

Ücreti kimin yüklendiği sağlayıcı bazında konfigüre edilir — `FeeOnFailure: Charged | Waived`
(`decisions.md` madde 18):

- `Charged` — banka başarısız denemeye de ücret kesiyor, maliyeti biz üstleniyoruz.
- `Waived` — sözleşme gereği kesmiyor, banka üstleniyor.

**`provider_fees` satırı denemeye bağlı yazılır, başarıya değil** (yalnızca `Charged`
sağlayıcılarda). Yazılmazsa mutabakat faturadaki kalemi "faturada var/sende yok" diye
kaçırılmış webhook sanır ve yanlış alarm üretir (`decisions.md` madde 11).

**Idempotency (saga):**

- API girişinde `Idempotency-Key`: aynı çekme request'i iki kez → yeni saga başlatma, mevcut durumu dön.
- Komut tüketiminde `CommandId`: bank-adapter ve wallet-service aynı komutu iki kez işlemez (`processed_messages`).
- Saga event tüketiminde: state + correlation ile değerlendirilir. Zararsız tekrar (aynı event, ya da saga çoktan ilerlemiş) yok sayılır; **çelişkili** event (telafiden sonra gelen "başarılı" gibi) yok SAYILMAZ — durum olduğu yerde bırakılıp alarm üretilir, çünkü para kaybına işaret ediyor (`decisions.md` madde 31).

**Retry (saga):**

- Transient (DB deadlock, ağ, timeout) → adım seviyesinde backoff'lu retry, compensation'a gitmeden.
- Retry'lar tükenince → kalıcı fail → compensation dalı.
- Compensation komutları da idempotent + retry'lı (compensation'ın kendisi de güvenilir çalışmalı).

## 7. Scheduled Raporlar (Background Jobs)

Periyodik job'lar `ScheduledJob` üzerinde çalışıyor: `BackgroundService` + `PeriodicTimer`. Birden fazla instance'ta tek turu `pg_try_advisory_lock` (`JobLease`) garanti ediyor. Wallet'ın doğal ihtiyaçları, yapay değil:

- **Mutabakat (reconciliation) raporu:** Clearing hesabı bakiyesi ile dış sağlayıcının settlement kayıtları karşılaştırılır. Tutmuyorsa eksik/hatalı işlem işaretlenir. Clearing hesabı konseptini kapatan job budur.
- **Business günlük özeti:** Her business için günlük işlem hacmi, işlem sayısı, kesilen komisyon toplamı.
- **Stuck saga taraması:** Belirli süredir `BankTransferPending`/`Compensating` durumunda takılı kalmış withdrawal saga'larını bulup raporlar. İncelemedeki (`UnderReview`) saga'yı takılmış saymaz: o bir insanı bekliyor.

Graceful shutdown ile uyumlu: job'lar `CancellationToken`'a saygı duyar, SIGTERM'de yarıda kalan iş temiz biter (`baseline.md` madde 10).

## 8. Sıralama (Ordering)

Her kuyruk `x-single-active-consumer` ve `prefetch=1` ile tek tüketici tarafından sırayla işlenir: kuyruğa gelen mesajlar geldikleri sırayla işleniyor. Çekimde sıra ayrıca saga'nın kendisinden geliyor: bir saga'nın aynı anda tek bekleyen komutu var. Kartla yüklemede aynı ödemenin bildirimleri sağlayıcının gönderdiği sırayla işleniyor.

**Sınır.** Bu garanti broker'a VARDIKTAN sonrası için geçerli. Relay çok instance koşarsa `SKIP LOCKED` ile alınan batch'ler farklı hızda yayınlanabiliyor ve sıra daha exchange'e ulaşmadan bozulabiliyor. topup-webhook'un relay'i bu yüzden tek instance; `decisions.md` madde 30.

## 9. Sahte kurumlar (`fakes/`)

Gerçek Stripe/banka yerine, dış dünya kötülüklerini **bilinçli tetikleyebilen** sahte kurumlar. "idempotency/retry çalışıyor mu" kanıtı bu servislerle verilir.

İkisi de `fakes/` altında, `src/` altında DEĞİL; `src/` → `fakes/` referansı derleme hatası (`HIW001`).

Kartla para girişi — `stripe-fake`:

- Ödeme API'si: `POST /v1/payments` ödeme açar (aynı referansla ikinci istek ilkini döner), `GET /v1/payments?reference=` durumu söyler.
- Ödeme sayfası `/odeme/{id}`: müşteri "Öde" ya da "Vazgeç" der, sayfa onu dönüş adresine yollar ve sonuç imzalı webhook'la `topup-webhook`'a gider.
- Oturumun süresi dolan ödeme için webhook GÖNDERMEZ; sonucu sormak gerekiyor.

Havaleyle para girişi — `bank-fake` (`POST /v1/incoming-transfers`): toplama hesabına gelen havale; bildirim gönderilmeden de verilebiliyor, hesap hareketi taramasını sınamak için.

Para çıkışı — yalnızca `bank-fake` (`POST /v1/scenarios`, `outcome` alanı):

- **Başarılı** transfer. Karşılığı `Success`.
- **Başarısız** sonuç (withdrawal'da compensation'ı tetiklemek için). Karşılığı `Failure`.
- **Transient sonra başarılı** (retry'ın devreye girip sonunda başardığını göstermek). Karşılığı `TransientFailure`.
- **Gecikmeli** sonuç. Karşılığı `DelayedSuccess`.
Transfer sonucu SENKRON DÖNMÜYOR — kabul `202 pending`, kesin sonuç callback ya da durum sorgusuyla (decisions.md madde 35). Sahte bankanın hafızası bellekte; yeniden başlatınca siliniyor.

## 10. Çıkış Kriteri

- 5 transfer tipi tek çekirdekten geçiyor, double-entry invariant'ı (toplam sıfır) her işlemde korunuyor.
- Limit aşımı immediate reddediliyor; komisyon aynı atomik işlemde kesiliyor.
- Kartla yükleme: limit yetmiyorsa ödeme açılmıyor; webhook imzası doğrulanıyor, inbox+relay ile kaybolmuyor, duplicate webhook bir kez işleniyor; süresi dolan ödemenin payı tarama ile serbest kalıyor.
- Withdrawal saga'sı uçtan uca çalışıyor; banka fail senaryosunda compensation cüzdana parayı geri yazıyor (ters kayıtla, silmeden).
- Clearing hesabı bakiyesi "yolda olan parayı" doğru gösteriyor (mutabakat dayanağı).
- Scheduled mutabakat raporu clearing ile settlement'ı karşılaştırıp tutarsızlığı yakalayabiliyor.
- Trace uçtan uca takip edilebiliyor (API → card-topup → sağlayıcı → webhook → kuyruk → consumer → ledger; API → saga → bank-adapter → banka → callback → compensation).
