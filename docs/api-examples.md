# API örnekleri

Her uç için istek ve **beklenen** yanıt. Elle denemek ve bir şeyin bozulduğunu
anlamak için; sözleşmenin kaynağı kod, bu dosya ona uyar.

Yanıtlar compose'da koşan sistemden alındı (`localhost:8091-8094`). Kimlikler her
koşuda değişir.

```bash
set -a; . ./.env; set +a          # webhook secret'ları kabuğa gelsin
```

**Durum kodları neden bu şekilde:** `201` yaratıldı, `202` kalıcı olarak alındı ama
henüz işlenmedi, `400` girdi bozuk, `404` kayıt yok, `409` eşzamanlılık çakışması,
`422` istek geçerli ama iş kuralı reddetti. `409` ile `422` karıştırılmaz — birincisi
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

`400` değil `422`: `USD` geçerli bir ISO 4217 kodu, istek kusursuz. Reddin sebebi
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

`Idempotency-Key` opsiyonel ama önerilir. Aynı anahtarla ikinci istek yeni transfer
yapmaz:

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
Yanıt döndüğünde para henüz cüzdanda yok; hat webhook → inbox → relay → RabbitMQ →
wallet-consumer → ledger. Birkaç saniye sonra bakiyeye bak.

İmza **ham gövde baytları** üzerinde HMAC-SHA256. Gövdeyi yeniden serialize edersen
(boşluk, alan sırası) imza tutmaz.

Sağlayıcılar: `stripe-fake`, `bank-fake` — her birinin kendi secret'ı var.

<details><summary>Tekrar eden event → yine <code>202</code></summary>

Aynı `eventId` ile ikinci istek:

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

**`Idempotency-Key` ZORUNLU** — transfer'dekinin aksine. Çekim çok adımlı ve dışarıya
para çıkarıyor; anahtarsız bir tekrar ikinci bir banka transferi başlatırdı. Başlık
yoksa `400`.

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

`state` sırası: `initiated` → `debited` → `bank_transfer_pending` → `completed`.
Telafi yolunda: `debited` → `compensating` → `failed`. Reddedilmişse `rejected`.

`totalDebited` cüzdandan gerçekte çıkan toplam (tutar + komisyon). Wallet düşmeyi
yapana kadar `null` — `0` yazılmıyor, "komisyonsuz çekildi" ile karışırdı.

IBAN **maskeli** döner: müşteri zaten kendi girdi, tam hali yanıtta dolaşınca log'a,
hata izlemeye ve tarayıcı geçmişine de düşer.

<details><summary>Aynı anahtarla tekrar → <code>202</code>, <code>replayed: true</code></summary>

```json
{ "withdrawalId": "aynı-kimlik", "state": "completed", "replayed": true }
```
Yeni çekim AÇILMADI.
</details>

<details><summary>Yetersiz bakiye / limit aşımı → saga <code>rejected</code></summary>

`POST` yine `202` döner — istek geçerliydi ve kalıcı olarak alındı. Ret sonradan
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

## bank-service (sahte) — `:8094`

Gerçek bir bankada bu uç yoktur. Varlık sebebi "banka reddetti" durumunun
denenebilmesi.

### Senaryo kur

```bash
curl -i -X POST localhost:8094/v1/scenarios \
  -H 'Content-Type: application/json' \
  -d "{\"sagaId\":\"$WD\",\"outcome\":\"PermanentFailure\"}"
```
```
HTTP/1.1 204 No Content
```

`outcome`: `Success` | `PermanentFailure` | `TransientFailure` | `DelayedSuccess`.
`TransientFailure` için `transientFailures` (1-10, varsayılan 1) kaç kez geçici hata
üretileceğini, `DelayedSuccess` için `delayMilliseconds` (≤30000) gecikmeyi belirler.

Senaryo **saga başına** kuruluyor — yani çekimi başlattıktan sonra kurman gerekiyor,
kimliği önceden bilemezsin. Tüm çekimleri reddettirmek istersen varsayılanı değiştir:

```bash
BANK_DEFAULT_OUTCOME=PermanentFailure docker compose up -d --force-recreate --no-deps bank-service
docker compose exec bank-service printenv Bank__DefaultOutcome
```

Geri almak için aynı komutu değişkensiz çalıştır.

### Senaryonun durumu

```bash
curl -s localhost:8094/v1/scenarios/$WD
```
```json
{
  "sagaId": "...",
  "outcome": "TransientFailure",
  "remainingTransientFailures": 0,
  "attempts": 2
}
```

`attempts` retry'ın gerçekten çalıştığının kanıtı. Kurulmamış saga için `404`.

---

## Telafi yolu — asıl görülmesi gereken

Banka kalıcı olarak reddettiğinde para üç bacaklı ters kayıtla geri döner:

```bash
BEFORE=$(curl -s localhost:8091/v1/wallets/$WALLET | jq -r .balance)

WD=$(curl -s -X POST localhost:8093/v1/withdrawals \
  -H 'Idempotency-Key: cekim-red' -H 'Content-Type: application/json' \
  -d "{\"accountId\":\"$ACCOUNT\",\"walletId\":\"$WALLET\",\"amount\":100,\"currency\":\"TRY\",\"destinationIban\":\"TR330006100519786457841326\"}" | jq -r .withdrawalId)

curl -s -X POST localhost:8094/v1/scenarios -H 'Content-Type: application/json' \
  -d "{\"sagaId\":\"$WD\",\"outcome\":\"PermanentFailure\"}"

sleep 6
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

## Sağlık uçları

```bash
curl -s localhost:8091/health/ready   # wallet-api      — yalnızca postgres
curl -s localhost:8092/health/ready   # topup-webhook
curl -s localhost:8093/health/ready   # orchestrator    — postgres + rabbitmq
curl -s localhost:8094/health/ready   # bank-service
```

`wallet-api`'nin çıktısında `rabbitmq` **olmamalı** — o uygulamanın broker'a hiç işi
yok (`decisions.md` madde 28).

Broker durdurulduğunda orchestrator `Unhealthy` değil `Degraded` döner ve uç `200`
dönmeye devam eder: çekim isteği kabul edilmeye devam ediyor, komut outbox'ta
bekliyor ve broker döndüğünde yayınlanıyor (`decisions.md` madde 32).

`wallet-consumer`'ın host'a portu yok; sağlık kontrolü container'ın içinden koşuyor
(`docker compose ps` ile `healthy` görünür).
