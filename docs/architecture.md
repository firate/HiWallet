# Mimari — kim ne yapıyor, veri nereye gidiyor

Bu dosya sistemin **şeklini** gösteriyor. Gerekçeler `decisions.md`'de, kurallar
`CLAUDE.md`'de, şema `ledger-schema.md`'de. Kodu okumaya nereden başlanacağı
`README.md`'de.

Diyagramlardaki exchange, kuyruk ve hesap adları koddan alındı; uydurulmuş ad yok.

---

## 1. Topoloji: altı uygulama, dört veritabanı, bir broker

```mermaid
flowchart LR
    client["Mobil / Web<br/>istemci"]

    subgraph public["public ingress"]
        api["<b>wallet-api</b><br/>hesap, cüzdan, transfer"]
        orch["<b>withdrawal-orchestrator</b><br/>çekim saga'sı"]
    end

    subgraph restricted["IP kısıtlı ingress"]
        hook["<b>topup-webhook</b><br/>imza doğrular, inbox'a yazar"]
        bhook["<b>bank-webhook</b><br/>imza doğrular, inbox'a yazar"]
    end

    subgraph noingress["ingress YOK"]
        consumer["<b>wallet-consumer</b><br/>ledger'a yazan tek tüketici"]
        adapter["<b>bank-adapter</b><br/>bankayı arar, sonucu yayınlar"]
    end

    subgraph outside["BİZİM DEĞİL — canlıda yok"]
        bank["<b>bank-fake</b><br/>bankanın API'si:<br/>havale girişi ve transfer"]
        stripe["<b>stripe-fake</b><br/>kart sağlayıcısı:<br/>yalnızca giriş"]
    end

    mq[["RabbitMQ"]]

    wdb[("hiwallet_wallet")]
    tdb[("hiwallet_topup")]
    odb[("hiwallet_withdrawal")]
    bdb[("hiwallet_bank")]

    client -->|HTTPS| api
    client -->|HTTPS| orch
    stripe -->|"webhook + HMAC"| hook
    bank -->|"webhook + HMAC"| hook
    bank -->|"callback + HMAC"| bhook

    api --> wdb
    consumer --> wdb
    hook --> tdb
    orch --> odb
    adapter --> bdb
    bhook --> bdb

    adapter -->|"HTTP"| bank

    hook -->|publish| mq
    orch <-->|"publish + consume"| mq
    mq -->|consume| consumer
    consumer -->|publish| mq
    mq -->|consume| adapter
    adapter -->|publish| mq
```


**Ayrım ölçütü erişim seviyesi, işlevsellik değil** (`decisions.md` madde 28). Farklı
ağ maruziyeti aynı process'te birleşmiyor; aynı maruziyet de gereksiz bölünmüyor —
`wallet-consumer` hem top-up event'lerini hem çekim komutlarını hem settlement'ı
dinliyor, üçü de ingress'siz ve aynı ledger'a yazıyor.

Dikkat edilecek dört şey:

**`wallet-api`'nin broker'a hiç bağlantısı yok.** Public yüzeyin tek bağımlılığı
Postgres. Tüketici ayrı bir uygulamaya taşındıktan sonra bu kasıtlı olarak korunuyor.

**`ledger_entries`'e yazan iki uygulama var** — `wallet-api` ve `wallet-consumer` —
ama **tek kod** üzerinden: `WalletService.Core`. İkinci bir kopya açılmıyor (madde 25).

**Orchestrator wallet veritabanına dokunmuyor.** Yalnızca komut gönderiyor. Bedeli iki
veritabanı arasında ayrışma ihtimali, karşılığı takılmış saga taraması (madde 33).

**Banka RabbitMQ dinlemiyor** ve bu diyagramdaki en önemli ayrıntı. `bank-adapter`
onu HTTP ile arıyor, banka da sonucu `bank-webhook`'a callback ile bildiriyor —
gerçek bir entegrasyonun şekli bu. Canlıda silinen kutular `bank-fake` ile
`stripe-fake`; yerlerine kurumların kendi endpoint'leri geçiyor ve adaptörün kodunda
tek satır değişmiyor (madde 35).

`bank-adapter` ile `bank-webhook` ayrı kutular çünkü **maruziyetleri farklı**:
birinin IP kısıtlı ingress'i var, öbürünün hiç ingress'i yok. Aralarındaki tek bağ
`hiwallet_bank`; doğrudan çağrı yok.

### Neden İKİ public yüzey var

Diyagrama bakan herkesin sorduğu soru bu, çünkü ilk bakışta kuralı deliyor gibi
duruyor.

Müşteriye dönük endpoint'ler iki uygulamaya dağılmış:

| endpoint | uygulama |
| --- | --- |
| `/v1/accounts`, `/v1/wallets`, `/v1/transfers` | `wallet-api` |
| `/v1/withdrawals` | `withdrawal-orchestrator` |

Madde 28'in ölçütü erişim seviyesi ve **aynı maruziyet bölünmez** diyor. Bu ikisinin
maruziyeti aynı: ikisi de public, ikisi de müşteriye dönük, ikisi de aynı istemciden
çağrılıyor. Kurala bakınca bölünmemeleri gerekirdi.

