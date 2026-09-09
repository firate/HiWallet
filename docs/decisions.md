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

## 2. Optimistic lock yalnızca güncellenen satırlarda

**Karar.** Concurrency token wallet-service'te `ledger_balances.version`,
orchestrator'da `withdrawal_sagas.version`. Başka hiçbir tabloda yok — özellikle
`ledger_entries` üzerinde hiçbir lock veya version kolonu yok.

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

**Orchestrator'daki ikinci token.** `withdrawal_sagas` satırı da aynı testi geçiyor:
gerçekten UPDATE ediliyor ve birden fazla yazarı var. Aynı saga'ya banka cevabı ile
stuck-saga taraması aynı anda gelebiliyor; ikisi de "oku, karar ver, yaz" yapıyor ve
version olmasa ikincisi birincisinin geçişini sessizce ezerdi. Kayıp bakiye
güncellemesiyle aynı hata sınıfı, farklı tablo.

Kural şu, tablo listesi değil: **token yalnızca gerçekten UPDATE edilen ve birden fazla
yazarı olan satırda olur.** `ledger_entries`'te yok çünkü append-only; `accounts`'ta yok
çünkü tek yazarı var. "İleride lazım olur" diye eklenmez — her token bir retry yolu
demek ve test edilmeyen retry yolu çalışmayan retry yoludur.

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

Bu ayrımın bir bedeli var ve bedelsizmiş gibi okunmamalı: gerekçesi, seçilmeyen
alternatifi ve faturası **madde 33'te**.

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

1. ✅ wallet çekirdeği: `accounts`, `ledger_transactions`, `ledger_entries`,
   `ledger_balances`, transfer + policy (limit, komisyon). Broker yok, saga yok.
2. ✅ Baseline'ın 12 maddesi (OTel, health, ProblemDetails, rate limiting, migration,
   graceful shutdown). Polly / dış servis dayanıklılığı ertelendi — henüz dış HTTP
   bağımlılığı yok.
3. ✅ Top-up hattı: webhook (HMAC + inbox) → relay → RabbitMQ → consumer. Broker ilk
   burada. Zincir gerçek bir broker'a karşı uçtan uca doğrulandı.
4. ✅ Withdrawal saga + bank-service + compensation. Zincir gerçek bir broker'a
   karşı uçtan uca doğrulandı; compose'dan ayağa kalkıyor, telafi yolu compose
   üzerinden henüz koşturulmadı. Alt adımlar aşağıda.
5. ✅ Settlement akışı + scheduled job'lar: mutabakat, business özeti, stuck saga
   taraması. Alt adımlar aşağıda.

Her adım bir sonrakine geçmeden çıkış kriterini (`overview.md` madde 10) karşılamalı.

### Adım 4'ün alt adımları

Sıra bağımlılığa göre: saf olanlar önce, dış dünyaya bağlananlar sonra. Her biri
kendi başına commit'lenebilir ve derlenebilir olmalı.

