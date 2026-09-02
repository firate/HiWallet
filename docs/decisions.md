# Kararlar

Bu dosya "ne" değil "neden" anlatır. Kısa kural listesi için `CLAUDE.md`.
Her madde: karar, gerekçe, elenen alternatif.

---

## 1. Wolverine yalnızca in-process mediator

**Karar.** Wolverine handler keşfi ve in-process command/query dispatch için kullanılır.
RabbitMQ transport'u, durable inbox/outbox'ı ve `Saga` base class'ı kullanılmaz.
Inbox tablosu, relay worker, `processed_messages` ve withdrawal state machine elle yazılır.

**Gerekçe.** Projenin öğretici mesajı "at-least-once mesajlaşmada tutarlılık nasıl kurulur".
Wolverine bu mekanizmayı hazır verdiğinde mesaj görünmez oluyor.

**Elenen alternatif.** Wolverine'in durable messaging'ini açmak. Hızlı, ama iki risk var:
(a) öğretici içerik kayboluyor, (b) Wolverine'in envelope tabloları ile elle yazılan outbox
paralel çalışırsa iki ayrı at-least-once garantisi birbirini bozuyor. İkisi bir arada olmaz —
ya tamamen Wolverine ya tamamen elle.

**Sonuç.** Eğer ileride Wolverine transport'una geçilirse elle yazılan inbox/outbox/relay
tamamen kaldırılır, karma bırakılmaz.

---

## 2. Optimistic lock `wallet_balances` üzerinde

**Karar.** Concurrency token `wallet_balances.version`. `ledger_entries` üzerinde
hiçbir lock veya version kolonu yok.

**Gerekçe.** `ledger_entries` append-only. Optimistic lock "okuduğumdan beri bu satır değişti mi"
sorusunu sorar; hiç UPDATE edilmeyen bir satırda bu soru anlamsız. İki INSERT birbiriyle
çakışmaz, ikisi de başarılı olur. Gerçek çakışma bakiye projeksiyonunda: iki eşzamanlı transfer
aynı gönderen için 500 okuyup ikisi de 400 yazmaya çalışır (lost update).

**Akış.**

```sql
-- 1. oku
SELECT balance, version FROM wallet_balances WHERE account_id = @from;   -- 500, v7

-- 2. uygulama: yeterli bakiye mi, limit aşılıyor mu, komisyon kaç

-- 3. ledger'a yaz (lock yok)
INSERT INTO ledger_entries (...) VALUES (@tx, @from, -100), (@tx, @to, +100);

-- 4. projeksiyonu güncelle
UPDATE wallet_balances
   SET balance = balance - 100, version = version + 1
 WHERE account_id = @from AND version = 7;
-- 0 satır → DbUpdateConcurrencyException → rollback → retry
```

**Çok instance.** Davranış değişmez. Kontrol uygulamada değil DB'de. Postgres aynı satıra yazan
transaction'ları seri hale getirir, ikinci transaction `WHERE` koşulunu güncel satır üstünde
yeniden değerlendirir. `READ COMMITTED` yeterli, `SERIALIZABLE` gerekmez.

