# API örnekleri

Her endpoint için request ve **beklenen** response. Elle denemek ve bir şeyin bozulduğunu
anlamak için; sözleşmenin kaynağı kod, bu dosya ona uyar.

Response'lar compose'da koşan sistemden alındı (`localhost:8091-8096`). Kimlikler her
koşuda değişir.

**`localhost`, compose'un koştuğu makine demek.** Stack'i başka bir makinede
kaldırdıysan bütün adreslerde onun adını kullan (`homelab:8091` gibi); `docker
compose ps` ve `docker compose exec` ile başlayan komutlar da o makinede koşar,
uzaktan çalışmaz.

```bash
set -a; . ./.env; set +a          # webhook secret'ları kabuğa gelsin
```

**Durum kodları neden bu şekilde:** `201` yaratıldı, `202` kalıcı olarak alındı ama
henüz işlenmedi, `400` girdi bozuk, `404` kayıt yok, `409` eşzamanlılık çakışması,
`422` request geçerli ama iş kuralı reddetti. `409` ile `422` karıştırılmaz — birincisi
"tekrar dene", ikincisi "tekrar denemenin faydası yok".

---

## wallet-api — `:8091`

### Hesap aç

```bash
curl -i -X POST localhost:8091/v1/accounts \
  -H 'Content-Type: application/json' \
  -d '{"type":"Person"}'
```

```
HTTP/1.1 201 Created
Location: http://localhost:8091/v1/accounts/d5e9df14-cd62-4ad5-b628-08df56e06daa
```
```json
{
  "accountId": "d5e9df14-cd62-4ad5-b628-08df56e06daa",
  "type": "Person",
  "createdAt": "2026-09-06T12:41:03.117421+00:00"
}
```

`type`: `Person` | `Business`. Sayı değil isim gönderilir — sayı olsaydı enum'a yeni
değer eklemek mevcut istemcilerin anlamını kaydırırdı.

Hesap para tutmaz. Bir hesabın aynı para biriminde birden fazla cüzdanı olabilir
(`decisions.md` madde 20); günlük limit bu yüzden cüzdan değil **hesap** bazında
uygulanır.

<details><summary>Geçersiz tip → <code>400</code></summary>

```bash
curl -i -X POST localhost:8091/v1/accounts \
  -H 'Content-Type: application/json' -d '{"type":"Robot"}'
```
```
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json
```
</details>

### Cüzdan aç

```bash
curl -i -X POST localhost:8091/v1/accounts/$ACCOUNT/wallets \
  -H 'Content-Type: application/json' \
  -d '{"name":"Birikim","currency":"TRY"}'
```

```
HTTP/1.1 201 Created
Location: http://localhost:8091/v1/wallets/23948ca4-7c69-4b0a-b8a1-eccdc87876a4
```
```json
{
  "walletId": "23948ca4-7c69-4b0a-b8a1-eccdc87876a4",
  "accountId": "d5e9df14-cd62-4ad5-b628-08df56e06daa",
  "name": "Birikim",
  "currency": "TRY",
  "balance": 0.0000
}
```

`name` zorunlu: aynı hesabın aynı para birimindeki cüzdanları başka türlü ayırt
edilemiyor. Bakiye her zaman `0` başlar — para yalnızca ledger üzerinden girer.

<details><summary>Olmayan hesap → <code>404</code></summary>

```json
{
  "type": "https://hiwallet.dev/problems/account-not-found",
  "title": "Hesap bulunamadı",
  "status": 404,
  "detail": "Hesap bulunamadı: 00000000-...",
  "traceId": "..."
}
```
</details>

<details><summary>Sistem hesabı olmayan para birimi → <code>422</code></summary>

```bash
curl -i -X POST localhost:8091/v1/accounts/$ACCOUNT/wallets \
  -H 'Content-Type: application/json' -d '{"name":"Dolar","currency":"USD"}'
```
```json
{
  "type": "https://hiwallet.dev/problems/business-rule",
  "title": "İşlem iş kuralı gereği reddedildi",
  "status": 422,
  "detail": "USD için sistem hesapları açılmamış; ... Desteklenen: TRY.",
  "rule": "unsupported_currency"
}
```

