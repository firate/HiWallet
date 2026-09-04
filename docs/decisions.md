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

## 2. Optimistic lock `ledger_balances` üzerinde

**Karar.** Concurrency token `ledger_balances.version`. `ledger_entries` üzerinde
hiçbir lock veya version kolonu yok.

**Gerekçe.** `ledger_entries` append-only. Optimistic lock "okuduğumdan beri bu satır değişti mi"
sorusunu sorar; hiç UPDATE edilmeyen bir satırda bu soru anlamsız. İki INSERT birbiriyle
çakışmaz, ikisi de başarılı olur. Gerçek çakışma bakiye projeksiyonunda: iki eşzamanlı transfer
aynı gönderen için 500 okuyup ikisi de 400 yazmaya çalışır (lost update).

**Akış.**

```sql
-- 1. oku
SELECT balance, version FROM ledger_balances WHERE ledger_account_id = @from;   -- 500, v7

-- 2. uygulama: yeterli bakiye mi, limit aşılıyor mu, komisyon kaç

-- 3. ledger'a yaz (lock yok)
INSERT INTO ledger_entries (...) VALUES (@tx, @from, -100), (@tx, @to, +100);

-- 4. projeksiyonu güncelle
UPDATE ledger_balances
   SET balance = balance - 100, version = version + 1
 WHERE ledger_account_id = @from AND version = 7;
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

**Adlandırma.** Tablo önce `wallet_balances` idi; yanlıştı, çünkü yalnızca cüzdanların
değil TÜM ledger hesaplarının bakiyesini tutuyor — mutabakat `clearing`'e, rapor
`nostro`'ya bakıyor. `ledger_accounts` ile 1:1 olmasına rağmen ayrı tablo olarak kalıyor:
orası neredeyse hiç yazılmayan referans verisi, burası her transfer'de yazılan projeksiyon.
Birleşselerdi her transfer geniş satırı ve onun unique index'lerini güncellerdi (HOT update
ihtimali düşer, index şişer); ayrıca projeksiyonu ledger'dan yeniden inşa etmek
(`TRUNCATE` + replay) mümkün olmazdı.

---

## 3. Distributed lock yok

**Karar.** Redis distributed lock kullanılmaz. Wallet Redis'siz kurulabilir.

**Gerekçe.** Distributed lock, korunacak kaynak tek transactional sınırın dışındaysa gerekir.
Burada bakiye tek serviste tek Postgres'te. Postgres zaten satır kilidi, MVCC ve commit garantisi
veriyor; üstüne Redis lock koymak aynı garantiyi daha zayıf bir mekanizmayla tekrarlamak olur.
Redis lock'un doğruluğu TTL'e dayanıyor: TTL dolarsa lock başkasına geçer, ilk sahibi hâlâ
transaction içindedir, iki taraf birden yazar. Optimistic lock'ta bu kırılma yok çünkü kontrol
kaynağın kendisinde.

**Kapsam notu.** Distributed lock'un gerçekten gerektiği senaryo başka: korunacak kaynağın
tek transactional sınırın dışında olduğu durumlar (klasik örnek koltuk rezervasyonu —
seat hold). Buranın teması in-process consistency, o yüzden kapsam dışı.

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
`(ledger_account_id, idempotency_key)` UNIQUE. Ayrı bir idempotency store yok.

**Gerekçe.** Transfer senkron ve atomik: ledger yazımı ile idempotency kaydı aynı satırda,
aynı transaction'da. Ayrı bir tablo ikinci yazma ve ikinci tutarlılık noktası demek olurdu.
Withdrawal'da istek bir ledger transaction'ı değil bir saga başlatıyor, dolayısıyla kolonun
yeri saga tablosu. Saga'nın kendi `state` kolonu "iş nerede" bilgisini zaten taşıdığı için
replay'de saklanmış response gövdesi de gerekmiyor — saga id + güncel state dönülür.

**Kalıp.**

```sql
INSERT INTO withdrawal_sagas (..., ledger_account_id, idempotency_key)
VALUES (...)
ON CONFLICT (ledger_account_id, idempotency_key) DO NOTHING;
-- 0 satır → mevcut kaydı oku, state'ini dön (yeni saga BAŞLATMA)
```

**Elenen alternatif.** Önce `SELECT`, yoksa `INSERT`. Tek instance'ta bile TOCTOU açığı var.

**Neden composite.** Key'i client üretiyor. Yalnız `idempotency_key` UNIQUE olsaydı iki farklı
kullanıcının aynı key'i üretmesi durumunda birinin isteği diğerininkiyle karışırdı.

**Not.** Bu, `overview.md` madde 5'teki iki kademe idempotency'yi (inbox `event_id` UNIQUE +
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

**Hesap tipleri.** Tek `ledger_accounts` tablosunda `type`:
`user_wallet`, `clearing`, `revenue`, `nostro`, `provider_expense`.

**Kural.** Sistem hesapları negatife düşebilir; `user_wallet` düşemez.
Bu bir CHECK constraint değil, uygulama kuralı — çünkü clearing tasarımı gereği negatif duruyor
(yükleme akışında cüzdan `+100`, clearing `-100`).

**`ledger_accounts.type` ile `accounts.type` dik boyutlardır.** Birincisi hesabın ledger'daki
rolü, ikincisi sahibinin kim olduğu (`person` / `business`). Tek kolonda birleştirilmez:
ledger çekirdeğinin sorduğu soru "bu hesap negatife düşebilir mi", cevabı person ve business
için aynı. Transfer tipi (`p2p`/`p2b`/`b2p`/`b2b`) `accounts.type`'a bakar, ama bu policy
katmanının işi, çekirdeğin değil. (Adlandırma: madde 20.)

**`nostro` neden gerekli.** `overview.md` "settlement geldiğinde clearing sıfıra çekilir" diyor ama
neye karşı dengelendiğini söylemiyor. Cevap bu hesap: `clearing` yolda olan para,
`nostro` bankada duran gerçek para. Mutabakat, `nostro` bakiyesini banka ekstresiyle
karşılaştırarak yapılır.

**`revenue` ve `provider_expense` netleştirilmez.** Müşteriden alınan komisyon gelir,
sağlayıcıya ödenen ücret gider. Ayrı hesaplarda durur, net marj rapor seviyesinde hesaplanır.
Ayrı durmalarının en net gerekçesi compensation: banka fail olduğunda `revenue` ters kayıtla
iade edilir (`overview.md` madde 6 kuralı), `provider_expense` edilmez — banka işlemi denediyse ücreti
kesilmiştir. Tek hesapta netleşselerdi bu ayrım yapılamazdı.

---

## 7. Servis sınırı ve veritabanı ayrımı

**Karar.** wallet-service ve withdrawal-orchestrator ayrı veritabanı (en az ayrı schema).
Orchestrator wallet tablolarına doğrudan yazmaz.

**Gerekçe.** Aynı DB paylaşıldığında saga'nın anlamı kalmıyor; orchestrator er ya da geç
wallet tablolarına doğrudan yazmaya başlıyor ve compensation gereksizleşiyor.
`overview.md`'nin "çekirdeği tek boundary'de ACID tut, sadece kenarı dağıt" mesajı ancak sınır
gerçekten varsa gösterilebilir.

---

## 8. Deadlock önleme: satır güncelleme sırası

**Karar.** Bir transaction içinde birden fazla `ledger_balances` satırı güncelleniyorsa
her zaman `ledger_account_id` artan sırayla.

**Gerekçe.** Transfer iki satıra dokunuyor, komisyonluysa üçe. A→B ve B→A eşzamanlı gelir ve
satırlar farklı sırayla güncellenirse Postgres deadlock verir. Tek instance'ta düşük yükte
görünmez, yük altında ve çok instance'ta çıkar.

---

## 9. Retry politikası

**Karar.** Transfer handler'ında `DbUpdateConcurrencyException` için 3 denemelik retry,
her denemede projeksiyon yeniden okunur ve komisyon/limit yeniden hesaplanır.

**Gerekçe.** Eski `version` ile tekrar denemek sonsuza kadar başarısız olur —
retry'ın anlamı yeni anlık görüntüyle yeniden denemek.

**Ayrım.** Bu retry, saga'daki adım retry'ından (`overview.md` madde 6, transient banka hatası) ayrıdır.
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

**Mutabakat job'ıyla ilişkisi.** `overview.md` madde 7'deki mutabakat raporunun ikinci ayağı bu.
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
- Authn/authz yok (`baseline.md` madde A opsiyonel). Başlı başına bir konu; buranın teması değil.
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
   `ledger_balances`, transfer + policy (limit, komisyon). Broker yok, saga yok.
2. Baseline'ın 12 maddesi bu tek servis üstünde (OTel, health, ProblemDetails,
   rate limiting, migration, graceful shutdown).
3. Top-up hattı: webhook (HMAC + inbox) → relay → RabbitMQ → consumer. Broker ilk burada.
4. Withdrawal saga + bank-service + compensation.
5. Scheduled job'lar: mutabakat, business özeti, stuck saga taraması.

Her adım bir sonrakine geçmeden çıkış kriterini (`overview.md` madde 10) karşılamalı.

---

## 14. Sistem hesaplarının ayrıştırılması: `ledger_accounts.provider`

**Karar.** `ledger_accounts` tablosuna `provider text NULL` kolonu eklenir. Sistem hesaplarında
sağlayıcıyı taşır (`nostro/garanti`, `provider_expense/stripe-fake`), `user_wallet`'ta NULL.
Tekillik `(type, provider, currency)` üzerinde, yalnızca sistem hesapları için.

**Gerekçe.** `ledger-schema.md` "`nostro` ve `provider_expense` sağlayıcı başına ayrı olabilir"
diyordu ama bunu taşıyacak kolon yoktu — `account_id` sistem hesaplarında NULL, `type` ise
rolü söylüyor, sağlayıcıyı değil. Kolon olmadan iki `nostro` hesabı ayırt edilemez ve
madde 11'deki fatura uyuşmazlığı analizi (hangi sağlayıcı, hangi fatura) yapılamaz. Mutabakat
sağlayıcı bazında koştuğu için bu kolon opsiyonel bir kolaylık değil, ön koşul.

**`clearing` de sağlayıcı bazında.** `overview.md` madde 3 clearing'i "yolda olan para" diye tanımlıyor;
yolda olan paranın kimde olduğu bilinmezse settlement karşılaştırması yapılamaz. Aynı kolon
clearing için de dolar.

**Elenen alternatif.** Tek `code text UNIQUE` kolonu (`'nostro:garanti:TRY'`). Adresleme için
yeterli ama sorgulanamıyor — "garanti'nin tüm hesapları" string parse etmeyi gerektirir.
İşaret/`direction` kolonunda uygulanan tek-kaynak mantığının tersi: burada tek kolon,
üç ayrı bilgiyi (`type`, `provider`, `currency`) içine gömüp erişilemez kılıyor.

**Elenen alternatif.** `code` + `provider` birlikte. İki kaynak, tutarsızlaşır. `direction`
kolonunun elenme gerekçesiyle aynı (`ledger-schema.md`, `ledger_entries`).

---

## 15. `ledger_transactions.ledger_account_id` iç işlemlerde ne olur

**Karar.** Kolon NOT NULL kalır. Anlamı "isteği başlatan hesap" değil,
**işlemin idempotency kapsamı olan hesap**:

| `type`             | `ledger_account_id`                          | `idempotency_key`  |
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
tek mekanizma bu index — nullable `ledger_account_id` onu tam da en riskli akışta devre dışı bırakır.

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

---

## 17. Para birimi bütünlüğü: composite FK + currency başına zero-sum

**Karar.** İki değişiklik birlikte:

1. `ledger_entries` ve `ledger_balances`, `ledger_accounts`'a `(ledger_account_id, currency)` composite
   FK ile bağlanır. Hedef `uq_ledger_accounts_id_currency UNIQUE (id, currency)`.
2. Zero-sum trigger'ı `GROUP BY currency` ile çalışır; her para birimi kendi içinde
   sıfırlanmalıdır.

**Problem.** `currency` iki yerde duruyordu — `accounts` ve `ledger_entries` — ve senkron
tutan hiçbir şey yoktu. `ledger_account_id` sadece `REFERENCES accounts(id)` idi: "böyle bir hesap
var mı" diye soruyor, "bu hesap bu para biriminde mi" diye sormuyordu. Trigger da
`SUM(amount)` yapıp para birimine bakmıyordu. İkisi birleşince:

```sql
INSERT INTO ledger_entries VALUES
  (@tx, cüzdan,   +100, 'TRY'),
  (@tx, clearing, -100, 'USD');
