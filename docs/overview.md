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

- Dış servisler simüle edilir (KYC `true` döner, fraud-check fake, Stripe/banka
  `provider-fake`). Ama her fake bir **interface arkasında** durur (`IKycService`,
  `IPaymentProvider`) — yarın gerçek implementasyon takılınca üst akış değişmez.
  Fake bile production mimarisine uygun (geçici hack değil, interface'li stub).
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
| ↳ wallet-api            | Yukarıdakinin public HTTP host'u (mobil/web)                            | —               |
| ↳ topup-consumer        | Yukarıdakinin ingress'siz worker host'u; kuyruktan okuyup ledger'a yazar | Idempotent     |
| withdrawal-orchestrator | Para çekme saga'sının state machine'i                                   | Eventual (saga) |
| bank-service (fake)     | Dış banka transferini simüle eder                                       | —               |
| topup-webhook           | Kart/banka yükleme webhook'larını alır (imza doğrulama + inbox)         | —               |
| provider-fake           | Test için sahte dış sağlayıcı (Stripe/banka muadili)                    | —               |

Broker: RabbitMQ. Komut/event taşıma ve saga koordinasyonu burada.

Dış sağlayıcılar (provider-fake, bank-service) bir `IPaymentProvider` / `IBankProvider` soyutlamasının arkasında durur; gerçekte burada Stripe/banka API'si olurdu, burada fake implementasyon. Böylece sağlayıcı bağımsızlığı gösterilir, sağlayıcıyı değiştirmek uygulama kodunu etkilemez.

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

## 5. Top-Up Akışı (eventual, idempotent consumer — saga DEĞİL)

Para sisteme dışarıdan girer. Tek adımlı olduğu için saga değil; idempotent consumer yeterli.

```
Dış sağlayıcı (provider-fake)
  → topup-webhook:
       1. İmza doğrula (HMAC: paylaşılan secret ile payload imzalanır;
          sahte webhook'u engeller). Geçersiz → 401.
       2. DB transaction: inbox tablosuna yaz (dış event_id UNIQUE).
          Duplicate event_id → çakışmayı yakala, yine başarı say.
       3. Commit başarılı → 200 dön. (200, ancak kalıcılık garanti olduktan SONRA.)
  → relay (background worker):
       inbox'taki "unpublished" satırları RabbitMQ'ya publish eder
       (publisher confirms ile), sonra "published" işaretler.
  → RabbitMQ (cüzdan-bazlı partitioning — bkz. madde 8)
  → topup-consumer:
       event_id daha önce işlendi mi? (processed_events tablosu)
       Hayırsa: (processed_events + ledger yazımı) TEK ACID transaction:
         kullanıcı cüzdanı +X, clearing -X
```

**İki kademe idempotency:** (1) webhook girişinde inbox `event_id` UNIQUE, (2) consumer'da `processed_events` + ledger aynı transaction. İkisi birlikte: webhook kaybolmaz (inbox + relay), çift teslimde bir kez işlenir (idempotency).

## 6. Withdrawal Akışı (eventual, SAGA orchestration — compensation burada)

Para çekme, saga'nın evidir: çekirdekte ACID düşme + dış banka adımı eventual + banka fail olursa compensation. State machine `withdrawal-orchestrator`'da, tek yerde okunur.

```
X = çekilen tutar, k = müşteriden alınan komisyon (yoksa k = 0).

[Initiated]
  → Girdi doğrulaması: IBAN mod-97 checksum'ı SINIRDA kontrol edilir (baseline.md
    madde 6). Geçersizse 400; saga başlamaz, bankaya istek gitmez, ücret doğmaz.
  → Limit/kural kontrolü (günlük çekim limiti, KYC vb.). Aşılırsa → [Rejected] (hiç para hareketi olmaz).
  → wallet-service: cüzdandan X+k düş (lokal ACID)
       cüzdan -(X+k), clearing +X, revenue +k    (para "yolda", komisyon tahakkuk etti)
  → [Debited]

[Debited]
  → bank-service'e komut: "X banka transferi başlat" (CommandId'li, idempotent)
  → [BankTransferPending]

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

- API girişinde `Idempotency-Key`: aynı çekme isteği iki kez → yeni saga başlatma, mevcut durumu dön.
- Komut tüketiminde `CommandId`: bank-service ve wallet-service aynı komutu iki kez işlemez (`processed_messages`).
- Saga event tüketiminde: state + correlation ile değerlendirilir. Zararsız tekrar (aynı event, ya da saga çoktan ilerlemiş) yok sayılır; **çelişkili** event (telafiden sonra gelen "başarılı" gibi) yok SAYILMAZ — durum olduğu yerde bırakılıp alarm üretilir, çünkü para kaybına işaret ediyor (`decisions.md` madde 31).

**Retry (saga):**

- Transient (DB deadlock, ağ, timeout) → adım seviyesinde backoff'lu retry, compensation'a gitmeden.
- Retry'lar tükenince → kalıcı fail → compensation dalı.
- Compensation komutları da idempotent + retry'lı (compensation'ın kendisi de güvenilir çalışmalı).

## 7. Scheduled Raporlar (Background Jobs)

`IHostedService` + scheduler (Quartz.NET ya da `PeriodicTimer`) ile periyodik çalışan job'lar. Wallet'ın doğal ihtiyaçları, yapay değil:

- **Mutabakat (reconciliation) raporu:** Clearing hesabı bakiyesi ile dış sağlayıcının settlement kayıtları karşılaştırılır. Tutmuyorsa eksik/hatalı işlem işaretlenir. Clearing hesabı konseptini kapatan job budur.
- **Business günlük özeti:** Her business için günlük işlem hacmi, işlem sayısı, kesilen komisyon toplamı.
- **Stuck saga taraması:** Belirli süredir `BankTransferPending`/`Compensating` durumunda takılı kalmış withdrawal saga'larını bulup raporlar.

Graceful shutdown ile uyumlu: job'lar `CancellationToken`'a saygı duyar, SIGTERM'de yarıda kalan iş temiz biter (`baseline.md` madde 10).

## 8. Sıralama (Ordering)

Sıralama yalnızca **aynı cüzdan** için önemlidir; farklı cüzdanlar bağımsız, paralel işlenir. Mesajlar cüzdan id'sine göre partition'lanır (RabbitMQ consistent hashing exchange): aynı cüzdanın tüm mesajları aynı kuyruğa düşer, kuyruk `x-single-active-consumer` ile tek tüketici tarafından sırayla işlenir; farklı cüzdanlar paralel akar. Büyük ölçekte de yeterli — tek darboğaz "tek cüzdana saniyede binlerce işlem" ki gerçekçi değil.

**Sınır.** Bu garanti broker'a VARDIKTAN sonrası için geçerli. Relay çok instance koşarsa `SKIP LOCKED` ile alınan batch'ler farklı hızda yayınlanabiliyor ve sıra daha exchange'e ulaşmadan bozulabiliyor. Bugün relay tek instance ve top-up'lar toplama olduğu için tetiklenmiyor; `decisions.md` madde 30.

## 9. Test Servisleri (provider-fake)

Gerçek Stripe/banka yerine, dış dünya kötülüklerini **bilinçli tetikleyebilen** sahte sağlayıcı. "idempotency/retry çalışıyor mu" kanıtı bu servisle verilir.

provider-fake şunları tetikleyebilmeli:

- **Başarılı** webhook/komut sonucu.
- **Başarısız** sonuç (withdrawal'da compensation'ı tetiklemek için).
- **Duplicate** gönderim (aynı event iki kez — idempotency testi: "webhook iki kez geldi, bakiye bir kez arttı").
- **Gecikmeli** gönderim (eventual davranışı görünür kılmak).
- **Sırasız** gönderim (ordering/partitioning testi).
- **Transient sonra başarılı** (retry'ın devreye girip sonunda başardığını göstermek).

bank-service de fake: withdrawal komutuna başarılı / transient-fail / kalıcı-fail / gecikmeli yanıt üretebilmeli.

## 10. Çıkış Kriteri

- 5 transfer tipi tek çekirdekten geçiyor, double-entry invariant'ı (toplam sıfır) her işlemde korunuyor.
- Limit aşımı immediate reddediliyor; komisyon aynı atomik işlemde kesiliyor.
- Top-up: webhook imzası doğrulanıyor, inbox+relay ile kaybolmuyor, duplicate webhook bir kez işleniyor.
- Withdrawal saga'sı uçtan uca çalışıyor; banka fail senaryosunda compensation cüzdana parayı geri yazıyor (ters kayıtla, silmeden).
- Clearing hesabı bakiyesi "yolda olan parayı" doğru gösteriyor (mutabakat dayanağı).
- Scheduled mutabakat raporu clearing ile settlement'ı karşılaştırıp tutarsızlığı yakalayabiliyor.
- Cüzdan-bazlı partitioning ile aynı cüzdanda sıra korunuyor.
- Trace uçtan uca takip edilebiliyor (webhook → kuyruk → consumer → ledger; API → saga → bank-service → compensation).
