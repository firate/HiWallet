# HiWallet

E-money cüzdan sistemi. Double-entry ledger + saga orchestration.

Referans uygulama: mimari production seviyesinde, kapsam bilinçli olarak dar. Dış
kurumlar sahte ama **sınırları gerçek** — banka ayrı bir process, arada HTTP ve
callback var, wallet'ı göremiyor. Katman ayrımı, transaction sınırları, idempotency,
concurrency stratejisi ve invariant zorlaması gerçekte nasıl yapılıyorsa öyle.

## Ne gösteriyor

**Çekirdek (immediate, ACID).** Cüzdanlar arası transfer: tek serviste, tek DB,
double-entry, optimistic lock. Saga yok — dağıtık karmaşıklık tutarlılığın kritik
olduğu yere taşınmıyor.

**Kenar (eventual).** Dış dünyayla konuşan akışlar. İki hat da çalışıyor:

- **Kartla yükleme:** limit yetmiyorsa ödeme hiç açılmıyor; wallet ödeme açılmadan önce
  limitten pay ayırıyor. Ödemenin ömrünü ayrı bir iç servis tutuyor, sağlayıcının bildirimi
  ayrı bir webhook servisinden RabbitMQ üzerinden ona, kapanış da wallet'a gidiyor.
- **Withdrawal saga:** orchestrator kendi veritabanında saga durumunu yürütüyor,
  wallet parayı düşüyor, adaptör bankayı HTTP ile arıyor. Banka "aldım" diyor,
  **sonuç sonra** callback ile geliyor — saga o arada gerçekten bekliyor. Banka
  reddederse **compensation** cüzdana parayı geri yazıyor: silmeyle değil, üç
  bacaklı ters kayıtla.

## On dört uygulama: önde ön API'ler, içeride cüzdan

| deployable | ingress | Postgres | RabbitMQ |
| --- | --- | --- | --- |
| `personal-mobile-api` | **public** — ön API: bireysel mobil uygulama | — | — |
| `personal-web-bff` | **public** — ön API: bireysel web uygulamasının BFF'i | — | — |
| `business-api` | **public** — ön API: işyerinin sistem entegrasyonu | — | — |
| `business-web-bff` | **public** — ön API: işyeri panelinin BFF'i | — | — |
| `backoffice-bff` | **iç ağ** — ön API: backoffice panelinin BFF'i | — | — |
| `wallet-api` | **iç ağ** — ön API'ler çağırıyor | `hiwallet_wallet` / `wallet_app` | — |
| `onboarding` | **iç ağ** — kayıt ve kimlik doğrulaması | `hiwallet_onboarding` / `onboarding_app`, kendi Postgres sunucusu | — |
| `staff-admin` | **iç ağ** — personel yönetimi: çalışanlar, roller, izinler | `hiwallet_staff_admin` / `staff_admin_app`, kendi Postgres sunucusu | — |
| `topup-webhook` | **IP kısıtlı** — sağlayıcı | `hiwallet_topup` / `topup_app` | publish |
| `wallet-consumer` | **yok** | `hiwallet_wallet` / `wallet_app` | consume |
| `withdrawal-orchestrator` | **iç ağ** — çekim saga'sı | `hiwallet_withdrawal` | ikisi de |
| `card-topup` | **iç ağ** — kartla yükleme: limit payı, ödeme, kapanış | `hiwallet_card_topup` / `card_topup_app` | ikisi de |
| `bank-adapter` | **yok** | `hiwallet_bank` / `bank_app` | ikisi de |
| `bank-webhook` | **IP kısıtlı** — banka | `hiwallet_bank` / `bank_app` | — |
| `hiwallet-keycloak` | **public** — müşterilerin ve işyerlerinin kimlik sağlayıcısı (bizim kodumuz değil) | kendi Postgres sunucusu | — |
| `hiwallet-staff-keycloak` | **iç ağ** — çalışanların kimlik sağlayıcısı, ayrı kurulum (bizim kodumuz değil) | kendi Postgres sunucusu | — |

İstemci yalnızca kendi ön API'sine bağlanıyor. `wallet-api`, orchestrator ve `card-topup` iç servis;
ön API veritabanına bağlanmıyor ve ledger'a giden her istek `wallet-api`'den geçiyor. Ön
API'ler ihtiyaç doğdukça açılıyor, her biri public ya da yalnızca iç ağdan erişiliyor.
Tarayıcıdan kullanılan arayüzün ön API'si BFF: token tarayıcıdaki koda verilmez, tarayıcı
yalnızca HttpOnly oturum cookie'si taşır. `personal-mobile-api`, `personal-web-bff` ve
`business-api`'nin uçları yazıldı; `backoffice-bff`'in görüntüleme uçları var, işyeri
BFF'i sağlık uçlarıyla ayakta.