**Bölünmelerinin sebebi madde 28 değil, madde 7.** Orchestrator'ın kendi veritabanı ve
kendi sınırı var; saga durumu ile ledger ayrı tutuluyor. Yani burada iki kural
çakışıyor ve kazanan servis sınırı oluyor.

Bedeli somut: istemci iki base URL biliyor, iki yüzey ayrı ayrı güvenceye alınıyor,
rate-limit'leniyor ve izleniyor.

İki yoldan biriyle kapanır:

**Endpoint katman** (BFF / API gateway) geldiğinde istemci tek adres görür; arkada iki
backend'in olması onu ilgilendirmez. Bugün o katman yok.

**Ya da saga wallet'ın içine taşınır** — madde 33'ün "elenen alternatif"i. O zaman
`/v1/withdrawals` de `wallet-api`'ye düşer ve ikinci yüzey diye bir şey kalmaz. Madde
33 bu alternatifi "daha basit" diye niteliyor ve canlıya çıkacak bir sistem tasarlanıyorsa
**tercih edilmesi gerektiğini** açıkça söylüyor. Burada seçilmemesinin sebebi tek:
bu bir referans uygulaması ve dağıtık saga'yı gerçekten dağıtık kurmak çıktının
kendisi.

---

## 2. Top-up: para dışarıdan giriyor

```mermaid
sequenceDiagram
    participant P as Sağlayıcı
    participant H as topup-webhook
    participant R as relay
    participant MQ as hiwallet.topups
    participant C as wallet-consumer

    P->>H: POST /v1/webhooks/topup/stripe-fake
    Note right of H: HAM gövde üzerinde HMAC,<br/>parse ETMEDEN önce.<br/>INSERT topup_inbox —<br/>provider + event_id UNIQUE
    H-->>P: 202 Accepted
    Note over P,H: Söz "işledim" değil, "kalıcı kaydettim"

    loop her tur
        R->>R: SELECT FOR UPDATE SKIP LOCKED
        R->>MQ: publish, routing key = cüzdan id
        R->>R: işlendi olarak işaretle
    end
    Note right of R: ÖNCE publish, SONRA işaretle.<br/>Ters sıra kayıp üretir.<br/>Relay tek instance — advisory lock

    MQ->>C: p0 .. p3, x-consistent-hash
    Note right of C: prefetch=1, x-single-active-consumer.<br/>processed_events + ledger<br/>AYNI transaction'da
```

`relay` ayrı bir uygulama değil, `topup-webhook`'un içinde koşan bir
`BackgroundService` — ama ayrı çizildi, çünkü **zincirin koptuğu yer orası**: HTTP
request'i `202` ile bitiyor, yayın sonra ve başka bir turda oluyor.

Routing key cüzdan kimliği: aynı cüzdanın mesajları hep aynı partition'a düşüyor ve
sıra orada korunuyor. Bu yüzden relay **tek instance** koşuyor — iki relay ayrı
batch'leri farklı hızda yayınlarsa mesajlar exchange'e ters sırada varır ve kuyruk içi
sıra garantisi bunu düzeltmez (madde 30).

İki kademe idempotency var: inbox'ta `(provider, event_id)` UNIQUE, tüketicide
`processed_events`. İkincisi ledger yazımıyla aynı transaction'da.

---

## 3. Çekim: para dışarı çıkıyor

```mermaid
sequenceDiagram
    participant U as İstemci
    participant O as withdrawal-orchestrator
    participant W as wallet-consumer
    participant A as bank-adapter
    participant B as bank-fake
    participant H as bank-webhook

    U->>O: POST /v1/withdrawals<br/>(Idempotency-Key ZORUNLU)
    Note over O: saga + outbox<br/>AYNI transaction'da
    O-->>U: 202 — hiçbir para hareket etmedi

    O->>W: DebitForWithdrawal
    Note over W: cüzdan −102<br/>clearing +100<br/>revenue +2
    W->>O: WithdrawalDebited

    O->>A: StartBankTransfer
    A->>B: POST /v1/transfers<br/>(Idempotency-Key = CommandId)
    B-->>A: 202 pending + bankReference
    Note over A: bank_transfers = pending<br/>CEVAP YAYINLANMIYOR
    Note over O: saga GERÇEKTEN<br/>bank_transfer_pending'de bekliyor

    B->>H: callback + HMAC
    H-->>B: 202 (inbox'a yazıldı)
    Note over H,A: tek bağ veritabanı,<br/>relay adaptörde

    alt banka kabul etti
        A->>O: BankTransferSucceeded
        O->>W: SettleWithdrawal
        Note over W: clearing −100<br/>nostro +100
        W->>O: WithdrawalSettled
        Note over O: completed
    else banka reddetti
        A->>O: BankTransferFailed
        O->>W: RefundWithdrawal
        Note over W: ters kayıt ÜÇ bacaklı:<br/>cüzdan, clearing, revenue
        W->>O: WithdrawalRefunded
        Note over O: failed
    end
```