`400` değil `422`: `USD` geçerli bir ISO 4217 kodu, request kusursuz. Reddin sebebi
o para biriminde `clearing` ve `revenue` hesaplarının seed edilmemiş olması — bu
ledger'ın bilgisi, sınırdaki doğrulayıcı bilemez.
</details>

<details><summary>Boş ad → <code>400</code></summary>

```json
{
  "status": 400,
  "errors": { "Name": ["Cüzdan adı zorunlu."] }
}
```
</details>

### Cüzdanı sorgula

```bash
curl -s localhost:8091/v1/wallets/$WALLET
```
```json
{
  "walletId": "23948ca4-7c69-4b0a-b8a1-eccdc87876a4",
  "accountId": "d5e9df14-cd62-4ad5-b628-08df56e06daa",
  "name": "Birikim",
  "currency": "TRY",
  "balance": 500.0000
}
```

Bakiye `ledger_balances` projeksiyonundan okunur, `ledger_entries` toplanarak değil.

**Sistem hesabı bu uçtan görünmez.** `revenue` ya da `clearing` kimliğiyle sorarsan
`404` dönerler — aynı tabloda duruyorlar ama iç muhasebe, public API'nin
cevaplayacağı soru değil.

### Hesabı ve cüzdanlarını sorgula

```bash
curl -s localhost:8091/v1/accounts/$ACCOUNT
```
```json
{
  "accountId": "d5e9df14-cd62-4ad5-b628-08df56e06daa",
  "type": "Person",
  "createdAt": "2026-09-06T12:41:03.117421+00:00",
  "wallets": [
    { "walletId": "23948ca4-...", "name": "Birikim", "currency": "TRY", "balance": 398.0000 },
    { "walletId": "7c1e0b22-...", "name": "Harcama", "currency": "TRY", "balance": 0.0000 }
  ]
}
```

### Transfer

```bash
curl -i -X POST localhost:8091/v1/transfers \
  -H 'Idempotency-Key: transfer-1' -H 'Content-Type: application/json' \
  -d "{\"fromWalletId\":\"$FROM\",\"toWalletId\":\"$TO\",\"amount\":200,\"currency\":\"TRY\",\"type\":\"Payment\"}"
```
```
HTTP/1.1 201 Created
```
```json
{ "transactionId": "...", "replayed": false }
```

`type`: `P2P` | `Payment` | `P2B` | `B2P` | `B2B`. Komisyon istenen tutara **ek**
olarak gönderenden düşülür: `Payment` %2 ise gönderen `-204`, alan `+200`,
`revenue` `+4`. Ledger'a üç satır düşer, toplamı sıfır.

**`Idempotency-Key` ZORUNLU**; başlık yoksa `400` ve ledger'a hiçbir şey yazılmaz
(`decisions.md` madde 4). Anahtarsız bir tekrar hiçbir constraint'e takılmaz ve çift
harcama sessizce ledger'a düşerdi; append-only olduğu için de geri alınamaz, yalnızca
ters kayıtla düzeltilir.

Aynı anahtarla ikinci request yeni transfer yapmaz:

```json
{ "transactionId": "aynı-kimlik", "replayed": true }
```

<details><summary>Yetersiz bakiye → <code>422</code></summary>

```json
{ "status": 422, "rule": "insufficient_funds", "traceId": "..." }
```

Limit aşımında `rule` limitin adını taşır (`per_transaction`, `daily`).
Eşzamanlılık çakışmasında ise `409` döner ve `rule` yoktur — o bir iş kuralı reddi
değil, "tekrar dene" demek.
</details>

---

## topup-webhook — `:8092`

Dışarıdan para girişi. Sağlayıcı rolünü sen oynuyorsun: gövdeyi imzalayıp
gönderiyorsun.