Token'ı Keycloak imzalıyor. Müşterilerin ve çalışanların Keycloak'ı ayrı kurulum: kendi
veritabanı, kendi yöneticisi. Ön API token'ı doğruluyor ve iç servise aynen iletiyor;
iç servis yeniden doğruluyor. Hangi kimliğin hangi hesabın kullanıcısı olduğu wallet'ta
duruyor ve müşteri yalnızca kendi hesabına erişiyor.

Kayıt `onboarding`'de: e-posta kodu ve parola, sonra Keycloak'ta kullanıcı ve wallet'ta
hesap. Parola Keycloak'ta, kişisel veri (e-posta, telefon, TCKN, onaylar) onboarding'in
kendi Postgres sunucusunda, ledger'da yalnızca doğrulamanın sonucu: hesabın seviyesi.
Seviye (`Unknown`, `Unverified`, `Verified`, `Contracted`) hangi hareketin ayda ne kadar
yapılabildiğini belirliyor. Giriş Keycloak'ın sayfasında, HiWallet temasıyla.

Webhook'larda ve ingress'siz uygulamalarda ayrımın sebebi erişim seviyesi: banka
webhook'u belirli IP bloklarına açılacak. IP kısıtı process seviyesinde uygulanamaz.

Bir de **başka kurumların yerinde duran** uygulamalar var:

| | temsil ettiği kurum | ne yapıyor |
| --- | --- | --- |
| `bank-fake` | bankamız | para girişi **ve** çıkışı; hafızası bellekte, veritabanı yok |
| `stripe-fake` | kart sağlayıcısı | yalnızca para girişi: ödeme API'si, ödeme sayfası, sonucun webhook'u; veritabanı yok |
| `sms-fake` | SMS sağlayıcısı | mesajı göndermiyor, kutusunda tutuyor; veritabanı yok |
| `nvi-fake` | nüfus kaydı | kimlik bilgisi eşleşiyor mu; eşleşmeyen numara senaryoyla |
| `mailpit` | e-posta sağlayıcısı | SMTP'yi kabul ediyor, dışarı göndermiyor (bizim kodumuz değil) |

Hepsi canlıda yok — yerlerine kurumların kendi endpoint'leri geçiyor. `.Fake` son ekinin
ölçütü "test amaçlı mı" değil, "başka bir kurumun yerine mi duruyor" (`decisions.md`
madde 35).

`bank-fake`'in iki yönde de çalışması tesadüf değil: aynı banka hem gelen havaleyi
bildiriyor hem giden transferi kabul ediyor. Ledger'da da öyle — `clearing/bank-fake`
iki yönde de hareket ediyor. Stripe'ın `nostro`'su yok, çünkü nostro bir banka hesabı.

Kodları da `src/` altında değil, kökteki **`fakes/`** klasöründe: canlıda deploy
edilen hiçbir şey oradan çıkmıyor. `src/` → `fakes/` referansı derleme hatası
(`HIW001`) — kural yorumda değil, derleyicide.

Ölçüt iki yöne de işliyor: farklı erişim seviyesi aynı process'te birleşmiyor, **aynı
erişim seviyesi de gereksiz bölünmüyor.** `wallet-consumer` kartla yüklemelerin
kapanışlarını, havaleleri ve çekim komutlarını birlikte dinliyor, çünkü hepsi ingress'siz ve
aynı ledger'a aynı kütüphaneyle yazıyor.

`wallet-api` ve `wallet-consumer` aynı şemayı yazıyor ve **aynı kütüphaneyi**
(`WalletService.Core`) paylaşıyor. Ayrı deployable, tek kod tabanı — çünkü ledger
invariant'larının bir kısmı hiçbir constraint tarafından zorlanmıyor (cüzdanın negatife
düşememesi, bakiye satırlarının artan id sırasıyla güncellenmesi, `version`'ın birer
artması). İkinci bir kopya, ilk sapmada sessizce bozulurdu.

`wallet-api`'nin RabbitMQ bağımlılığı **yok**: yalnızca Postgres'e bağlı. Ön API'lerin
ne Postgres ne RabbitMQ bağlantısı var.

Ana mesaj bu ayrımda: **tutarlılığın kritik olduğu çekirdeği tek boundary'de ACID tut,
sadece dışarıyla konuşan kenarı dağıt.**

## Şu an ne çalışıyor