**Elenen alternatif.** `UPDATE ... WHERE balance >= 100` (version'sız koşullu update).
Atomik ve doğru bakiye korur ama iki sorunu var: (a) limit ve komisyon uygulama tarafında
hesaplanıyor, hangi anlık görüntüye dayandığı sabitlenemiyor; (b) 0 satır dönünce
"para yetmedi" ile "çakışma" ayırt edilemiyor — biri `422`, diğeri `409` gerektiriyor.

**Elenen alternatif.** `SELECT ... FOR UPDATE` (pessimistic). Çakışma nadir olduğu için
gereksiz bekleme üretir.

---

## 3. Distributed lock yok

**Karar.** Redis distributed lock kullanılmaz. Wallet Redis'siz kurulabilir.

**Gerekçe.** Distributed lock, korunacak kaynak tek transactional sınırın dışındaysa gerekir.
Burada bakiye tek serviste tek Postgres'te. Postgres zaten satır kilidi, MVCC ve commit garantisi
veriyor; üstüne Redis lock koymak aynı garantiyi daha zayıf bir mekanizmayla tekrarlamak olur.
Redis lock'un doğruluğu TTL'e dayanıyor: TTL dolarsa lock başkasına geçer, ilk sahibi hâlâ
transaction içindedir, iki taraf birden yazar. Optimistic lock'ta bu kırılma yok çünkü kontrol
kaynağın kendisinde.

**Kapsam notu.** Distributed lock bu referans uygulama setinde Biletleme'nin konusu
(seat hold senaryosu). Wallet'ın teması in-process consistency.

**İstisna — background job tekilliği.** Projedeki tek gerçek koordinasyon ihtiyacı.
Çok instance'ta relay worker aynı satırı birden fazla kez publish etmemeli.
Çözüm Postgres içinde:

- Relay ve kuyruk tüketiminde: `SELECT ... FOR UPDATE SKIP LOCKED` — instance'lar paralel
  çalışır, birbirinin satırına dokunmaz. Tercih edilen.
- Tekil çalışması gereken job'larda (mutabakat, stuck saga taraması):
  `SELECT pg_try_advisory_lock(@jobId)`, alamayan instance o turu atlar.

Aynı DB'de olduğu için TTL sorunu yok; bağlantı koparsa advisory lock otomatik düşer.

---

## 4. Idempotency key ilgili tablonun kolonudur

**Karar.** `ledger_transactions` ve `withdrawal_sagas` tablolarında
`(account_id, idempotency_key)` UNIQUE. Ayrı bir idempotency store yok.

**Gerekçe.** Transfer senkron ve atomik: ledger yazımı ile idempotency kaydı aynı satırda,
aynı transaction'da. Ayrı bir tablo ikinci yazma ve ikinci tutarlılık noktası demek olurdu.
Withdrawal'da istek bir ledger transaction'ı değil bir saga başlatıyor, dolayısıyla kolonun
yeri saga tablosu. Saga'nın kendi `state` kolonu "iş nerede" bilgisini zaten taşıdığı için
replay'de saklanmış response gövdesi de gerekmiyor — saga id + güncel state dönülür.

**Kalıp.**

```sql
INSERT INTO withdrawal_sagas (..., account_id, idempotency_key)
VALUES (...)
ON CONFLICT (account_id, idempotency_key) DO NOTHING;
-- 0 satır → mevcut kaydı oku, state'ini dön (yeni saga BAŞLATMA)
```

**Elenen alternatif.** Önce `SELECT`, yoksa `INSERT`. Tek instance'ta bile TOCTOU açığı var.

**Neden composite.** Key'i client üretiyor. Yalnız `idempotency_key` UNIQUE olsaydı iki farklı
kullanıcının aynı key'i üretmesi durumunda birinin isteği diğerininkiyle karışırdı.

**Not.** Bu, Bölüm 3 madde 5'teki iki kademe idempotency'yi (inbox `event_id` UNIQUE +
`processed_events`) değiştirmez. O hat mesajlaşma tarafı, bu hat API girişi.

---

## 5. Zero-sum invariant nerede zorlanıyor

**Karar.** İki yerde: DB'de deferred constraint trigger + test suite'inde property test.

**Gerekçe.** Postgres'te "bir transaction_id'ye ait entry'lerin toplamı sıfır" kontrolü
tek satırlık `CHECK` ile yapılamaz — satırlar ayrı ayrı insert ediliyor, ara durumda toplam
sıfır değil. `CONSTRAINT TRIGGER ... DEFERRABLE INITIALLY DEFERRED` commit anında çalışır,
gerçek garantiyi o verir. Test ise kanıt üretir ve regresyonu yakalar.

**Ek koruma.** `ledger_entries` üzerinde UPDATE ve DELETE revoke edilir. Yazılmazsa
ilk fırsatta "düzeltme" amaçlı bir update kodu ortaya çıkar ve invariant sessizce bozulur.

---

## 6. Para tipi ve hesap tipleri

**Karar.** `numeric(19,4)`, yanında ayrı `currency` kolonu (ISO 4217, `char(3)`).
Tek para birimi kullanılsa bile kolon baştan durur.

**Gerekçe.** Sonradan currency eklemek tüm ledger'ı dolaşan bir migration demek.
`float`/`double` para için hiçbir koşulda kullanılmaz.

**Hesap tipleri.** Tek `accounts` tablosunda `account_type`:
`user_wallet`, `clearing`, `revenue`, `nostro`, `provider_expense`.

**Kural.** Sistem hesapları negatife düşebilir; `user_wallet` düşemez.
Bu bir CHECK constraint değil, uygulama kuralı — çünkü clearing tasarımı gereği negatif duruyor
(yükleme akışında cüzdan `+100`, clearing `-100`).

**`account_type` ile `owner_type` dik boyutlardır.** Birincisi hesabın ledger'daki rolü,
ikincisi sahibinin kim olduğu (`person` / `business`, sistem hesaplarında NULL).
Tek kolonda birleştirilmez: ledger çekirdeğinin sorduğu soru "bu hesap negatife düşebilir mi",
cevabı person ve business için aynı. Transfer tipi (`p2p`/`p2b`/`b2p`/`b2b`) owner_type'a bakar,
ama bu policy katmanının işi, çekirdeğin değil.

**`nostro` neden gerekli.** Bölüm 3 "settlement geldiğinde clearing sıfıra çekilir" diyor ama
neye karşı dengelendiğini söylemiyor. Cevap bu hesap: `clearing` yolda olan para,
`nostro` bankada duran gerçek para. Mutabakat, `nostro` bakiyesini banka ekstresiyle
karşılaştırarak yapılır.

**`revenue` ve `provider_expense` netleştirilmez.** Müşteriden alınan komisyon gelir,
sağlayıcıya ödenen ücret gider. Ayrı hesaplarda durur, net marj rapor seviyesinde hesaplanır.
Ayrı durmalarının en net gerekçesi compensation: banka fail olduğunda `revenue` ters kayıtla
iade edilir (Bölüm 3 madde 6 kuralı), `provider_expense` edilmez — banka işlemi denediyse ücreti
kesilmiştir. Tek hesapta netleşselerdi bu ayrım yapılamazdı.

---

## 7. Servis sınırı ve veritabanı ayrımı

**Karar.** wallet-service ve withdrawal-orchestrator ayrı veritabanı (en az ayrı schema).
Orchestrator wallet tablolarına doğrudan yazmaz.

**Gerekçe.** Aynı DB paylaşıldığında saga'nın anlamı kalmıyor; orchestrator er ya da geç
wallet tablolarına doğrudan yazmaya başlıyor ve compensation gereksizleşiyor.
Bölüm 3'ün "çekirdeği tek boundary'de ACID tut, sadece kenarı dağıt" mesajı ancak sınır
gerçekten varsa gösterilebilir.

---

## 8. Deadlock önleme: satır güncelleme sırası

**Karar.** Bir transaction içinde birden fazla `wallet_balances` satırı güncelleniyorsa
her zaman `account_id` artan sırayla.

**Gerekçe.** Transfer iki satıra dokunuyor, komisyonluysa üçe. A→B ve B→A eşzamanlı gelir ve
satırlar farklı sırayla güncellenirse Postgres deadlock verir. Tek instance'ta düşük yükte
görünmez, yük altında ve çok instance'ta çıkar.

---

## 9. Retry politikası

**Karar.** Transfer handler'ında `DbUpdateConcurrencyException` için 3 denemelik retry,
her denemede projeksiyon yeniden okunur ve komisyon/limit yeniden hesaplanır.

**Gerekçe.** Eski `version` ile tekrar denemek sonsuza kadar başarısız olur —
retry'ın anlamı yeni anlık görüntüyle yeniden denemek.

**Ayrım.** Bu retry, saga'daki adım retry'ından (Bölüm 3 madde 6, transient banka hatası) ayrıdır.
Karıştırılmaz: buradaki DB içi çakışma, oradaki dış bağımlılık hatası.

---

## 10. Sağlayıcı ücreti: net vs invoiced

**Karar.** İki model desteklenir, sağlayıcı bazında konfigürasyonla seçilir
(`FeeSettlement: Net | Invoiced`). Global ayar değil — iki sağlayıcının aynı sistemde
farklı modelle çalışması gerçekçi ve gösterilmeye değer.

**Net.** Sağlayıcı ücreti settlement anında kesiyor, tutar o an belli
(Stripe tarzı). `provider_expense` bacağı settlement kaydının parçası.

**Invoiced.** Ücret işlem anında tahakkuk ediyor ama ödeme dönem sonunda faturayla
(bazı bankalar). Settlement kaydında `provider_expense` bacağı yok; fatura geldiğinde
tek toplu ledger kaydı yazılır: `provider_expense -toplam / nostro +toplam`.

**`provider_payable` hesabı eklenmedi.** Muhasebe olarak tahakkuk daha doğru olurdu, ama
projeye tahakkuk kavramı ekliyor ve dominant temayla (saga, idempotency) ilgisi yok.
Fatura anındaki kayıt kendi içinde dengeli olduğu için zero-sum bozulmuyor.

**Kaybedilen ve geri kazanılan.** Invoiced modelde ledger granülaritesi gidiyor —
işlem başına gider satırı yok, tek toplu kayıt var. Bilgi kaybolmuyor: `provider_fees`
tablosu işlem bazında `transaction_id` ile bağlı satır tutuyor.

**Ayrım.** Ledger gerçekleşen para hareketi, `provider_fees` beklenen/gerçekleşen ücret takibi.
`expected_amount` ledger'a yazılmaz: gerçekleşmiş bir hareket değil, hiçbir entry'ye karşılık
gelmiyor, zero-sum toplamına girmiyor. Ledger'ın "burada yazan her şey gerçekleşmiştir"
özelliği korunur. Ayrıca fatura eşleştirmesi sonradan UPDATE istiyor (`invoice_ref`,
`actual_amount`), ledger ise immutable — yaşam döngüleri uyuşmuyor.

**Elenen alternatif.** Ücret kolonlarını `ledger_transactions`'a eklemek. Kolonlar
transferlerde (hacmin çoğunluğu) hep NULL kalır ve "NULL demek yok mu, uygulanmaz mı"
ayrımı her sorguya taşınır. Tek kaynak ilkesi zaten korunuyor: gerçekleşen paranın tek
kaynağı ledger, ücret beklentisinin tek kaynağı `provider_fees`.

**Rapor notu.** İki model aynı sistemde çalıştığında "toplam gider" iki yerden geliyor
(net'te ledger, invoiced'ta fatura kaydı). Rapor katmanında bunu birleştiren tek bir view
yazılır, yoksa her raporda iki kaynak elle toplanır.

---

## 11. Fatura uyuşmazlığı

**Karar.** Fatura tutarı ile `provider_fees.expected_amount` toplamı tutmuyorsa
ledger'a HİÇBİR ŞEY yazılmaz. Kayıt `PendingReview` durumunda bekler, alarm üretilir.

**Gerekçe.** Sistem hangi tarafın haklı olduğuna karar veremez. Fatura tutarını sorgusuz
yazmak yanlış gider kaydı üretir; beklenen tutarı yazmak da yanlış, çünkü bankadan çıkan
para faturadaki tutar. İkisi de sonradan ters kayıt gerektirir. Yazmamak doğru olan.

**Tolerans.** Kuruş farkı kaçınılmaz (sağlayıcı işlem başına yuvarlıyor, sen ondalık
tutuyorsun; binlerce işlemde birikiyor). Eşik oransal: beklenenin binde biri veya 1 birim,
hangisi büyükse. Tolerans içindeki fark loglanır ve kayıt yazılır.

**Fark analizi.** `provider_fees` satır bazlı olduğu için "tutmadı" demekle kalmayıp nerede
tutmadığını gösterebiliyor. Fatura kalem detayı varsa `provider_ref` üzerinden eşleştirilip
üç grup çıkarılır: faturada var/sende yok (kaçırılmış webhook), sende var/faturada yok
(sonraki döneme kaymış), ikisinde var/tutar farklı (en sık — genelde sağlayıcı ücret oranı
değişmiş, konfigürasyon eski kalmış; fark sabit oranlıysa neredeyse kesin budur).

**Çıkış durumları (üçü de manuel).**

- Fatura doğru, konfigürasyon eski → oran güncellenir, fatura olduğu gibi yazılır.
  Geçmiş `expected_amount` değerleri geriye dönük düzeltilmez.
- Fatura hatalı → sağlayıcıya itiraz, düzeltilmiş fatura gelene kadar ledger'a yazılmaz.
- Fark kabul ediliyor → yazılır, `note` alanına gerekçe düşülür.

**Mutabakat job'ıyla ilişkisi.** Bölüm 3 madde 7'deki mutabakat raporunun ikinci ayağı bu.
Birincisi clearing–settlement karşılaştırması (para tarafı), ikincisi expected–fatura
karşılaştırması (ücret tarafı). Stuck saga taraması gibi bunun da çıktısı rapor;
sistem düzeltmez, gösterir.

**Fatura idempotency'si.** `ledger_transactions.idempotency_key` = sağlayıcının fatura
numarası. Aynı faturayı iki kez işlemek manuel tetiklenen job'larda yüksek risk ve sonucu
doğrudan yanlış gider kaydı.

---

## 12. Kabul edilen sınırlamalar

Bilinçli olarak eksik bırakılanlar, README'de de yazılacak:

- Rate limiting in-memory. Çok instance'ta efektif limit instance başınadır.
  (Dağıtık limiter Redis gerektirir; bu projenin konusu değil.)
- Secret yönetimi `.env` + Docker Compose. Vault yok.
- Authn/authz yok (baseline madde A opsiyonel; konusu Auth uygulaması).
- Caching yok. Bakiye projeksiyonu cache değil, kalıcı read tablosu.
- Multi-tenancy yok. Person/business ayrımı hesap tipidir, tenancy değil.

**Ücret modellerinden bilinçli olarak elenenler** (düşünülmemiş değil, elenmiş):

- **Prepaid / bakiye düşümü** — sağlayıcıya önden yatırılan bakiyeden düşme.
  `provider_prepaid` hesabı gerektirir. SMS ve bazı KYC servislerinde yaygın,
  ödeme kurumlarında nadir.
- **Interchange / çok katmanlı ücret** — interchange + scheme fee + acquirer markup ayrı
  kalemler. Doğrudan acquirer ile çalışıldığında geçerli. Modellemesi `provider_fees`'e
  `fee_type` ile ayrı satır eklemek; ayrı bir yapı değil, o yüzden kolon şemada duruyor.
- **Gross settlement** — ücret ayrı direct debit ile tahsil ediliyor. Ledger açısından
  invoiced'dan farkı yok, sadece tetikleyici farklı.
- **Sabit dönemsel ücret** — aylık platform ücreti. `transaction_id` NULL olacağı için
  `provider_fees`'e girmez; doğrudan gider kaydı olarak ledger'a yazılır.

Şema genişlemeye kapalı değil: `settlement_model` CHECK'ine değer eklemek tek satırlık
migration, `fee_type` kolonu şimdilik hep `provider` ama yerinde duruyor.

---

## 13. Uygulama sırası

1. wallet-service çekirdeği: `accounts`, `ledger_transactions`, `ledger_entries`,
   `wallet_balances`, transfer + policy (limit, komisyon). Broker yok, saga yok.
2. Baseline'ın 12 maddesi bu tek servis üstünde (OTel, health, ProblemDetails,
   rate limiting, migration, graceful shutdown).