Durumlar: `initiated → debited → bank_transfer_pending → settling → completed`.
Telafi yolu: `debited → compensating → failed`. `rejected` terminal.

**`bank_transfer_pending` gerçek bir pencere.** Banka "aldım" diyor, sonucu sonra
bildiriyor; o arada saga bekliyor. Önceki tasarımda banka aynı teslimde cevap
verdiği için bu durumdan hiç geçilmiyordu ve takılmış saga taraması (madde 33)
yakalayacak bir şey bulamıyordu.

**Callback kaybolursa kayıp olmuyor.** `bank-adapter`'ın mutabakat taraması
`StaleAfter` süresinden uzundur cevapsız kalan transferleri bankaya soruyor.
Callback asıl yol, tarama kontrol — ve taramanın bulduğu satır sayısı doğrudan
callback hattının sağlık göstergesi (madde 35).

**Ters kayıt politikadan yeniden üretilmiyor**, orijinal işlemin bacakları okunup
negatifleniyor. `revenue` bacağı atlanırsa kayıt yine dengeli çıkar ve trigger susar —
ama müşteri gerçekleşmemiş bir işlemin komisyonunu ödemiş kalır.

**Response'ların hepsi saklanıyor** (`processed_messages`, `bank_transfers`). Tekrar
teslimde iş ikinci kez yapılmıyor ama aynı cevap yeniden yayınlanıyor; cevapsız
kalan saga müşteriyi sonsuza kadar "işleniyor"da bırakırdı.

---

## 4. Para nerede duruyor

Diyagramların en önemlisi bu: **her akışta toplam sıfır.**

Beş hesap rolü var: ikisi "iddia", üçü "gerçekleşmiş".

```mermaid
flowchart LR
    subgraph claim["iddia — henüz banka hareketi yok"]
        wallet["<b>user_wallet</b><br/>müşteriye borcumuz"]
        clearing["<b>clearing</b><br/>sağlayıcıyla<br/>açık hesap"]
    end

    subgraph real["gerçekleşmiş"]
        nostro["<b>nostro</b><br/>bankadaki paramız"]
        revenue["<b>revenue</b><br/>gelirimiz"]
        expense["<b>provider_expense</b><br/>giderimiz"]
    end

    claim -.->|settlement<br/>iddiayı gerçeğe çevirir| real
```

Her işlem tipinin yazdığı bacaklar — **toplamı her satırda sıfır**:

| işlem | bacaklar |
| --- | --- |
| `topup` | `wallet +100`, `clearing −100` |
| `p2p` | `gönderen −100`, `alan +100` |
| `payment` | `gönderen −102`, `alan +100`, `revenue +2` |
| `withdrawal` | `cüzdan −102`, `clearing +100`, `revenue +2` |
| `refund` | orijinalin bacakları negatiflenerek — üçü de |
| `settlement` (top-up) | `clearing +gross`, `nostro −net`; `Net` modelde ayrıca `provider_expense −fee` |
| `settlement` (çekim) | `clearing −owed`, `nostro +owed` |
| `provider_invoice` | `provider_expense −tutar`, `nostro +tutar` |

İşaret konvansiyonu: credit `+`, debit `−`, hiçbir yerde tersine çevrilmiyor.
`nostro` bir **varlık** hesabı ve bu ledger'da varlıklar negatif duruyor — `−97.1`
"97.1 açık" değil, "bankada 97.1 var" demek.

`revenue` ile `provider_expense` **netleştirilmiyor**: biri müşteriden aldığımız,
diğeri sağlayıcıya ödediğimiz. Ayrı hesaplar.

`nostro` para birimi başına **tek**: ödeme sağlayıcısının nostro'su yok, çünkü nostro
bir banka hesabı. Stripe parayı bizim banka hesabımıza yatırıyor.

---

## 5. Zamanlanmış işler

| iş | nerede | varsayılan | ne yapıyor |
| --- | --- | --- | --- |
| takılmış saga taraması | orchestrator | 5 dk | iki veritabanı arasında asılı kalan çekimi yakalıyor |
| banka mutabakatı | bank-adapter | 4 saat | callback'i kaçırılmış transferi bankaya sorup kapatıyor |
| mutabakat | wallet-consumer | 6 saat | projeksiyon sapması, gelmeyen settlement, geciken fatura |
| işletme günlük özeti | wallet-consumer | 1 saat | hacim, işlem sayısı, kesilen komisyon |

Dördü de `pg_try_advisory_lock` ile tek instance'a kilitleniyor ve **ilk turu beklemeden
koşmuyor** — dağıtımda ayağa kalkan her instance aynı anda tarama başlatmasın diye.

Takılmış saga taraması opsiyonel bir iyileştirme **değil**: ayrı orchestrator
veritabanı kararının zorunlu tamamlayıcısı (madde 33).

Banka mutabakatı da öyle (madde 35) ve **kapatılamıyor** — yalnızca aralığı
ayarlanıyor. İkisi aynı desen ama aynı şey değil: biri iki veritabanımız arasındaki
ayrışmaya bakıyor, öbürü bizimle banka arasındakine.