| | durum |
| --- | --- |
| Hesap ve cüzdan endpoint'leri (`/v1/accounts`, `/v1/wallets`) | evet |
| Transfer çekirdeği (5 tip), limit ve komisyon | evet |
| Double-entry ledger, zero-sum invariant | evet — DB trigger + testler |
| Optimistic lock, retry, idempotency | evet |
| `POST /v1/transfers`, ProblemDetails | evet |
| Baseline: OTel, health, rate limiting, validation | evet |
| Kartla yükleme: limit payı, ödeme, bildirim, kapanış, ledger | evet — testte; compose'da denenmedi |
| Kart limitte öncelikli: limit yetmiyorsa ödeme açılmıyor, açık pay gelen her parayla sayılıyor | evet — testte |
| Süresi dolan ve bildirimi kaçırılmış ödemenin taramayla kapanması | evet — testte |
| HMAC imza, inbox, dead-letter | evet |
| Deployable ayrımı erişim seviyesine göre | evet |
| Kartla yüklemenin gerçek bir broker'a karşı uçtan uca koşması | hayır — testler kuyrukları atlayıp handler'ları çağırıyor |
| Withdrawal saga: state machine, outbox, IBAN doğrulama | evet |
| Compensation: üç bacaklı ters kayıt (komisyon dahil) | evet |
| Saga zincirinin uçtan uca koşması | evet — API → wallet → adaptör → banka → callback → saga |
| Banka entegrasyonu: asenkron sonuç, callback + mutabakat | evet |
| Sahte kart sağlayıcısı: ödeme API'si, ödeme sayfası, imzalı webhook | evet |
| Sekiz container'ın compose'dan ayağa kalkması | evet |
| Ön API'lerin compose'dan ayağa kalkması | evet |
| `personal-mobile-api`'nin uçları: cüzdan, transfer, çekim | evet — `wallet-api` ve orchestrator'a iletiyor |
| `business-api`'nin uçları: hesap, cüzdan, transfer, müşteriye promo, çekim | evet — işyerinin entegrasyonu client credentials ile |
| Bireysel web uygulaması: kayıt, doğrulama, giriş, cüzdan, hareketler, transfer, çekim | evet |
| BFF oturumu: şifreli cookie, token yenileme, X-CSRF | evet |
| Backoffice paneli: müşteri kaydı, çekim incelemesi, personel promo'su, kampanyalar | evet — testte; panel compose'da denenmedi |
| İşyeri BFF'inin uçları | hayır — sağlık uçlarıyla ayakta |
| Müşteri başına rate limit ön API'de | evet — anahtar token'daki kimlik; iç servislerde yok |
| Kimlik doğrulama: Keycloak, token ön API'de ve iç serviste doğrulanıyor | evet — testte kendi imzaladığı token'la |
| Sahiplik: müşteri yalnızca kullanıcısı olduğu hesaba erişiyor | evet — çekimde wallet düşmeden önce doğruluyor |
| Hesap numarası: on hane, Luhn kontrol hanesi; gelen para varsayılan cüzdana | evet — testte; panelde numarayla arama, uygulamada numaraya gönderim |
| Keycloak'ın compose'dan ayağa kalkması | evet — işyerinin token'ıyla `business-api` üzerinden `wallet-api`'ye kadar |
| Her ön API yalnızca kendisi için verilmiş token'ı kabul ediyor | evet — `aud` |
| Çalışan kimliği: ayrı Keycloak kurulumu, izinle yetki, OTP zorunlu | evet — giriş ve OTP compose'da denendi; izin modeli testte |
| Personel yönetimi: izinler kodda; roller, çalışanlar ve kayıt panelden, personel yönetiminin veritabanında | evet — testte; compose'da denenmedi |
| Çalışanın izni her istekte: rolü alınan çalışanın bir sonraki isteği reddediliyor | evet — testte |
| Kayıt: e-posta kodu, parola, Keycloak'ta kullanıcı, wallet'ta hesap | evet |
| Temel doğrulama: telefon (SMS), kimlik (nüfus kaydı), sözleşme ve aydınlatma metni | evet |
| Telefon değiştirme: parolayla yeniden giriş, yeni numaraya kod, bankaya çekim 24 saat kapalı | evet — testte; compose'da denenmedi |
| Panelde müşterinin kişisel bilgisi (maskeli) ve e-posta, telefon ya da kimlik numarasıyla arama | evet — testte; compose'da denenmedi |
| Doğrulama seviyesine göre aylık limitler | evet — transfer, ödeme, çekim, havale, kartla yükleme |
| Kimliği tespit edilmemiş seviyede ayın toplam girişi ve bakiye tavanı (5.500 TL) | evet — testte |
| Havale ile yükleme: toplama hesabı, açıklamadaki hesap numarası, yalnızca kendi hesabından | evet — testte; compose'da denenmedi |
| Eşleşmeyen havale askıya, panelde liste | evet — testte |
| Askıdaki havalenin kaynağa iadesi ya da bir cüzdana aktarılması | hayır |
| `Verified`: uzaktan kimlik tespiti (kimlik kartının çipi, canlılık, yüz) | hayır |
| `Contracted`: backoffice'ten | hayır |
| Takılmış saga taraması (job altyapısı + advisory lock) | evet |
| Business günlük özeti | evet |
| Sağlayıcı ücreti tahakkuku (`provider_fees`, Net/Invoiced) | evet |
| Settlement: clearing kapanır, `nostro` hareket eder | evet — kartla yükleme ve çekim |
| Fatura işleme, uyuşmazlıkta `PendingReview` | evet |
| Promo: işyerinin kendi müşterisine verdiği parti, ödemede harcama, süre sonu | evet |
| Promo: kampanya motoru, platform fonlu parti, koruma hesabı açığı raporu | evet — kampanyalar SQL ile |
| Promo: personel promo'su, kampanya yönetimi, işyerinin promo kabulü | evet — backoffice'ten, kendi izinleriyle |
| Çekim incelemesi: eşiğin üstü bekliyor, çalışan serbest bırakıyor ya da iptal ediyor | evet — iptal banka reddinden ayrı durumda |
| Koruma hesabının fonlama kaydı | hayır |
| Mutabakat raporu (projeksiyon, yaşlanma, fatura) | evet |
| Çekim settlement'ı (banka ücreti saga üzerinden) | evet |
| Relay tekilliği: sıra broker'a varmadan bozulmuyor | evet — advisory lock |