3. Top-up hattı: webhook (HMAC + inbox) → relay → RabbitMQ → consumer. Broker ilk burada.
4. Withdrawal saga + bank-service + compensation.
5. Scheduled job'lar: mutabakat, business özeti, stuck saga taraması.

Her adım bir sonrakine geçmeden çıkış kriterini (Bölüm 3 madde 10) karşılamalı.

---

## 14. Sistem hesaplarının ayrıştırılması: `accounts.provider`

**Karar.** `accounts` tablosuna `provider text NULL` kolonu eklenir. Sistem hesaplarında
sağlayıcıyı taşır (`nostro/garanti`, `provider_expense/stripe-fake`), `user_wallet`'ta NULL.
Tekillik `(account_type, provider, currency)` üzerinde, yalnızca sistem hesapları için.

**Gerekçe.** `ledger-schema.md` "`nostro` ve `provider_expense` sağlayıcı başına ayrı olabilir"
diyordu ama bunu taşıyacak kolon yoktu — `owner_id` sistem hesaplarında NULL, `account_type`
ise rolü söylüyor, sağlayıcıyı değil. Kolon olmadan iki `nostro` hesabı ayırt edilemez ve
madde 11'deki fatura uyuşmazlığı analizi (hangi sağlayıcı, hangi fatura) yapılamaz. Mutabakat
sağlayıcı bazında koştuğu için bu kolon opsiyonel bir kolaylık değil, ön koşul.