```bash
BODY="{\"eventId\":\"evt_1\",\"walletId\":\"$WALLET\",\"amount\":500.00,\"currency\":\"TRY\",\"reference\":\"pi_1\",\"occurredAt\":\"2026-09-06T10:00:00+00:00\"}"
SIG=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$STRIPE_FAKE_WEBHOOK_SECRET" -hex | awk '{print $2}')

curl -i -X POST localhost:8092/v1/webhooks/topup/stripe-fake \
  -H 'Content-Type: application/json' \
  -H "X-Hive-Signature: sha256=$SIG" \
  --data "$BODY"
```

```
HTTP/1.1 202 Accepted
```
```json
{ "accepted": true, "duplicate": false }
```

**`200` değil `202`, bilerek.** Verilen söz "işledim" değil "kalıcı kaydettim".
Response döndüğünde para henüz cüzdanda yok; hat webhook → inbox → relay → RabbitMQ →
wallet-consumer → ledger. Birkaç saniye sonra bakiyeye bak.

İmza **ham gövde baytları** üzerinde HMAC-SHA256. Gövdeyi yeniden serialize edersen
(boşluk, alan sırası) imza tutmaz.

Sağlayıcılar: `stripe-fake`, `bank-fake` — her birinin kendi secret'ı var.

<details><summary>Tekrar eden event → yine <code>202</code></summary>

Aynı `eventId` ile ikinci request:

```json
{ "accepted": true, "duplicate": true }
```

Sağlayıcı için yeniden gönderim başarılı bir sonuçtur; hata dönmek onu sonsuza
kadar tekrar ettirirdi. Ayrım gövdedeki `duplicate` alanında.
</details>

<details><summary>Geçersiz imza / eksik başlık / tanınmayan sağlayıcı → <code>401</code></summary>

Üçü de `401`. Tanınmayan sağlayıcıya `404` DÖNÜLMEZ — hangi sağlayıcıların tanımlı
olduğunu dışarıya söylemek istemiyoruz.
</details>

---

## withdrawal-orchestrator — `:8093`

### Çekim başlat

```bash
curl -i -X POST localhost:8093/v1/withdrawals \
  -H 'Idempotency-Key: cekim-1' -H 'Content-Type: application/json' \
  -d "{\"accountId\":\"$ACCOUNT\",\"walletId\":\"$WALLET\",\"amount\":100,\"currency\":\"TRY\",\"destinationIban\":\"TR330006100519786457841326\"}"
```

```
HTTP/1.1 202 Accepted
Location: http://localhost:8093/v1/withdrawals/cf13827f-470c-43af-a3b7-e3606e48c31d
```
```json
{
  "withdrawalId": "cf13827f-470c-43af-a3b7-e3606e48c31d",
  "state": "initiated",
  "replayed": false
}
```

**`Idempotency-Key` ZORUNLU** — transfer'de olduğu gibi. Burada bahis daha da yüksek:
çekim çok adımlı ve dışarıya para çıkarıyor, anahtarsız bir tekrar ikinci bir banka
transferi başlatırdı. Başlık yoksa `400`.

`202` dönüldüğünde **hiçbir para hareket etmedi**. IBAN boşluklu yazılabilir,
normalize edilir; mod-97 geçmezse `400`.

### Çekimi sorgula

```bash
curl -s localhost:8093/v1/withdrawals/$WD
```
```json
{
  "withdrawalId": "cf13827f-470c-43af-a3b7-e3606e48c31d",
  "state": "completed",
  "amount": 100.0000,
  "currency": "TRY",
  "destinationIban": "TR33******************1326",
  "totalDebited": 102.0000,
  "failureReason": null,
  "createdAt": "2026-09-06T12:54:21.993862+00:00",
  "updatedAt": "2026-09-06T12:54:23.2473+00:00"
}
```