827 test: 256 unit (DB'siz), 571 integration — gerçek Postgres ve gerçek RabbitMQ.
Web uygulamalarının testleri ayrı (Vitest): bireysel uygulamanın 57, panelin 40.

Withdrawal zinciri broker'la uçtan uca koşuyor: `POST /v1/withdrawals` → orchestrator →
wallet-consumer → bank-adapter → (HTTP) banka → callback → bank-webhook → inbox → relay →
orchestrator. Beş host ayrı ayrı ayakta; aralarında hem broker hem gerçek HTTP var.
Kartla yüklemenin testi ön API → card-topup → wallet-api ve sahte sağlayıcı arasında
gerçek HTTP ile koşuyor; kuyruk halkaları handler'lara doğrudan veriliyor.

## Çalıştırma

```bash
cp .env.example .env      # <DOLDUR> yazan yerleri doldur
docker compose up --build
```

Sırayla: Postgres ayağa kalkar, altı rol, beş uygulama veritabanı ve integration testlerin
veritabanı (`hiwallet_schema_check`) kurulur; onboarding'in ve personel yönetiminin
Postgres'i kendi rolleriyle ayrıca kalkar → yedi migrator şemaları uygular → uygulamalar
başlar (on dördü bizim, beşi başka kurumların yerinde). RabbitMQ paralel kalkar; hiçbiri
onu BEKLEMEZ.

```bash
curl http://localhost:8091/health/ready   # wallet-api
curl http://localhost:8092/health/ready   # topup-webhook
curl http://localhost:8093/health/ready   # withdrawal-orchestrator
curl http://localhost:8094/health/ready   # bank-fake (BİZİM DEĞİL, canlıda yok)
curl http://localhost:8095/health/ready   # bank-webhook
curl http://localhost:8096/health/ready   # stripe-fake (BİZİM DEĞİL, canlıda yok)
curl http://localhost:8097/health/ready   # personal-mobile-api
curl http://localhost:8098/health/ready   # business-api
curl http://localhost:8099/health/ready   # backoffice-bff
curl http://localhost:8100/health/ready   # business-web-bff
curl http://localhost:8102/health/ready   # personal-web-bff
curl http://localhost:8103/health/ready   # onboarding
curl http://localhost:8104/health/ready   # sms-fake (BİZİM DEĞİL, canlıda yok)
curl http://localhost:8105/health/ready   # nvi-fake (BİZİM DEĞİL, canlıda yok)
curl http://localhost:8108/health/ready   # staff-admin
curl http://localhost:8109/health/ready   # card-topup
curl http://localhost:8101/realms/hiwallet/.well-known/openid-configuration         # keycloak, müşteriler
curl http://localhost:8107/realms/hiwallet-staff/.well-known/openid-configuration   # keycloak, çalışanlar
```

`wallet-api` (8091), orchestrator (8093) ve `card-topup` (8109) canlıda iç ağda;
compose'da elle denemek için host'a açıklar.

Bireysel web uygulaması <http://localhost:8102>'de. Kayıt uygulamanın kendi sayfasında
(`/kayit`): e-postaya giden kod Mailpit'te (<http://localhost:8106>), telefona giden kod
`sms-fake`'in kutusunda (`GET http://localhost:8104/v1/messages`). Giriş Keycloak'ın
sayfasından, HiWallet temasıyla. Oturum cookie'si yalnızca HTTPS'te ve `localhost`'ta
yazıldığı için başka bir makineden bu portla girilemiyor; orada ters proxy arkasındaki
adres kullanılıyor (`docs/verify-compose.md`).

Arayüzü geliştirirken Vite'ın sunucusu, arkada compose'daki BFF:

```bash
cd src/personal/edge/PersonalWeb && npm install && npm run dev   # http://localhost:5173
```

Backoffice paneli <http://localhost:8099>'da; giriş çalışanların Keycloak'ından, OTP ile.
Geliştirirken:

```bash
cd src/backoffice/edge/BackofficeWeb && npm install && npm run dev   # http://localhost:5174
```

`wallet-consumer`'ın host'a açılmış portu yok — health check container'ın içinden
koşuyor (`docker compose ps` ile görülür). Ingress'i olmayan bir uygulamanın port
açmasının sebebi olmazdı.

API dokümanı, yalnızca Development'ta. Ters proxy arkasında aynı sayfa
`https://hiwallet-<servis>.<PROXY_DOMAIN>/scalar/` adresinde:

| | Scalar arayüzü | Sayfadaki giriş |
| --- | --- | --- |
| `wallet-api` | <http://localhost:8091/scalar/> | müşteri, işyeri |
| `withdrawal-orchestrator` | <http://localhost:8093/scalar/> | müşteri, işyeri |
| `card-topup` | <http://localhost:8109/scalar/> | müşteri |
| `personal-mobile-api` | <http://localhost:8097/scalar/> | müşteri |
| `onboarding` | <http://localhost:8103/scalar/> | müşteri |
| `staff-admin` | <http://localhost:8108/scalar/> | — |
| `business-api` | <http://localhost:8098/scalar/> | işyeri |
| `backoffice-bff` | <http://localhost:8099/scalar/> | — |
| `business-web-bff` | <http://localhost:8100/scalar/> | — |
| `stripe-fake` | <http://localhost:8096/scalar/> | — |
| `bank-fake` | <http://localhost:8094/scalar/> | — |
| `sms-fake` | <http://localhost:8104/scalar/> | — |
| `nvi-fake` | <http://localhost:8105/scalar/> | — |

OpenAPI dokümanı her serviste `/openapi/v1.json`. Sondaki eğik çizgi bilerek: eğik
çizgisiz adres `302` ile ona yönleniyor. Tarayıcı takip ediyor, `curl` varsayılan
olarak etmiyor.

Doküman ve arayüz kimliksiz açık; uçları çağırmak token istiyor. Sayfadaki girişte
müşteri Keycloak'ın giriş sayfasına gidiyor, işyeri istemci kimliğini ve gizli
anahtarını giriyor. Ayrıntısı `docs/verify-compose.md` "Kimlik" bölümünde.

`topup-webhook`'ta yok: o sözleşmeyi sağlayıcı dayatıyor, biz belgelemiyoruz.

Host portlarının varsayılanı (`8091`–`8109`, `5433`, `5673`) alışıldık portlardan
bilerek kaçıyor: `8080`, `5432` ve `5672` geliştirme makinelerinde çoğu zaman dolu.
`.env`'den değiştirilebilir.

> **Stack compose'dan koşuyor ve uçtan uca akışları geçiyor.** Çekimin mutlu yolu ve
> telafi yolu compose üzerinde doğrulandı: banka reddettiğinde
> bakiye `500` → `500` dönüyor ve ters kaydın `revenue` bacağı yerinde. Yapısal
> tarafta uygulamalar `healthy`, migrator'lar şemaları uyguluyor, `wallet_app`
> konteyner içinde de `ledger_entries`'i güncelleyemiyor ve her rol yalnızca kendi
> veritabanına bağlanabiliyor. Adımlar, ölçülen çıktılar ve açık uçlar:
> **[docs/verify-compose.md](docs/verify-compose.md)**
>
> **Bu doğrulama bankanın SENKRON cevap verdiği sürümde yapıldı.** Asenkron hat
> (adaptör → banka → callback → webhook) testlerde koşuyor ama compose üzerinde
> henüz tekrarlanmadı; `verify-compose.md`'de açık uç olarak duruyor. Kartla yükleme de
> compose'da henüz denenmedi.

### İki veritabanı rolü

`migrator` **`wallet_owner`** ile, uygulama **`wallet_app`** ile bağlanır. Ayrım şart:
`ledger_entries` üzerindeki `REVOKE UPDATE, DELETE` yalnızca tablo sahibi **olmayan**
bir role işler — sahiplik yetkisi örtüktür ve revoke edilemez. Tek rolle çalışılsaydı
append-only kuralı tamamen süs olurdu.

Ölçülmüş hali (`AppRolePrivilegeTests`):

| rol | SELECT | UPDATE | DELETE |
| --- | --- | --- | --- |
| `wallet_app` | izin var | `42501` reddedildi | `42501` reddedildi |
| `wallet_owner` | izin var | izin var | izin var |

## Senaryolar

**Hesap ve cüzdan açma.** Diğer her şeyin başlangıcı; aşağıdaki `walletId`'ler buradan gelir.

```bash
ACCOUNT=$(curl -s -X POST http://localhost:8091/v1/accounts \
  -H 'Content-Type: application/json' -d '{"type":"Person"}' | jq -r .accountId)

WALLET=$(curl -s -X POST http://localhost:8091/v1/accounts/$ACCOUNT/wallets \
  -H 'Content-Type: application/json' -d '{"name":"Birikim","currency":"TRY"}' | jq -r .walletId)

curl -s http://localhost:8091/v1/wallets/$WALLET
```

Cüzdan sıfır bakiyeyle açılır ve **para yalnızca ledger üzerinden girer** — yükleme ya da
transfer. Bakiyeye doğrudan yazan bir endpoint yok, olsaydı zero-sum invariant'ı delerdi.

Bir hesabın aynı para biriminde birden fazla cüzdanı olabilir (`decisions.md` madde 20);
`GET /v1/accounts/{id}` hepsini bakiyeleriyle listeler. Günlük limit bu yüzden cüzdan
değil **hesap** bazında uygulanır.

**Komisyonlu ödeme.** Komisyon ayrı bir transfer değil, aynı atomik işlemin ek bacağı:

```bash
curl -X POST http://localhost:8091/v1/transfers \
  -H 'Content-Type: application/json' \
  -H "Idempotency-Key: $(uuidgen)" \
  -d '{"fromWalletId":"...","toWalletId":"...","amount":200,"currency":"TRY","type":"Payment"}'
```

Ledger'a üç satır düşer: gönderen `-204`, alan `+200`, `revenue` `+4`. Toplam sıfır.

**Idempotency.** Aynı `Idempotency-Key` ile ikinci request yeni transfer yapmaz:

```bash
curl -X POST http://localhost:8091/v1/transfers \
  -H 'Idempotency-Key: ayni-istek' -H 'Content-Type: application/json' -d '{...}'
```

İkinci response aynı `transactionId` ve `"replayed": true` döner.

**Yetersiz bakiye / limit aşımı** → `422` + `rule` alanı.
**Concurrency çakışması** (retry tükendi) → `409`. İkisi karıştırılmaz.

**Uçtan uca akışlar** (kayıt, havale, kart, ödeme, çekim) token'la birlikte
`docs/verify-compose.md`'de. Sahte servisleri tek başına denemek için Rider'da
`fakes/Bank.Fake/bank-fake.http` ve `fakes/Stripe.Fake/stripe-fake.http`; sağ üstten
ortamı seç.

**Kartla yükleme.** En kolayı bireysel web uygulamasından: cüzdanın sayfasında "Kartla para
yükle", tutarı yaz, ödeme sayfasında "Öde" de. Ödeme sayfası uygulamanın `/kart-yukleme`
sayfasına dönüyor; sonuç orada kesinleşene kadar izleniyor. API'den, müşterinin token'ıyla:

```bash
curl -X POST http://localhost:8097/v1/card-topups -H "Authorization: Bearer $TOKEN" \
  -H "Idempotency-Key: $(uuidgen)" -H 'Content-Type: application/json' \
  -d "{\"walletId\":\"$WALLET\",\"amount\":100,\"currency\":\"TRY\",\"returnUrl\":\"http://localhost:8102/kart-yukleme\"}"
```

Response **`202 Accepted`** ve `paymentUrl`: limit payı ayrıldı, ödeme açıldı, para henüz
hareket etmedi. Limit yetmiyorsa `422` (`card_topup_limit`) ve ödeme hiç açılmıyor.
Sayfada "Öde" denince sağlayıcının imzalı webhook'u topup-webhook'a, oradan RabbitMQ
üzerinden card-topup'a gidiyor; card-topup kapanışı wallet'a yolluyor. Ledger'a iki satır
düşer: cüzdan `+100`, `clearing/stripe-fake` `-100` (sağlayıcıdan alacak). Toplam sıfır.
Ödenmeyen ya da oturumu dolan ödemede ledger'a hiçbir şey yazılmıyor, yalnızca pay kapanıyor.

**Havale ile yükleme.** Sahte bankaya toplama hesabına havale geldiğini söyle. Cüzdana
geçmesi için açıklamada müşterinin hesap numarası, `senderNationalId`'de onun doğrulamada
verdiği kimlik numarası olmalı; yoksa para askıya düşer ve panelde "Askıdaki havaleler"de
görünür:

```bash
curl -X POST http://localhost:8094/v1/incoming-transfers -H 'Content-Type: application/json' \
  -d "{\"amount\":250,\"currency\":\"TRY\",\"description\":\"$ACCOUNT_NUMBER\",\"senderNationalId\":\"$TCKN\"}"
```

Kart sağlayıcısının webhook'unu elle göndermek istersen imza ham gövde baytları üzerinde
HMAC-SHA256; örnek `docs/verify-compose.md` "Ödeme bildirimini elle göndermek"te.
Response **`202 Accepted`** — `200` değil, bilerek: verilen söz "işledim" değil "kalıcı
kaydettim". Aynı webhook ikinci kez gelirse yine `202` döner ama `"duplicate": true` ve
bakiye değişmez. İmza tutmazsa `401` ve inbox'a **hiçbir şey** yazılmaz.

**Withdrawal (dışarıya para çıkışı).** Saga'nın evi. `Idempotency-Key` burada
**zorunlu** — transfer'dekinin aksine: çekim çok adımlı ve dışarıya para çıkarıyor,
anahtarsız bir tekrar ikinci bir banka transferi başlatırdı.

```bash
curl -X POST http://localhost:8093/v1/withdrawals \
  -H 'Idempotency-Key: cekim-1' -H 'Content-Type: application/json' \
  -d '{"accountId":"...","walletId":"...","amount":100,"currency":"TRY",
       "destinationIban":"TR33 0006 1005 1978 6457 8413 26"}'
```

Response **`202 Accepted`**: döndüğünde hiçbir para hareket etmemiş durumda. IBAN sınırda
mod-97 ile doğrulanıyor; geçersizse `400` ve saga hiç başlamıyor.

Mutlu yolda ledger'a üç satır düşer: cüzdan `-102`, `clearing/bank-fake` `+100`,
`revenue` `+2`.

Durumu `GET /v1/withdrawals/{id}` ile izleyebilirsin: `initiated` → `debited` →
`bank_transfer_pending` → `settling` → `completed`.

`bank_transfer_pending`'de birkaç saniye takılı görmen normal ve **istenen şey**:
banka çağrısı "aldım" diyor, sonuç callback ile sonra geliyor (`decisions.md`
madde 35). Süreyi `BANK_SETTLEMENT_DELAY` belirliyor.

**Banka reddederse compensation.** Sahte bankaya söyleyerek tetiklenir — anahtar
saga kimliği, alan adı `clientReference` (bankanın gözünde bizim referansımız):

```bash
curl -X POST http://localhost:8094/v1/scenarios -H 'Content-Type: application/json' \
  -d '{"clientReference":"<ID>","outcome":"Failure"}'
```

Ters kayıt orijinalin aynası ve **üç bacaklı**:
cüzdan `+102`, clearing `-100`, `revenue` `-2`. Komisyon iadesi koşulsuz — başarısız
bir çekimin sebebi ya bizde ya bankada.

`revenue` bacağının kritikliği şurada: atlansaydı kayıt **yine dengeli** olurdu,
zero-sum trigger'ı susardı ve müşteri gerçekleşmemiş bir işlemin komisyonunu ödemiş
kalırdı. Bu yüzden ters kayıt politikadan yeniden üretilmiyor — orijinal işlemin
bacakları okunup negatifleniyor.

Orijinal kayıt **silinmiyor**; ledger append-only, saga başına iki işlem kalıyor.

**Zero-sum, yük altında.** `EszamanliTransferler_ZeroSumKorunur_VeHicbirCuzdanNegatifDusmez`
500 eşzamanlı transfer atıyor ve dört şeyi doğruluyor: her transaction'ın toplamı sıfır,
sistem genelinde toplam sıfır, hiçbir cüzdan negatif değil, projeksiyon ledger'dan
sapmamış. Testin iddiası "her transfer başarılı olur" değil — çakışanlar `409` alıyor —
**"ne olursa olsun invariant korunur."**

## Testler

```bash
dotnet test
cd src/personal/edge/PersonalWeb && npm test
cd src/backoffice/edge/BackofficeWeb && npm test
```

Integration testler bir Postgres sunucusu ister; bağlantı
`ConnectionStrings__IntegrationTests`'ten gelir. Her koşu kendi schema'sını açar,
migration'ı oraya uygular, sonunda düşürür — izolasyon böyle sağlanıyor, Docker
gerekmiyor. topup-webhook, withdrawal-orchestrator, card-topup, banka entegrasyonu ve sahte banka için ayrı schema'lar
açılıyor: canlıdaki ayrı veritabanı sınırları testte de korunuyor, servisler
birbirinin tablosunu göremiyor.

Uçtan uca testler ayrıca bir RabbitMQ ister (`RabbitMq__*`); eklenti istemiyor, bütün
topolojiler düz direct ve fanout exchange. Broker'a ulaşılamıyorsa test
`Assert.SkipUnless` ile atlanıyor ve mesajda eksiğin ne olduğu yazıyor — kurulumu
zorunlu kılmak yerine varsa doğrulanıyor, atlanan test yeşil değil "skipped" görünüyor.

Her koşu kendine özel bir exchange/kuyruk ön eki kullanıyor (`RabbitMq:NamePrefix`) ve
sonunda topolojiyi siliyor (`BrokerCleanup`): aynı broker'a bakan iki koşu birbirinin
kuyruğundan mesaj çekmiyor, broker'da da çöp birikmiyor. Yarıda kesilen koşunun artığı
kalıyor.

```bash
set -a; . ./.env; set +a
dotnet test
```

## Dokümanlar

| dosya | ne anlatır |
| --- | --- |
| [docs/architecture.md](docs/architecture.md) | **diyagramlar**: topoloji, akışlar, paranın nerede durduğu |
| [docs/overview.md](docs/overview.md) | sistem: kapsam, servisler, akışlar, saga, çıkış kriteri |
| [docs/baseline.md](docs/baseline.md) | uygulamadan bağımsız 12 zorunlu katman |
| [docs/decisions.md](docs/decisions.md) | kararlar, gerekçeler, **elenen alternatifler** |
| [docs/ledger-schema.md](docs/ledger-schema.md) | şemanın okunabilir karşılığı |
| [docs/structure.md](docs/structure.md) | yeni dosya nereye konur |
| [docs/api-examples.md](docs/api-examples.md) | her endpoint için request ve beklenen response |
| [docs/verify-compose.md](docs/verify-compose.md) | compose'u ayağa kaldırma ve doğrulama |
| [CLAUDE.md](CLAUDE.md) | pazarlıksız kurallar |

Şemanın tek kaynağı EF migration'ları; `ledger-schema.md` onları açıklar, üretmez.

### Hangi sırayla okunur

**Baştan sona okunacak tek doküman yok.** Hangisini açacağın ne yaptığına bağlı:

**Sistemi tanımak ya da hatırlamak için:** `architecture.md` → `overview.md` →
`ledger-schema.md`. İlki en hızlı resmi veriyor, hepsi diyagram. `overview.md`'nin
1–10 numaralı maddeleri sistemi anlatıyor; o numaralara `decisions.md` ve kod
yorumları atıf yapıyor, bu yüzden sabitler. Üçüncüsünü ancak tablo yapısı
gerektiğinde aç.

**Kod yazarken:** `CLAUDE.md` → `structure.md` → `decisions.md`'de ilgili madde.
İlki kuralların listesi, ikincisi yeni dosyanın nereye konacağı. `decisions.md`
baştan sona OKUNMAZ — 1400 satır ve referans niteliğinde; merak ettiğin maddeyi ara
(banka entegrasyonu 35, servis sınırı 7 ve 33, aktör 34).

**Elle denerken:** `fakes/` altındaki `.http` dosyaları → `api-examples.md` →
`verify-compose.md`. İlki Rider'da en hızlı yol, ikincisi bir şey bozulduğunda
karşılaştırman için beklenen response'ları veriyor.

**Yeni karar alırken:** `decisions.md`. Karar oraya gerekçesiyle yazılıyor, sonra
`CLAUDE.md`'ye ve ilgili dokümanlara yayılıyor.

`baseline.md` nadiren açılır: uygulamadan bağımsız ve neredeyse hiç değişmiyor.

## Bilinçli sınırlamalar

Eksik değil, **elenmiş** — gerekçeleri `decisions.md` madde 12'de:

- **Rate limiting in-memory.** Çok instance'ta efektif limit instance başınadır.
- **Secret yönetimi `.env` + User Secrets.** Vault yok.
- **Caching yok.** Bakiye projeksiyonu cache değil, kalıcı read tablosu.
- **Multi-tenancy yok.** Person/business ayrımı hesap tipidir, tenancy değil.

Ayrıca kayda geçirilmiş, henüz sorun olmayan bir darboğaz var: komisyonlu her transfer
tek `revenue` satırını güncelliyor ve eşzamanlı komisyonlu transferler cüzdanları farklı
olsa bile çakışıyor (`decisions.md` madde 23).

## Lisans

MIT — [LICENSE](LICENSE).

Bu bir **referans uygulamasıdır**, canlıya çıkmaya hazır bir e-para sistemi değil. Yukarıdaki
bilinçli sınırlamalar okunmadan canlıda kullanılmamalı. `stripe-fake` ve
`bank-fake` yerel simülatörlerdir; hiçbir ödeme sağlayıcısıyla ilişkisi yoktur.