**`clearing` de sağlayıcı bazında.** Bölüm 3 madde 3 clearing'i "yolda olan para" diye tanımlıyor;
yolda olan paranın kimde olduğu bilinmezse settlement karşılaştırması yapılamaz. Aynı kolon
clearing için de dolar.

**Elenen alternatif.** Tek `code text UNIQUE` kolonu (`'nostro:garanti:TRY'`). Adresleme için
yeterli ama sorgulanamıyor — "garanti'nin tüm hesapları" string parse etmeyi gerektirir.
İşaret/`direction` kolonunda uygulanan tek-kaynak mantığının tersi: burada tek kolon,
üç ayrı bilgiyi (`type`, `provider`, `currency`) içine gömüp erişilemez kılıyor.

**Elenen alternatif.** `code` + `provider` birlikte. İki kaynak, tutarsızlaşır. `direction`
kolonunun elenme gerekçesiyle aynı (`ledger-schema.md`, `ledger_entries`).

---

## 15. `ledger_transactions.account_id` iç işlemlerde ne olur

**Karar.** Kolon NOT NULL kalır. Anlamı "isteği başlatan hesap" değil,
**işlemin idempotency kapsamı olan hesap**:

| `type`             | `account_id`                          | `idempotency_key`  |
| ------------------ | ------------------------------------- | ------------------ |
| transfer (5 tip)   | gönderen `user_wallet`                | client'ın key'i    |
| `topup`            | alıcı `user_wallet`                   | webhook `event_id` |
| `withdrawal`       | çeken `user_wallet`                   | client'ın key'i    |
| `refund` (comp.)   | aynı `user_wallet`                    | saga id            |
| settlement         | ilgili `clearing` (sağlayıcı bazında) | sağlayıcı batch ref|
| `provider_invoice` | ilgili `provider_expense`             | fatura numarası    |