`state` sırası: `initiated` → `debited` → `bank_transfer_pending` → `settling` →
`completed`. `settling`, para bankadan çıktıktan sonra iç muhasebenin kapanmasını
bekliyor (clearing boşalıp `nostro`'ya yazılıyor) ve saniyeler sürüyor.
Telafi yolunda: `debited` → `compensating` → `failed`. Reddedilmişse `rejected`.

`totalDebited` cüzdandan gerçekte çıkan toplam (tutar + komisyon). Wallet düşmeyi
yapana kadar `null` — `0` yazılmıyor, "komisyonsuz çekildi" ile karışırdı.

IBAN **maskeli** döner: müşteri zaten kendi girdi, tam hali response'ta dolaşınca log'a,
hata izlemeye ve tarayıcı geçmişine de düşer.

<details><summary>Aynı anahtarla tekrar → <code>202</code>, <code>replayed: true</code></summary>

```json
{ "withdrawalId": "aynı-kimlik", "state": "completed", "replayed": true }
```
Yeni çekim AÇILMADI.
</details>

<details><summary>Yetersiz bakiye / limit aşımı → saga <code>rejected</code></summary>

`POST` yine `202` döner — request geçerliydi ve kalıcı olarak alındı. Ret sonradan
ortaya çıkıyor:

```json
{
  "state": "rejected",
  "failureReason": "Cüzdan 2394... 50,00 TRY tutuyor, 102,00 TRY çekilemez.",
  "totalDebited": null
}
```

`failureReason` domain'in mesajı, makine tarafından ayrıştırılacak bir kod değil.
Kod isteyen bir istemci çıkarsa event'e ayrı bir alan eklenir.

Bu durum dead-letter'a GİTMEZ: cevapsız kalan saga müşteriyi sonsuza kadar
"işleniyor"da bırakırdı.
</details>

---

## bank-fake (BİZİM DEĞİL) — `:8094`

Bankanın API'sinin yerinde duran servis; canlıda yok (`decisions.md` madde 35).
Senaryo endpoint'i gerçek bir bankada bulunmaz — varlık sebebi "banka reddetti" durumunun
denenebilmesi.

**Transfer sonucu artık senkron dönmüyor.** `POST /v1/transfers` `202 pending`
veriyor, kesin sonuç callback ile ya da `GET /v1/transfers/{ref}` ile sonra
öğreniliyor. Bu yüzden çekim saga'sı gerçekten `bank_transfer_pending`'de bekliyor.

### Senaryo kur

```bash
curl -i -X POST localhost:8094/v1/scenarios \
  -H 'Content-Type: application/json' \
  -d "{\"clientReference\":\"$WD\",\"outcome\":\"Failure\"}"
```
```
HTTP/1.1 204 No Content
```

`outcome`: `Success` | `Failure` | `TransientFailure` | `DelayedSuccess`.
`TransientFailure` için `transientFailures` (1-10, varsayılan 1) kaç kez geçici hata
üretileceğini, `DelayedSuccess` için `delayMilliseconds` (≤30000) gecikmeyi belirler.

`TransientFailure` ile `Failure` arasındaki fark kritik: birincisinde banka `503`
dönüyor ve **transfer hiç açılmıyor** (adaptör yeniden deniyor, saga bekliyor),
ikincisinde transfer açılıyor ama sonucu başarısız (saga telafiye giriyor).

Senaryo **çekim başına** kuruluyor ve anahtarı `clientReference` — bizim saga
kimliğimiz. Yani çekimi başlattıktan sonra kurman gerekiyor ve bu bir **yarış**:
zincir seni beklemiyor, banka senaryoyu transfer request'i geldiği anda okuyor.
Çekim request'inin hemen ardından aynı betikte kurarsan genelde yetişirsin; elle
kopyalayıp yapıştırırken geç kalırsın. Garantili yol varsayılanı değiştirmek:

```bash
BANK_DEFAULT_OUTCOME=Failure docker compose up -d --force-recreate --no-deps bank-fake
docker compose exec bank-fake printenv BankFake__DefaultOutcome
```

Geri almak için aynı komutu değişkensiz çalıştır.

**Senaryolar ve transferler bellekte** — sahte bankanın veritabanı yok. Yukarıdaki
gibi container'ı yeniden yaratmak hepsini siler. O anda `bank_transfer_pending`'de
bekleyen bir çekim varsa kapanmaz: mutabakat taraması sorduğunda banka onu artık
tanımıyor (`404`). Önce bekleyen çekimlerin bitmesini bekle.

### Senaryonun durumu

```bash
curl -s localhost:8094/v1/scenarios/$WD
```
```json
{
  "clientReference": "...",
  "outcome": "TransientFailure",
  "remainingTransientFailures": 0,
  "attempts": 2
}
```

`attempts` retry'ın gerçekten çalıştığının kanıtı. Kurulmamış çekim için `404`.

### Transfer endpoint'leri — adaptörün konuştuğu sözleşme

Bunları elle çağırman gerekmiyor; `bank-adapter` çağırıyor. Burada duruyorlar çünkü
**gerçek entegrasyonda bankanın dokümanından yazılacak kısım** tam olarak bu ikisi.

```bash
curl -i -X POST localhost:8094/v1/transfers \
  -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuidgen)" \
  -d '{"clientReference":"<sagaId>","amount":100,"currency":"TRY","destinationIban":"TR330006100519786457841326"}'
```
```
HTTP/1.1 202 Accepted
```
```json
{ "bankReference": "BNK4F2A9C1E8B7D6A3", "status": "pending", "replayed": false }
```

**`status` her zaman `pending`.** Banka "aldım" diyor, "gönderdim" demiyor. Sonuç
callback ile ya da durum sorgusuyla sonra geliyor (`decisions.md` madde 35).

```bash
curl -s localhost:8094/v1/transfers/BNK4F2A9C1E8B7D6A3
```
```json
{
  "bankReference": "BNK4F2A9C1E8B7D6A3",
  "clientReference": "...",
  "status": "succeeded",
  "amount": 100.0000,
  "fee": 1.5000,
  "currency": "TRY",
  "failureReason": null,
  "acceptedAt": "..."
}
```

**Mutabakat taramasının okuduğu endpoint bu.** Bankanın böyle bir endpoint'i olmasaydı, callback'i
kaçırılan transferin sonucunu hiçbir şey öğrenemezdi.

`Idempotency-Key` başlıksız request `400`. Aynı anahtarla ikinci request yeni transfer
AÇMAZ: aynı `bankReference` ve `"replayed": true` döner.

`TransientFailure` senaryosunda endpoint `503` veriyor ve **transfer hiç açılmıyor** —
kalıcı hatadan farkı bu. Adaptör bunu yeniden deniyor, saga'ya hiçbir şey
bildirilmiyor.

---

## stripe-fake (BİZİM DEĞİL) — `:8096`

Kart sağlayıcısının yerinde duran servis; canlıda yok. **Tek endpoint'i var** — Stripe'tan
para çıkmadığı için ne transfer endpoint'i var ne callback alıcısı.

Gerçek Stripe'ta bu endpoint YOKTUR: webhook müşteri ödeme yaptığında gelir, sen
istediğinde değil.

### Para girişi tetikle

```bash
curl -i -X POST localhost:8096/v1/topups \
  -H 'Content-Type: application/json' \
  -d "{\"walletId\":\"$WALLET\",\"amount\":100,\"currency\":\"TRY\",\"mode\":\"Normal\"}"
```
```
HTTP/1.1 202 Accepted
```
```json
{ "mode": "Normal", "eventCount": 1 }
```

`202` çünkü gönderim ARKA PLANDA: dönüldüğünde webhook henüz gitmedi. `eventCount`
kaç webhook gideceğini söylüyor.

| `mode` | ne yapar | beklenen |
| --- | --- | --- |
| `Normal` | tek event | bakiye bir kez artar |
| `Duplicate` | **aynı** event iki kez (`eventId` de aynı) | bakiye **bir kez** artar |
| `Delayed` | tek event, `delayMilliseconds` sonra | eventual davranış görünür olur |
| `OutOfOrder` | aynı cüzdana `count` event, en yenisi önce | hepsi iner, bakiye toplama eşit |

`Duplicate`'in `eventId`'si bilerek aynı: farklı olsaydı bu iki ayrı para girişi
olurdu, tekrar değil.

`OutOfOrder` "sıra korunuyor" demiyor — top-up'ta toplama değişmeli. Dediği şey ters
sırada gelen bir dizinin tamamının kabul edildiği; değeri consistent-hash routing'in
hepsini aynı partition'a düşürmesinde.

Aynı endpoint `bank-fake`'te de var (`:8094`) ve `clearing/bank-fake`'e yazıyor — aynı
banka hem gelen havaleyi bildiriyor hem giden transferi kabul ediyor.

---

## bank-webhook — `:8095`

Bankanın transfer sonucunu bildirdiği endpoint. **Bizim kodumuz**, canlıda da koşuyor;
`bank-adapter`'dan ayrı bir deployable çünkü ingress'i var (`decisions.md` madde 28).

Elle çağırman gerekmiyor — `bank-fake` çağırıyor. İmza `topup-webhook`'unkiyle aynı
algoritma ama **ayrı bir sözleşme**: başlık adı `X-Bank-Signature` ve secret
`BANK_CALLBACK_SECRET`. Orada şemayı biz dayatıyoruz, burada bankanınkini uyguluyoruz.

```bash
BODY='{"eventId":"evt-BNK4F2A9C1E8B7D6A3","bankReference":"BNK4F2A9C1E8B7D6A3","clientReference":"...","status":"succeeded","fee":1.50,"currency":"TRY","failureReason":null,"occurredAt":"2026-03-01T10:00:00+00:00"}'
SIG=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$BANK_CALLBACK_SECRET" -hex | awk '{print $2}')
curl -i -X POST localhost:8095/v1/webhooks/bank/bank-fake \
  -H 'Content-Type: application/json' -H "X-Bank-Signature: sha256=$SIG" --data "$BODY"
```
```
HTTP/1.1 202 Accepted
```
```json
{ "accepted": true, "duplicate": false }
```

`202`, `200` değil: verilen söz "işledim" değil "kalıcı kaydettim". Bu servis
**işlemiyor** — inbox'a yazıp bırakıyor, transferi kapatmak `bank-adapter`'daki
relay'in işi.

`eventId` tekrar denemelerde aynı kalmak zorunda; ikinci kez gelirse yine `202` ama
`"duplicate": true` ve satır ikinci kez yazılmıyor.

İmza tutmazsa `401` ve inbox'a **hiçbir şey** yazılmaz. Tanınmayan kurum da `401`,
`404` değil — hangi bankalarla çalıştığımız dışarıya sızmamalı. `eventId` yoksa
`400`: kimliksiz bir bildirim deduplike edilemez.

---

## Telafi yolu — asıl görülmesi gereken

Banka kalıcı olarak reddettiğinde para üç bacaklı ters kayıtla geri döner:

```bash
BEFORE=$(curl -s localhost:8091/v1/wallets/$WALLET | jq -r .balance)

WD=$(curl -s -X POST localhost:8093/v1/withdrawals \
  -H 'Idempotency-Key: cekim-red' -H 'Content-Type: application/json' \
  -d "{\"accountId\":\"$ACCOUNT\",\"walletId\":\"$WALLET\",\"amount\":100,\"currency\":\"TRY\",\"destinationIban\":\"TR330006100519786457841326\"}" | jq -r .withdrawalId)

# Hemen ardından: zincir bankaya varmadan senaryo kurulmuş olmalı. Yetişmezse
# çekim başarılı biter — o durumda BANK_DEFAULT_OUTCOME=Failure yolunu kullan
# (yukarıda, "Senaryo kur").
curl -s -X POST localhost:8094/v1/scenarios -H 'Content-Type: application/json' \
  -d "{\"clientReference\":\"$WD\",\"outcome\":\"Failure\"}"

# Banka sonucu ANINDA vermiyor: BANK_SETTLEMENT_DELAY kadar bekliyor, sonra
# callback gönderiyor, sonra adaptörün relay'i cevabı yayınlıyor.
sleep 10
curl -s localhost:8093/v1/withdrawals/$WD; echo
echo "önce=$BEFORE sonra=$(curl -s localhost:8091/v1/wallets/$WALLET | jq -r .balance)"
```

Beklenen: saga `failed`, bakiye **başladığı yerde**.

Ledger'da altı satır — `withdrawal` üçlüsü ve tam aynası `refund` üçlüsü:

```bash
docker compose exec postgres psql -U postgres -d hiwallet_wallet -c \
  "SELECT t.type, la.type AS hesap, e.amount FROM ledger_entries e
     JOIN ledger_accounts la ON la.id = e.ledger_account_id
     JOIN ledger_transactions t ON t.id = e.transaction_id
    WHERE t.correlation_id = '$WD' ORDER BY t.created_at, e.amount;"
```

```
    type    |    hesap    |  amount
------------+-------------+-----------
 withdrawal | user_wallet | -102.0000
 withdrawal | revenue     |    2.0000
 withdrawal | clearing    |  100.0000
 refund     | clearing    | -100.0000
 refund     | revenue     |   -2.0000
 refund     | user_wallet |  102.0000
```

`refund` satırındaki **`revenue -2` kritik**. O bacak atlansaydı kayıt yine dengeli
olurdu, trigger susardı, testler geçerdi — ama müşteri gerçekleşmemiş bir işlemin
komisyonunu ödemiş kalırdı. Ters kayıt bu yüzden politikadan yeniden ÜRETİLMİYOR:
orijinal işlemin bacakları okunup negatifleniyor.

---

## Health check endpoint'leri

```bash
curl -s localhost:8091/health/ready
```
```json
{
  "status": "Healthy",
  "durationMs": 0.4953,
  "checks": [
    { "name": "postgres", "status": "Healthy", "durationMs": 0.4189, "error": null }
  ]
}
```

**`checks` listesi uygulamanın bağımlılıklarını sayıyor.** `wallet-api` yalnızca
Postgres'e bağlanıyor. Broker bağlantısı `wallet-consumer`'da; mesajları o çekiyor
ve ledger'a o yazıyor (`decisions.md` madde 28).

Orchestrator ikisini birden sayıyor:

```bash
curl -s localhost:8093/health/ready
```
```json
{
  "status": "Healthy",
  "durationMs": 2.0093,
  "checks": [
    { "name": "postgres", "status": "Healthy", "durationMs": 0.4396, "error": null },
    { "name": "rabbitmq", "status": "Healthy", "durationMs": 1.9646, "error": null }
  ]
}
```

Broker durdurulduğunda bu endpoint `200` dönmeye devam eder, yalnızca `status` alanı
`Degraded` olur. Çekim request'i kabul edilmeye devam ediyor çünkü komut outbox'a
yazılıyor ve broker döndüğünde yayınlanıyor (`decisions.md` madde 26 ve 32).

Kalan endpoint'ler aynı gövdeyi döndürüyor, yalnızca `checks` içerikleri farklı:

```bash
curl -s localhost:8092/health/ready   # topup-webhook  — postgres + rabbitmq
curl -s localhost:8094/health/ready   # bank-fake      — checks BOŞ (canlıda yok)
curl -s localhost:8095/health/ready   # bank-webhook   — postgres
curl -s localhost:8096/health/ready   # stripe-fake    — checks BOŞ (canlıda yok)
```

Sahte kurumların `checks` listesi boş: ikisinin de veritabanı yok, sağlıklı olmaları
process'in ayakta olduğunu söylüyor.

`wallet-consumer`'ın host'a açılmış portu yok; onun health check'i container'ın
içinden koşuyor ve sonucu `docker compose ps` çıktısında `healthy` olarak görünüyor.