-- SUM = 0. Trigger "dengeli" deyip geçiriyor.
```

100 TRY yoktan var oluyor, karşılığında 100 USD borç yazılıyor. Ne kadar para basıldığı
kura bağlı. Sistemin tüm iddiası "para yoktan var olmaz, taşınır" — bu tam onu deliyordu,
üstelik en sessiz şekilde: hata yok, log yok, trigger susuyor.

**Neden FK, neden trigger değil.** FK declarative ve index'e dayanıyor; plpgsql
çalıştırmıyor. Trigger yalnızca FK'nın ifade edemediği şey için kullanılır — zero-sum
gibi. Currency eşleşmesi FK'nın tam olarak ifade edebildiği bir şey.

**FK'ya kasten gereksiz kolon.** `ledger_account_id` tek başına hesabı zaten buluyor.
`currency`'yi FK'ya eklemek DB'ye "bu iki değer o satırda BİRLİKTE bulunsun" dedirtiyor.
Yani soru "hesap var mı" değil, "entry'nin söylediği para birimi hesabın söylediğiyle
aynı mı". `uq_ledger_accounts_id_currency` de bu yüzden var — tekillik amacı yok (`id` zaten PK),
tek işi FK hedefi olabilmek.

**Bedava gelen.** FK varsayılan `ON UPDATE NO ACTION` ile geliyor: bir hesabın
`currency`'sini değiştirmek, o hesapta entry varsa reddediliyor. Yani hesap ilk hareketi
gördükten sonra para birimi donuyor. Ayrı kural yazmaya gerek kalmıyor —
`UPDATE accounts SET currency='USD'` gibi tek satırlık bir "düzeltme" geçmişteki tüm TRY
kayıtlarını sessizce çeviremiyor.

**Elenen alternatif.** `ledger_entries.currency`'yi tamamen kaldırmak (normalize et,
hesaptan oku). Tek kaynak olurdu ama her ledger sorgusu sırf para birimini bilmek için
`accounts`'a join olurdu. Kolon işe yarıyor; klasik cevap "denormalize et, constraint ile
zorla".

**Elenen alternatif.** Eşleşmeyi yalnızca uygulamada kontrol etmek. Uygulama tarafı zaten
doğru (`Money` farklı para birimlerini toplamıyor, `LedgerTransaction.AssertBalanced()`
currency başına ayrı topluyor) — ama ledger'a yazan tek yol uygulama değil: migration,
düzeltme script'i, ileride bir admin aracı. Madde 5'in mantığı burada da geçerli, asıl
duvar DB'de olmalı.

**Maliyet.** `accounts` üzerinde ikinci bir btree index; tablo küçük ve yazma nadir,
ihmal edilebilir. Sıcak yolda değişen bir şey yok: `ledger_entries` INSERT'ünde FK
kontrolü zaten `accounts_pkey`'e bir index probe yapıyordu, artık
`uq_ledger_accounts_id_currency`'ye yapıyor — aynı sayıda probe.

**Doğrulandı.** DDL homelab'daki Postgres 17'de koşturuldu, 11 senaryonun hepsi beklendiği
gibi davrandı. Özellikle iki şüpheli nokta teyit edildi: (a) `CONSTRAINT ... UNIQUE`
composite FK hedefi olarak kabul ediliyor — `CREATE UNIQUE INDEX` yerine constraint
yazılması bu yüzden şart, (b) karışık para birimli "toplamı sıfır" işlem artık `P0001`
ile reddediliyor. Ayrıca hareket görmüş bir hesabın `currency`'sini değiştirme denemesi
FK tarafından engellendi — yukarıda "bedava gelen" denen davranış gerçekten geliyor.
---

## 18. Başarısız transferin sağlayıcı ücreti: `FeeOnFailure`

**Karar.** Müşteri komisyonu compensation'da **koşulsuz** iade edilir — konfigüre edilmez.
Konfigüre edilen şey başarısız denemenin **sağlayıcı ücretini kimin yüklendiği**, ve bu
sağlayıcı bazındadır: `FeeOnFailure: Charged | Waived`.

- `Charged` — banka başarısız denemeye de ücret kesiyor, maliyeti biz üstleniyoruz.
  Deneme anında `provider_fees` satırı yazılır; ücret settlement'ta netleşerek ya da
  faturayla gelir ve `provider_expense` bacağı olarak ledger'a girer.
- `Waived` — sözleşme gereği kesmiyor. `provider_fees` satırı yazılmaz.

**Neden müşteri komisyonu konfigüre edilmiyor.** Başarısızlığın sebebi ya bizde ya
bankadadır. Müşteri kaynaklı tek gerçekçi senaryo yanlış IBAN, o da mod-97 checksum'ı ile
sınırda eleniyor — saga başlamıyor, bankaya istek gitmiyor, ücret doğmuyor. Geriye kalan
(yapısal olarak geçerli ama kapalı hesap) nadir; kalıcı bir konfigürasyon kolunu hak etmiyor.
Gerçekleşmemiş bir hizmet için komisyon almak zaten ödeme kurumlarının pratiği değil.

**Bağımlılık.** Bu kural IBAN doğrulamasının sınırda gerçekten yapılmasına yaslanıyor.
Kalkarsa müşteri yazım hataları bankaya ulaşır, gerçek ücret doğurur ve "başarısızlık asla
müşterinin suçu değil" cümlesi yalan olur. `overview.md` madde 6'da da yazılı.

**Neden sağlayıcı bazında, global değil.** Bu bir sözleşme şartı; iki bankayla farklı
şartlarda çalışmak gerçekçi. Madde 10'daki `FeeSettlement` ile aynı yerde, aynı kalıpta.

**Yan fayda: mutabakat akıllanıyor.** `Waived` bir sağlayıcının faturasında başarısız
transfer ücreti belirirse beklenen satır olmadığı için madde 11'in "faturada var/sende yok"
dalına düşer. Bu artık gürültü değil, **sözleşmeye aykırı bir kalem** — itiraz sinyali.
`Charged`'da ise beklenen satır zaten var, eşleşir, alarm çalmaz.

**Elenen alternatif.** Müşteri komisyonunu da bayrakla konfigüre etmek. Nadir bir durumu
kalıcı karmaşıklıkla ödemek olurdu; ayrıca `CLAUDE.md`'deki koşulsuz kuralı delerdi.

**Elenen alternatif.** Başarısızlık sebebine göre karar vermek (`BankRejected` → iade,
`InvalidBeneficiary` → iade etme). Gerçeğe daha yakın ama bank-service'in güvenilir sebep
kodu üretmesini ve saga'nın bunu taşımasını gerektiriyor. Fake sağlayıcıyla üretilen sebep
kodu üzerine iş kuralı kurmak, doğrulanmamış bir varsayımı şemaya gömmek olur.

---

## 19. Ledger tutarları minor unit'te, oranlar serbest

**Karar.** `Money` her zaman para biriminin minor unit'ine oturur (`Currency.MinorUnit`,
TRY için 2). Oranlar (komisyon oranı, sağlayıcı ücret oranı) `Money` değil, düz `decimal` —
basamak sınırı yok.

**Gerekçe.** Wallet'ta birim fiyat kavramı yok; satır kalemi yok, her değer bir tutar.
Ledger'a yazılan her tutar müşterinin gerçekten tutabileceği ve çekebileceği bir şey olmalı.
Komisyon 4 haneye yuvarlanırsa (`33,33 × %2,9 = 0,966570 → 0,9666`) cüzdanda çekilemeyen
bakiye oluşuyor: banka 2 haneden fazlasını kabul etmiyor, aradaki kalıntı ne ödenebiliyor ne
de ledger'dan çıkarılabiliyor. "Tam bakiyeyi çek" dendiğinde ya kalıntı kalıyor ya zero-sum
bozuluyor.

Oran bir çarpan, para değil — kimseye ödenmiyor, ledger'a yazılmıyor, yalnızca hesap
sırasında yaşıyor. Hassasiyeti kısıtlamak için sebep yok.

**Şema değişmiyor.** Kolon `numeric(19,4)` kalıyor (madde 6). Kolonun 4 tutabilmesi
uygulamanın 4 yazması gerektiği anlamına gelmiyor; 2 fazla hane emniyet payı. Kısıt tipte,
şemada değil.

**Kabul edilen sonuç.** Yuvarlama farkı kaybolmuyor, yeri değişiyor: `0,966570 → 0,97`
yazınca kurum lehine 0,00343 yuvarlanmış oluyor ve binlerce işlemde birikiyor. Madde 11 bu
farkı zaten öngörmüş ("kuruş farkı kaçınılmaz, eşik oransal"), sistem bunu bekliyor.

**Yuvarlama yönü `AwayFromZero`.** Banker's rounding kurum lehine sistematik sapma üretmiyor
ama "yarımı aşağı yuvarladık" tartışması açıyor; `AwayFromZero` müşteri açısından
öngörülebilir.

---

## 20. Bir hesabın aynı para biriminde birden fazla cüzdanı olabilir

**Karar.** İki seviye: `accounts` (müşteri hesabı) ve `ledger_accounts` (bakiye tutabilen
her şey). Bir hesabın altında istediği kadar cüzdan durabilir ("Birikim", "Günlük", "Kira"),
aynı para biriminde bile — `ledger_accounts (account_id, currency)` üzerinde tekillik YOK.

**Neden ledger tarafı tek tablo.** `ledger_entries` tek bir FK hedefine işaret etmek zorunda:
bir transfer'in bacakları hem cüzdan hem `revenue` olabiliyor. Cüzdanlar ve sistem hesapları
ayrı tablolarda olsaydı polimorfik FK gerekirdi ve referential integrity çökerdi — madde
17'de kapattığımız delik sınıfının aynısı. Bu yüzden "bakiye tutabilen her şey" tek tabloda;
cüzdan onun `type = 'user_wallet'` olan alt kümesi.

**1. Günlük limit hesap bazında uygulanır, cüzdan bazında değil.** Cüzdan bazında olsaydı
limit hiçbir şey korumazdı: günlük 10.000 limiti olan biri beş cüzdan açıp 50.000 gönderirdi.
Kural teknik olarak çalışır, iş olarak boşa çıkardı. `LimitPolicy` hesap kimliğini alır ve
`spentToday` o hesabın **tüm cüzdanlarından** toplanır. Aynısı KYC eşikleri için de geçerli.

Bedeli: `ix_ledger_accounts_account` artık sıcak yolda — her transfer'de hesabın cüzdanları
toplanıyor. Tek cüzdan varsayımında bu sorgu hiç olmayacaktı.

**2. person/business ayrımı `accounts.type`'ta, tek yerde.** Önceden cüzdanın üstündeydi; tek
cüzdan varken sorunsuzdu, N cüzdan olunca aynı bilgi N satıra kopyalanacaktı ve senkron tutan
hiçbir şey olmayacaktı. Bir cüzdan `person`, diğeri `business` olabilirdi — transfer tipi
(`p2p`/`p2b`) buna baktığı için aynı müşteri hangi cüzdanını kullandığına göre farklı
politikaya tabi olurdu.

Çözüm denormalizasyon + composite FK DEĞİL, doğrudan normalizasyon: bilgi tek yerde durunca
sapacak ikinci bir kopya kalmıyor ve FK numarasına gerek olmuyor. Policy katmanı person/business
bilgisini `accounts`'a bakarak alır — PK araması, ucuz.

**3. Cüzdanın `name`'i var, `user_wallet`'ta zorunlu.** Aynı hesabın üç TRY cüzdanı uuid
dışında ayırt edilemezdi. Ledger için gerekli değil, ürün için gerekli.

**Adlandırma.** "account" müşteri tarafına ayrıldı, ledger tarafı `ledger_accounts` oldu —
`ledger_transactions` ve `ledger_entries` ile aynı önekte buluşuyor, üçü birlikte "ledger'ın
tabloları" diye okunuyor. Kolon adları da buna uydu: `ledger_entries.ledger_account_id`,
`ledger_transactions.ledger_account_id`.

Elenen alternatif: `owners` + `accounts` (müşteri "owner", ledger tarafı "account"). Muhasebe
dilinde doğru ama konuşma dilinde "account" müşteriyi gösteriyor ve çakışma üç ayrı tartışmada
kafa karıştırdı. Elenen alternatif: `accounts` + `owner_account_id` kolonu — `accounts` tablosu
ledger tarafını gösterirken `owner_account_id`'nin başka bir tabloya işaret etmesi okuyucuyu
doğrudan yanlış yere yollardı. Elenen alternatif: üç tablo (`ledger_accounts` supertype +
`wallets` + `system_accounts`). Integrity korunur, isimler tam konuşma dili — ama her cüzdan
okumasında join, her cüzdan yaratmada iki insert. Bu ölçekte ağır.

**Açık bırakılan.** `(account_id, name)` üzerinde tekillik yok; aynı hesap iki cüzdanına da
"Birikim" diyebilir. İsim ayırt etmek için varsa bu onu boşa çıkarıyor, ama bir ürün kararı
ve şimdi verilmedi.

**Doğrulandı.** Homelab'daki Postgres 17'de 17 senaryo koşturuldu, hepsi geçti. Bu maddenin
kendi testleri: aynı hesap + aynı currency ile ikinci cüzdan **açılabiliyor** (T12); sistem
hesabına `account_id` verilemiyor (T13, `ck_ledger_accounts_account`); var olmayan hesaba
cüzdan bağlanamıyor (T14); adsız cüzdan ve adlı sistem hesabı reddediliyor (T15/T16);
cüzdanı olan bir hesap silinemiyor (T17). Madde 17'nin para birimi testleri de yeni
kolon adlarıyla geçiyor.

---

## 21. Idempotency kapısı policy'den ÖNCE

**Karar.** Transfer akışında `idempotency_key` kontrolü limit ve komisyon
hesaplamasından ÖNCE yapılır. `ledger-schema.md`'deki referans akış bunu sonra
gösteriyordu; o sıralama bozuk.

**Gerekçe.** Tekrar eden bir istek hiçbir kuralı yeniden değerlendirmemeli, sadece
mevcut işlemi dönmeli. Policy önce koşarsa şu senaryo kırılıyor: günlük limit 10.000,
müşteri 10.000 gönderiyor, ağ kopuyor, client aynı `Idempotency-Key` ile tekrar
deniyor. İkinci istekte `spentToday` artık 10.000 — limit aşımı görünüyor ve `422`
dönüyor. Oysa doğru cevap ilk transferin kimliği.

Hata sessiz değil ama yanlış: client "limit doldu" sanıyor, gerçekte işlemi başarılı.

**Sonuç.** `ledger-schema.md`'deki referans akış düzeltildi. Test:
`Transfer_LimitAsimindanSonraTekrar_LimitDegilMevcutIslemiDoner`.

---

## 22. Limit cüzdandan çıkan TOPLAMA uygulanır (komisyon dahil)

**Karar.** Günlük ve işlem limiti `amount + komisyon` üzerinden değerlendirilir.

**Gerekçe.** Limitin koruduğu şey "bu hesaptan bugün ne kadar para çıktı". Cüzdandan
çıkan tutar komisyon dahil olan; müşterinin bakiyesinden eksilen de o.

Ayrıca ölçülebilir olmalı: `spentToday` ledger'daki debit bacaklarının toplamı, tek
sorgu. Komisyon hariç tutulsaydı her işlem için debit bacağından `revenue` bacağını
çıkarmak gerekirdi — aynı sorgu, gereksiz karmaşıklık, ve iki hesaplama yolu
(uygulama ile rapor) ayrışma riski.

**Not.** Bu, kodda daha önce ters yönde bir varsayım olarak duruyordu ("limit
müşterinin gönderdiği tutara uygulanır, kurumun kestiği komisyona değil"). İkisi de
savunulabilir; ölçülebilirlik terazi bu tarafa yattı.

---

## 23. Bilinen darboğaz: `revenue` hesabı komisyonlu akışlarda hotspot

**Durum.** Komisyon kesilen her transfer tek bir `revenue` bakiye satırını güncelliyor.
Optimistic lock o satırda olduğu için, eşzamanlı komisyonlu transferler birbirini
çakıştırıp retry'a düşürüyor — cüzdanları farklı olsa bile.

**Şu an sorun değil.** Komisyon yalnızca `payment` ve `b2b`'de var, hacmin çoğunluğu
`p2p` ve orada `revenue` bacağı hiç yazılmıyor. 500 eşzamanlı transfer testi p2p
olduğu için bu yolu hiç zorlamıyor.

**Sorun olursa çözümü.** `revenue`'yu tek satır olmaktan çıkarmak: gün veya shard
bazında bölmek (`revenue` bakiyesi bunların toplamı), ya da komisyonu ledger'a anında
yazıp bakiye projeksiyonunu periyodik toplamaya bırakmak. İkincisi "bakiye ledger'dan
türetilir" ilkesiyle zaten uyumlu.

**Neden şimdi yapılmıyor.** Ölçülmemiş bir darboğaz için tasarım karmaşıklığı eklemek;
sorunun gerçekten var olduğunu gösteren bir test yok. Buraya yazılıyor ki komisyonlu
akışlarda yavaşlama görülürse ilk bakılacak yer belli olsun.

---

## 24. `wallet_app` yetkileri migration'da, elle kurulum adımında değil

**Karar.** `GRANT`'ler de `REVOKE` gibi migration içinde. Şema adı sabit değil,
`current_schema()` ile dinamik.

**Gerekçe.** Yetkiler elle çalıştırılan bir kurulum adımı olarak bırakılmıştı ve ilk
gerçek çalıştırmada uygulama `permission denied for table ledger_accounts` ile karşılandı.
Her yeni ortamda aynı hata çıkardı. `REVOKE` zaten migration'daydı; `GRANT`'in ayrı yerde
durması ikisinin ayrışmasına ve "REVOKE var ama GRANT yok" gibi yarım durumlara yol açıyor.

Migration `wallet_owner` ile koşuyor ve tabloların sahibi o; sahip kendi tablolarında
GRANT verebiliyor, superuser gerekmiyor.

**Sıra önemli.** Toplu `GRANT ... ON ALL TABLES` `ledger_entries`'e de UPDATE/DELETE
veriyor; `REVOKE` en sonda, onu geri alıyor.

**Şema adı neden dinamik.** Önce `public` yazılmıştı. Integration testler koşu başına ayrı
schema'da migrate ettiği için yetkiler yanlış şemaya verilirdi — ve test bunu FARK ETMEZDİ,
çünkü testler `wallet_owner` ile bağlanıyor.

**Doğrulandı (deneysel).** `ledger_entries` üzerinde iki rol denendi:

| rol | SELECT | UPDATE | DELETE |
| --- | --- | --- | --- |
| `wallet_app` | izin var | `42501` reddedildi | `42501` reddedildi |
| `wallet_owner` | izin var | **izin var** | **izin var** |

Alt satır madde 5'in gerekçesinin ispatı: sahip rolüne `REVOKE` işlemiyor. Tek rol
kullanılsaydı append-only kuralı tamamen süs olurdu.

**Testle doğrulandı.** `AppRolePrivilegeTests` uygulama rolüyle bağlanıyor: `UPDATE` ve
`DELETE` `42501` alıyor, `SELECT`/`INSERT` çalışıyor, ve normal bir transfer baştan sona
geçiyor. Son test olmasaydı "her şeyi revoke et" de yeşil görünürdü.

Kimlik ayrı bir ortam değişkeninden değil, `ConnectionStrings:Wallet`'tan alınıp test
veritabanının host/adıyla birleştiriliyor — üçüncü bir parola dolaşıma sokmamak için.
Rol kurulu değilse testler atlanıyor (`Assert.SkipUnless`): kurulumu zorunlu kılmak
yerine, varsa doğrulanıyor.