**Gerekçe.** Kolonu nullable yapmak ilk akla gelen çözümdü ve sessizce bozuyor:
Postgres'te unique index içindeki NULL hiçbir NULL'a eşit sayılmaz, dolayısıyla
`(NULL, 'INV-2026-03')` iki kez insert edilebilir. Madde 11 fatura idempotency'sinin dayandığı
tek mekanizma bu index — nullable `account_id` onu tam da en riskli akışta devre dışı bırakır.

Sistem hesabını kapsam olarak kullanmak hem index'i canlı tutuyor hem de doğru soruyu
soruyor: "bu fatura bu sağlayıcının gider hesabına daha önce yazıldı mı".

**Sonuç.** `ledger-schema.md`'deki kolon yorumu güncellenir. Ayrı bir `scope_account_id`
kolonu eklenmez — aynı bilgi, iki isim.

---

## 16. Platform: .NET 10

**Karar.** Tek TFM `net10.0`, `Directory.Build.props`'ta merkezi. Servis `.csproj`'larında
`TargetFramework` yazılmaz.

**Gerekçe.** LTS, makinede kurulu (`10.0.201`), EF Tools 10.0.7 ile eşleşiyor. Çok TFM'li
build bu projede hiçbir şey kazandırmaz, `#if` dallanması getirir.