| # | ne | durum |
| --- | --- | --- |
| 4.1 | `Iban` değer tipi, mod-97 (madde yok — `overview.md` madde 6'nın taşıyıcısı) | ✅ |
| 4.2 | Saga state machine: durumlar, geçişler, çelişki ayrımı (madde 31) | ✅ |
| 4.3 | Mesaj sözleşmeleri: üç komut, beş event | ✅ |
| 4.4 | Withdrawal topolojisi + paylaşılan `MessagePublisher` | ✅ |
| 4.5 | Orchestrator kalıcılığı: `withdrawal_sagas`, `withdrawal_outbox`, migration (madde 32) | ✅ |
| 4.6 | Orchestrator API: `POST /v1/withdrawals`, idempotency, IBAN sınırda | ✅ |
| 4.7 | Outbox relay + event tüketicisi (saga'yı ilerleten taraf) | ✅ |
| 4.8 | wallet-service komut handler'ları: `DebitForWithdrawal`, `RefundWithdrawal` + ters kayıt, `processed_messages` | ✅ |
| 4.9 | `bank-service` (fake): komut tüketir, senaryo tetikleyicileriyle dört sonuç üretir | ✅ |
| 4.10 | Uçtan uca testler: wallet ve bank ile TAM zincir (orchestrator tarafı 4.7'de kapandı) | ✅ |
| 4.11 | Compose servisleri, `.env.example`, dokümanlar | ✅ |

**4.8 en riskli adım.** Ters kayıt üç bacaklı olmak zorunda (cüzdan, clearing,
`revenue`) ve `revenue` bacağını atlamak iki bacakla da DENGELİ bir kayıt üretiyor —
trigger susuyor, zero-sum korunuyor, ama müşteri gerçekleşmemiş bir işlemin
komisyonunu ödemiş oluyor. Sessiz ve para kaybettiren kusur; testi bu bacağı ayrıca
doğrulamalı (`overview.md` madde 6).

### Adım 5'in alt adımları

Mutabakat, `overview.md` madde 7'de "clearing bakiyesi ile sağlayıcının settlement
kayıtları karşılaştırılır" diye tarif ediliyor — ama settlement akışı hiç yazılmadı.
`LedgerTransactionType.Settlement` enum'da duruyor, onu yazan kod yok. Yani
karşılaştıracak ikinci taraf yok; mutabakat job'ı bugün yazılsa yalnızca kendi
verisine bakardı ve "tutuyor" demekten başka bir şey söyleyemezdi.

**Karar: önce settlement akışı, sonra mutabakat.** Sıra bu yüzden ikiye ayrılıyor —
settlement'a bağlı olmayan job'lar önce, çünkü onlar bugün değer üretiyor ve job
altyapısını da yerine oturtuyorlar.

| # | ne | durum |
| --- | --- | --- |
| 5.1 | Job altyapısı: `PeriodicTimer`, `pg_try_advisory_lock` tekilliği, graceful shutdown (madde 3) | ✅ |
| 5.2 | Takılmış saga taraması — madde 33'ün zorunlu tamamlayıcısı | ✅ |
| 5.3 | Business günlük özeti: hacim, işlem sayısı, kesilen komisyon | ✅ |
| 5.4 | `provider_fees` tablosu + `FeeSettlement: Net \| Invoiced` konfigürasyonu (madde 10) | ✅ |
| 5.5 | Settlement alımı ve ledger kaydı (top-up): clearing kapanır, `nostro` hareket eder | ✅ |
| 5.5b | Çekim settlement'ı: banka ücreti saga üzerinden dönüyor, ayrı akış | ✅ |
| 5.6 | Fatura işleme (invoiced model) + uyuşmazlıkta `PendingReview` (madde 11) | ✅ |
| 5.7 | Mutabakat raporu: clearing vs settlement, yaşlanan kalemler | ✅ |

**5.2 opsiyonel değil.** Ayrı orchestrator veritabanı kararının (madde 7 ve 33)
faturası iki veritabanı arasında ayrışma ihtimali; karşılığı bu tarama. Yazılmazsa
`bank_transfer_pending`'de asılı kalmış bir çekimi hiçbir şey yakalamaz — hata log'u
yok, saga "bekliyor" görünüyor, müşteri parasını göremiyor.

**Wallet tarafındaki job'lar `wallet-consumer`'da.** Ayrı bir `wallet-jobs`
deployable'ı AÇILMIYOR: madde 28'in ölçütü erişim seviyesi ve job'ların da ingress'i
yok, aynı ledger'a aynı kütüphaneyle yazıyorlar. "Aynı maruziyet bölünmez" kuralı iki
yöne de işliyor. Consumer ölçeklendiğinde job'ın iki kez koşmasını engelleyen şey
deployable ayrımı değil, advisory lock (5.1).

**5.5 top-up settlement'ı; çekim tarafı 5.5b.** İkisi aynı adım değil: top-up'ta
ücreti bildiren taraf sağlayıcı ve bilgi doğrudan webhook'la geliyor. Çekimde
bildiren taraf banka ve bilgi saga üzerinden dönmek zorunda — yeni bir event,
saga'da yeni bir alan ve wallet'a yeni bir komut demek. Aynı dalda yapmak, çalışan
bir akışı yazılmamış bir akışın riskine bağlardı.

**5.5'in giriş noktası `topup-webhook`.** Settlement de sağlayıcıdan gelen, imzalı,
IP kısıtlı bir bildirim — top-up webhook'uyla aynı maruziyet. Yeni bir public uç
açmak ya da wallet-api'ye sağlayıcı yüzeyi eklemek madde 28'i deler.

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
| `topup`            | alıcı `user_wallet`                   | `provider:event_id`|
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

**Doğrulandı.** DDL gerçek bir Postgres 17'de koşturuldu, 11 senaryonun hepsi beklendiği
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

**Doğrulandı.** Gerçek bir Postgres 17'de 17 senaryo koşturuldu, hepsi geçti. Bu maddenin
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

---

## 25. Ledger'a yazan kodun tek kopyası olur

**Karar.** `ledger_transactions`, `ledger_entries` ve `ledger_balances`'a yazan mantık
tek yerde: `WalletService.Core`. Kaç process bu kütüphaneyi çalıştırırsa çalıştırsın
(bugün `wallet-api` ve `wallet-consumer`) yazan **kod** tek.

**Gerekçe.** Veritabanı invariant'ların hepsini zorlamıyor. Zorladıkları:

- `ledger_entries` append-only (`REVOKE`, sahip olmayan role)
- transaction başına para birimi başına sıfır toplam (deferred trigger)
- entry currency'si hesabınkiyle aynı (composite FK)
- `(ledger_account_id, idempotency_key)` tekilliği

Zorlamadıkları — ve tehlikeli olan bunlar:

- **Cüzdan negatife düşemez.** CHECK değil, `LedgerBalance.Apply`'daki bir `if`
  (madde 6: sistem hesapları düşebilmeli, o yüzden kolon üzerinde constraint olamıyor).
- **Bakiye ledger'a yazmadan güncellenmez ve ledger'a yazıp bakiyeyi güncellemeden
  bırakılamaz.** Bu ilişkiyi hiçbir şema nesnesi tutmuyor.
- **Bakiye satırları `ledger_account_id` artan sırayla güncellenir** (madde 8).
- **`version` her yazımda birer artar** — optimistic lock buna dayanıyor, DB üretmiyor.

Somut kırılma: ikinci bir yazar ledger'a entry yazıp `ledger_balances`'ı güncellemeyi
atlarsa DB'de hiçbir şey hata vermez. Zero-sum korunur, append-only korunur, FK
korunur — ve müşteri parasını göremez. Fark edilmesi için bakiyelerin ledger'dan
yeniden hesaplanması gerekir.

**Sınır şema değil, kod.** Şema sınır olsaydı ("kim yazarsa yazsın DB tutar") çok
yazarlı yapı sorunsuz olurdu. İnvariant'ların bir kısmı yalnızca kodda yaşadığı için
o kodun ikinci kopyası olamaz.

**Kaç deployable olduğu ayrı bir soru** ve cevabı madde 28'de. İki host aynı
kütüphaneyi çalıştırdığı sürece bu madde ihlal edilmiş olmuyor.

**Elenen alternatif.** Ayrı tüketici + wallet-api'ye senkron HTTP çağrısı. Broker'ın
sağladığı geri baskıyı ve tekrar denemeyi HTTP katmanında yeniden kurmayı
gerektirirdi; kazancı yalnızca bir kutu daha olurdu.

---

## 26. Sağlık kontrolünde `failureStatus` bağımlılığın kritikliğine göre seçilir

**Karar.** Bir bağımlılık olmadan servis **hiçbir** iş yapamıyorsa `Unhealthy`;
yalnızca bir yan akış duruyorsa `Degraded`.

| deployable | Postgres | RabbitMQ |
| --- | --- | --- |
| `wallet-api` | `Unhealthy` | *kontrol yok — bağımlılık da yok* |
| `topup-webhook` | `Unhealthy` | `Degraded` |
| `wallet-consumer` | `Unhealthy` | `Unhealthy` |

**Gerekçe.** `Unhealthy` readiness'ı düşürür ve orchestrator servisi trafikten çeker.
Bu, çalışmaya devam edebilecek yolları da kapatmak demek — arızayı olduğundan büyük
yapar. `Degraded` durumu sağlık çıktısında görünür kılıyor ama uç `200` dönmeye devam
ediyor.

Satır satır:

- **`topup-webhook` / RabbitMQ `Degraded`.** Webhook'u kabul etmek yalnızca Postgres'e
  bağlı; inbox'ın varlık sebebi zaten "broker yokken de kaybetme". Broker'ı readiness'a
  bağlamak inbox'ı anlamsız kılardı.
- **`wallet-consumer` / RabbitMQ `Unhealthy`.** Bu uygulamanın tek işi kuyruktan okuyup
  ledger'a yazmak. Broker yoksa yapacak başka bir şey yok, `Degraded` demek yanıltıcı
  olurdu.
- **`wallet-api` / kontrol yok.** Tüketici ayrı deployable'a taşındıktan sonra bu
  uygulamanın broker ile hiç işi kalmadı (madde 28). Önce `Degraded` bir kontrol
  vardı; bağımlılık ortadan kalkınca kontrol de kalktı. Bir tasarım tercihiyken
  yapısal gerçek oldu.

**Testle doğrulandı.** `Readiness_BrokerBagimliligiIcERMEZ` sağlık çıktısında
`rabbitmq` kaydının BULUNMADIĞINI doğruluyor.

---

## 27. Top-up idempotency key'i sağlayıcıyı da taşır

**Karar.** `ledger_transactions.idempotency_key` top-up'ta `event_id` değil,
`provider:event_id`.

**Gerekçe.** İki tekillik alanı var ve kapsamları farklı:

- mesaj tarafı: `(provider, event_id)` — `event_id` yalnızca sağlayıcı içinde tekil,
- ledger tarafı: `(ledger_account_id, idempotency_key)` — sağlayıcıyı hiç tanımıyor.

Yalnız `event_id` yazılsaydı, iki sağlayıcı aynı id'yi aynı cüzdan için ürettiğinde
ikinci yükleme unique index'e takılır ve **müşterinin parası sessizce kaybolurdu**.
Sağlayıcılar birbirinden habersiz id ürettiği için bu uzak bir ihtimal değil; `evt_1`
gibi sayaç tabanlı id'lerde neredeyse kaçınılmaz.

**Nasıl bulundu.** Kod önce yalnızca `event_id` yazıyordu ve tek sağlayıcıyla yazılmış
her test geçiyordu. `FarkliSaglayicilar_AyniEventId_AyriAyriIslenir` bunu yakaladı.

---

## 28. Deployable ayrımı erişim seviyesine göre

**Karar.** wallet çekirdeği tek uygulama değil, ortak bir kütüphane (`WalletService.Core`)
üstünde iki host:

| deployable | ingress | Postgres | RabbitMQ |
| --- | --- | --- | --- |
| `wallet-api` | **public** (mobil/web) | `hiwallet_wallet` / `wallet_app` | — |
| `topup-webhook` | **IP kısıtlı** (sağlayıcı) | `hiwallet_topup` / `topup_app` | publish |
| `wallet-consumer` | **yok** | `hiwallet_wallet` / `wallet_app` | consume |

**Birincil gerekçe: farklı ağ maruziyeti aynı process'te olamaz.** Banka webhook'u
belirli IP bloklarına açılacak, cüzdan API'si herkese. IP kısıtı process seviyesinde
uygulanamaz, yalnızca deployable seviyesinde. Bu tek başına webhook'un ayrılmasını
gerektiriyor — o zaten ayrıydı.

**Tüketicinin ayrılma gerekçesi farklı.** Onun hiç ingress'i yok; mesajları kendisi
çekiyor. Ama wallet-api'nin İÇİNDE koştuğu sürece o uygulamanın maruziyetini ve blast
radius'unu miras alıyordu: public ingress'i olan bir uygulamanın içinde ledger'a yazan
bir iş parçacığı. Ayırınca ledger'a yazan kod dışarıdan erişilemeyen bir sürece taşındı.

**Yan kazançlar.**

- wallet-api'nin RabbitMQ bağımlılığı tamamen kalktı; public yüzeydeki bağımlılık
  sayısı azaldı (madde 26).
- Bağlantı havuzları ayrıldı. Kuyruk birikmesi artık HTTP'nin bağlantılarını yiyemiyor.
  Tüketicinin dizesinde `Application Name` ayrı, `pg_stat_activity`'de yük kaynağı
  görünüyor.
- Tüketici tıkandığında kendi sağlık ucu var; wallet-api'nin sağlıklı görünmesi durumu
  bitti.

**Ölçüt iki yöne de işliyor.** Farklı maruziyet aynı process'te birleşmiyor; AYNI
maruziyet de gereksiz yere bölünmüyor. `wallet-consumer` bugün iki kuyruk dinliyor —
top-up event'leri ve withdrawal saga'sının komutları. İkisi de ingress'siz, ikisi de
`hiwallet_wallet`'a aynı kütüphaneyle yazıyor; ayırmayı gerektiren hiçbir şey yok.
Ayrı süreç açmanın gerekçeleri (bağımsız ölçekleme, biri çökerken diğerinin ayakta
kalması) bu projede gerçek bir ihtiyaç değil ve gerekçesiz deployable taşınmıyor.

Adı önce `topup-consumer` idi. İkinci kuyruk eklenince isim gerçeği anlatmaz oldu:
bu uygulama "top-up tüketicisi" değil, **ledger'a asenkron giren her şeyin girdiği
yer**. Ayrı hosted service'ler, ayrı kanallar — biri tıkanınca diğeri akmaya devam
ediyor.

**Neden ortak kütüphane, ayrı kopya değil.** Madde 25: ayrı deployable olmak sorun
değil, ayrı **kod** olmak sorun.

**Kütüphane sınırı: `WalletService.Core`'a wallet dışından referans verilmez.**
topup-webhook onu görmemeli. Görürse ayrı veritabanı sınırı yapısal bir gerçek olmaktan
çıkıp nezaket kuralına döner. Bu, "db'ye ve rabbitmq'ya yazan her şeyi tek kütüphaneye
koy" alternatifinin elenme sebebi: o sınır altyapı şeklinde çizilmiş olurdu, veri
sahipliği şeklinde değil.

**Ne kazandırmıyor.** Tüketici verimi artmıyor — `x-single-active-consumer` yüzünden
aktif tüketici sayısı `PartitionCount` ile sınırlı, instance sayısıyla değil. Postgres
de izole olmuyor; ayrılan yalnızca .NET tarafındaki havuz.

**Maliyet.** wallet-api ve wallet-consumer aynı şemayı paylaştığı için birlikte deploy
edilmek zorundalar. Migration sahipliği değişmiyor (ayrı `migrator` job'ı, hiçbir
uygulama startup'ta migrate etmiyor) ama koordine edilecek şey ikiye çıktı.

---

## 29. Webhook `202` döner, `200` değil

**Karar.** `POST /v1/webhooks/topup/{provider}` başarıda `202 Accepted` dönüyor,
gövde `{"accepted": true, "duplicate": false}`.

**Gerekçe.** Yanıt döndüğünde para henüz cüzdanda değil: ledger'a yazan kod başka bir
deployable'da, arada broker var. `200 OK` "istediğin işi yaptım" demek ve bu doğru
değil. `202` tam olarak verilen sözü söylüyor — **kabul edildi ve kalıcı kaydedildi,
işlenmesi sonra.**

İşlevsel fark yok (sağlayıcıların çoğu 2xx'in hepsini başarı sayıyor); fark
sözleşmenin dürüstlüğünde. Yanıtın anlamını olduğundan güçlü göstermek, ileride
"200 aldım, neden bakiyem artmadı" tartışmasının kaynağı olur.

**Tekrar eden event de `202`.** Sağlayıcı için yeniden gönderim beklenen bir davranış,
hata değil; `4xx` dönmek onu sonsuz tekrara sokardı. Ayrım gövdedeki `duplicate`
alanında.

---

## 30. Relay tek instance: sıra broker'a varmadan bozulmasın

**Durum.** `overview.md` madde 8 "aynı cüzdanın mesajlarında sıra korunur" diyor.
Broker tarafında bu doğru: consistent hash exchange aynı cüzdanı hep aynı kuyruğa
düşürüyor ve `x-single-active-consumer` + `prefetch=1` o kuyruğu sırayla işletiyor.

**Ama sıra broker'a VARMADAN önce bozulabiliyordu.** Relay inbox'tan
`FOR UPDATE SKIP LOCKED` ile batch alıyor. İki instance ayrı batch'ler kilitliyor:
A 1-50'yi, B 51-100'ü aldıysa ve A yavaşsa, B önce publish ediyor. Aynı cüzdanın
iki event'i farklı batch'lere düşerse exchange'e ters sırada varıyorlar. Kuyruğun
içindeki sıra garantisi, kuyruğa yanlış sırada gelen mesajı düzeltmiyor.

**Karar.** Top-up relay'i `pg_try_advisory_lock` ile tek instance'a bağlanıyor
(madde 3'teki mekanizma, 5.1'de yazılan `JobLease`). Kilidi alamayan instance o turu
atlıyor ve bekliyor.

**Neden bu seçenek.** Üç seçenek vardı:

1. **Tek instance'a bağla.** Sıra gerçekten korunuyor. Verim tavanı tek relay'in
   hızı; ölçeklenme partition sayısıyla değil, o tek süreçle sınırlı.
2. `x-single-active-consumer`'ı kaldır, sıra iddiasını da kaldır. Verim instance
   sayısıyla ölçeklenir — top-up için yeterliydi (hepsi alacak kaydı, toplama
   işlemi) ama çekim akışı aynı cüzdana sırası önemli mesajlar akıtıyor ve o iddiayı
   geri istemek zor.
3. Cüzdan bazında sıra numarası taşı, tüketici sırasızları tamponlasın. Doğru
   çözüm ama bu projenin ağırlığının üstünde: tampon, zaman aşımı ve boşluk tespiti
   gerektiriyor.

Birincisi seçildi çünkü **iddia ile gerçeği hizalıyor.** İkincisi dokümanda verilen
sıra sözünü geri almak demekti; üçüncüsü kapsam dışı. Bedeli açık ve ölçülebilir:
relay yatay ölçeklenmiyor.

**Bedelin sınırı.** Kilit yalnızca top-up relay'inde. Withdrawal outbox relay'i
kilitlenmiyor ve gerek de yok: bir saga'nın aynı anda birden fazla bekleyen komutu
olamıyor — her geçiş en fazla bir komut üretiyor ve bir sonraki ancak cevabı gelince
yazılıyor. Orada sıra saga'nın kendisinden geliyor, batch'lerin hızından değil.

**`SKIP LOCKED` KALIYOR.** Kilitle birlikte gereksizleşmiş gibi duruyor ama ikinci
emniyet kemeri: kilit yalnızca "aynı anda tek relay" diyor, `SKIP LOCKED` ise kilit
bir şekilde alınamadığında (bağlantı koptu, kilit elle bırakıldı) iki relay'in aynı
SATIRI almasını engelliyor. Birincisi sıra için, ikincisi çift yayın için.

---

## 31. Saga'da "yok say" ile "çelişki" ayrılır

**Karar.** Saga geçişleri üç sonuç dönüyor: `Applied`, `Ignored`, `Conflict`.
`overview.md` madde 6 "tekrar gelen event mevcut durumla eşleşmezse yok sayılır"
diyor; kod bunun ilerisine geçiyor.

**Gerekçe.** "Eşleşmeyen event" tek bir şey değil, iki farklı şey:

| durum | event | ne demek |
| --- | --- | --- |
| `Debited` | `WithdrawalDebited` | broker ikinci kez teslim etti — **beklenen** |
| `BankTransferPending` | `WithdrawalDebited` | saga ilerlemiş, geciken event — **beklenen** |
| `Compensating` | `BankTransferSucceeded` | banka "olmadı" dedi, iade ediyoruz, şimdi "oldu" diyor |
| `Failed` | `BankTransferSucceeded` | iade edildi ama para bankadan çıkmış olabilir |
| `Completed` | `BankTransferFailed` | tamamlandı sayıldı, sonra hata geldi |

İlk ikisi en-az-bir-kez teslimin doğal sonucu; ack'lenip geçilir. Son üçü ise
**para kaybına işaret ediyor** — muhtemelen hem bankadan çıkmış hem müşteriye iade
edilmiş bir tutar var. İkisini aynı `Ignored` kefesine koymak, en pahalı hatayı en
sessiz hale getirirdi.

**`Conflict`'te ne oluyor.** Saga durumu DEĞİŞMİYOR — yarıda bırakılan bir telafi
daha kötü. Mesaj ack'leniyor (tekrar denemek aynı çelişkiyi üretir), alarm log'u
yazılıyor ve kayıt mutabakat raporuna düşüyor. Sistem düzeltmiyor, gösteriyor —
madde 11'deki fatura uyuşmazlığıyla aynı yaklaşım.

**Neden state machine'de, handler'da değil.** Karar tamamen mevcut durumun
fonksiyonu; DB'ye, mesaja ve zamana bakmıyor. Handler'da olsaydı her handler kendi
tablosunu taşır ve ilki sapan yerde sessizce yanlış davranırdı.

---

## 32. Outbox orchestrator'da; komut gönderimi saga geçişiyle aynı transaction'da

**Karar.** Orchestrator saga durumunu değiştirirken göndereceği komutu aynı DB
transaction'ında bir `withdrawal_outbox` tablosuna yazıyor. Broker'a taşımak ayrı bir
relay'in işi — top-up'taki inbox+relay ile aynı kalıp, ters yönde.

**Gerekçe.** Saga'nın her adımı iki şey yapıyor: durumu ilerlet, komut gönder. Bu
ikisi atomik değilse aradaki çökme iki bozuk sonuçtan birini bırakıyor:

| sıra | çökme noktası | sonuç |
| --- | --- | --- |
| önce publish, sonra commit | ikisinin arası | komut gitti, saga hâlâ `Debited` — banka parayı gönderir, saga sonsuza kadar bekler |
| önce commit, sonra publish | ikisinin arası | saga `BankTransferPending`, bankaya hiç komut gitmedi — **müşterinin parası clearing'de asılı kalır** |

İkincisi daha sinsi: hiçbir hata log'u yok, saga "bekliyor" görünüyor ve ancak stuck
saga taraması yakalıyor. Outbox ikisini de kapatıyor — durum ve niyet aynı commit'te.

**Nerede DEĞİL.** Outbox wallet-service'te ya da bank-service'te yok. Onlar komut
tüketip event yayınlıyor; event yayınlanamazsa mesaj ack'lenmiyor ve komut yeniden
teslim ediliyor. Yani orada güvenilirlik zaten broker'ın redelivery'sinden geliyor,
ikinci bir tablo gereksiz olurdu. Saga'da öyle değil: geçişi tetikleyen şey her zaman
bir mesaj olmayabilir (API isteği, zamanlanmış tarama) ve o durumda geri alınacak bir
teslim yok.

**Inbox ile ilişkisi.** İkisi aynı kalıbın iki yönü ve karıştırılmamalı: inbox
"dışarıdan gelen mesajı, göndereni onaylamadan önce kalıcı yaz" (top-up webhook'u),
outbox "kendi işini commit'lerken haber vermeyi de aynı commit'e al". Top-up
girişinde commit'lenen bir iş olmadığı için orada ikisi tek tabloya çöküyordu; burada
gerçekten iki ayrı şey var.

**En az bir kez teslim, yine.** Relay publish edip commit edemezse komut ikinci kez
gidiyor. Alıcı tarafta `CommandId` + `processed_messages` bunu yutuyor
(`overview.md` madde 6). Ters sıra (önce işaretle, sonra publish) KAYIP üretirdi;
kaybetmektense iki kez göndermek tercih ediliyor — madde 3'teki relay kararıyla aynı.

**Deduplikasyon nerede.** Komutu TÜKETEN tarafta. Orchestrator da event tüketiyor ama
orada ayrı bir tabloya gerek yok — saga'nın kendi durumu zaten "bu event uygulandı mı"
sorusunu cevaplıyor (madde 31). İkinci bir tablo aynı bilgiyi iki yerde tutmak olurdu.

Aynı gerekçe tablo adlarını da belirledi:

| taraf | tablo | neden |
| --- | --- | --- |
| wallet-service | `processed_messages` | komut işlenirken yazılacak başka bir kayıt yok |
| bank-service | `bank_transfers` | zaten "ne yaptık" kaydı tutuluyor, anahtarı da `CommandId` |
| orchestrator | — | saga durumu cevabı taşıyor |

bank-service'te ayrıca bir `processed_messages` AÇILMADI: "bu komut işlendi mi" ile
"bu transfer kaydı var mı" aynı soru ve iki tablo ilk ayrıştıklarında hangisinin doğru
olduğu belirsizleşirdi.

**Cevap da saklanıyor.** İki tüketen taraf da yalnızca "işledim" demiyor, verdiği
cevabı da yazıyor. Sıra şu: işi yap ve commit et → cevabı yayınla → mesajı ack'le.
Yayın başarısızsa ack yok ve komut yeniden teslim ediliyor; o teslimde iş İKİNCİ KEZ
yapılmamalı ama cevap yine gitmeli. Saklanmasaydı ikinci teslim sessizce ack'lenir ve
saga sonsuza kadar beklerdi.

Bu bir outbox DEĞİL: tarayan bir relay yok, yayın tüketici iş parçacığında ve yeniden
deneme broker'ın redelivery'sinden geliyor.

---

## 33. Neden ayrı orchestrator: bedeli ve seçilmeyen alternatif

**Karar.** Withdrawal saga'sı ayrı bir serviste, ayrı veritabanında kalıyor (madde 7).
Bu madde kararın kendisini değil **bedelini ve alternatifini** kayda geçiriyor; madde 7
ayrımı doğal bir sonuçmuş gibi anlatıyor, oysa gerçek bir tercih ve gerçek bir bedeli var.

**Seçilmeyen alternatif.** Saga satırı wallet'ın kendi veritabanında dursun, çekimi
ledger'ın sahibi olan servis yürütsün, bankaya çağrı outbox üzerinden gitsin:

```
wallet-api ──▶ wallet DB (withdrawal_sagas + ledger_entries + outbox, TEK COMMIT)
                   │
                   └─ relay ──▶ bank-service ──▶ cevap ──▶ aynı servis saga'yı ilerletir
```

Bu alternatif **daha basit ve bir hata sınıfını tamamen ortadan kaldırıyor.** Şu anki
tasarımda saga durumu ile ledger kaydı iki ayrı veritabanında: wallet parayı düşüp
`WithdrawalDebited` yayınlıyor, o mesaj kaybolursa wallet'ta para düşmüş ama
orchestrator'da saga hâlâ `Initiated` görünüyor. Müşterinin parası clearing'de asılı
kalıyor ve bu iki veritabanı karşılaştırılmadan anlaşılmıyor. **Takılmış saga taraması
(adım 5) tam olarak bu ayrım yüzünden var.** Alternatifte saga satırı ile ledger kaydı
aynı commit'te olurdu ve o tarama gereksizleşirdi.

Ayrıca şunlar da gereksizleşirdi: `DebitForWithdrawal`/`WithdrawalDebited` ve
`RefundWithdrawal`/`WithdrawalRefunded` gidiş-dönüşleri, wallet tarafındaki
`processed_messages`, orchestrator'ın ayrı veritabanı ve bir deployable.

**Yine de ayrı tutuluyor, üç sebeple.**

1. **Ledger'ın sahibi olan servis dışarıya çağrı yapmıyor.** Banka yavaşladığında ya da
   erişilemez olduğunda o basınç ledger yazan sürecin içine girmiyor.
2. **Akış büyürse yeri hazır.** Çekime KYC kontrolü, dolandırıcılık servisi ya da ikinci
   bir ödeme sağlayıcısı girdiğinde bunlar wallet'ın içine değil orchestrator'a ekleniyor.
3. **Bu bir referans uygulama.** Dağıtık saga'yı gerçekten dağıtık kurmak çıktının
   kendisi; tek veritabanına çökmüş bir saga deseni göstermiyor.

**Dürüst olmak gerekirse** ilk iki gerekçe bugün gerçek bir ihtiyaç değil: projede ne KYC
var ne ikinci sağlayıcı, banka da sahte. Bugünkü ağırlık üçüncü maddede. Üretim
sistemi tasarlanıyor ve bu üç koşulun hiçbiri yoksa **alternatif tercih edilmeli.**

**Bedelini kim ödüyor.** Ayrımın faturası tek kalemde toplanıyor: iki veritabanı
arasında ayrışma ihtimali. Karşılığı da tek: takılmış saga taraması. O tarama
opsiyonel bir iyileştirme DEĞİL, bu kararın zorunlu tamamlayıcısı — yazılmazsa
asılı kalmış çekimleri hiçbir şey yakalamaz.
