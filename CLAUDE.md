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

**Hesap numarası ve varsayılan cüzdan**
- Her hesabın insanın kullandığı on haneli numarası var (`accounts.number`): rastgele,
  sıralı DEĞİL; son hane Luhn kontrol hanesi; bir kez verilir ve DEĞİŞMEZ. Kimlik (`id`)
  içeride kalır; servisler, yabancı anahtarlar ve idempotency kapsamı numarayı KULLANMAZ.
- Hesap numarasına gelen para alıcının o para birimindeki VARSAYILAN cüzdanına düşer
  (`default_wallets`). Hesabın cüzdanı olan her para biriminde tam bir varsayılan var:
  ilk cüzdan kendiliğinden varsayılan, değiştiren yalnızca müşteri. Alıcının o para
  biriminde cüzdanı yoksa `422`; hesabında kendiliğinden cüzdan AÇILMAZ.
- Numara sınırda (wallet-api'nin ucu) cüzdana çevrilir; transfer çekirdeği cüzdandan
  cüzdana kalır. Kontrol hanesi tutmayan numara `400`, başkasının numarası `404`:
  numaranın bir hesaba ait olduğu ve hesabın kimliği dışarı verilmez.

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
- Personel promo'su platform fonlu, aktörü çalışan ve para birimi başına tek seferlik
  tavanla sınırlı; tavanı tanımlı olmayan para biriminde VERİLMEZ.
- Kampanya tabanına (eşik toplamı, yüzde ödül) promo payı ve komisyon GİRMEZ.
- Kampanya değerlendirmesi id cursor'ıyla İLERLEMEZ: id sırası commit sırası değil.
  Değerlendirilen ödeme `promo_campaign_evaluations`'a işaretlenir.

**Concurrency**
- wallet-service'te optimistic lock `ledger_balances.version` üzerinde.
  `ledger_entries` üzerinde lock YOK.
- `IsConcurrencyToken()` yalnızca GERÇEKTEN UPDATE edilen ve birden fazla yazarı olan
  satırlarda: wallet'ta `LedgerBalance`, orchestrator'da `WithdrawalSaga`, card-topup'ta
  `CardTopup`. Başka entity'ye EKLENMEZ — append-only tabloda anlamsız (`decisions.md` madde 2).
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
- `card_topups(subject, idempotency_key)` UNIQUE: kartla yüklemenin kapsamı isteyen kimlik.
- Para hareketi başlatan HER endpoint'te `Idempotency-Key` başlığı ZORUNLU — transfer dahil.
  Yoksa `400`. Anahtarsız bir tekrar hiçbir constraint'e takılmaz ve çift harcama
  sessizce ledger'a yazılır; append-only olduğu için de geri alınamaz, yalnızca
  ters kayıtla düzeltilir.
- Kalıp: `INSERT ... ON CONFLICT DO NOTHING`, 0 satır ise mevcut kaydı oku ve onu dön.
  "Önce SELECT sonra INSERT" YOK.

**Deployable'lar**
- **`wallet-api`, `withdrawal-orchestrator`, `card-topup`, `onboarding` ve `staff-admin` İÇ
  servis;** istemci onlara doğrudan bağlanmaz. Orchestrator kendi sınırı ve kendi veritabanı
  (madde 7 ve 33); card-topup da öyle.
- Dışarıya açılan her yüzey bir **ön API**. Ön API ihtiyaç doğdukça açılır, kendi
  istemcisine hizmet eder ve ya public ya da yalnızca iç ağdan erişilir:
  `personal-mobile-api`, `personal-web-bff`, `business-api` ve `business-web-bff` public,
  `backoffice-bff` iç ağda.
- Tarayıcıdan kullanılan arayüzün ön API'si **BFF**: token tarayıcıdaki koda VERİLMEZ.
  Tarayıcı yalnızca HttpOnly oturum cookie'si taşır; token'lar cookie'nin içinde, BFF'in
  anahtarıyla şifreli. BFF'in adı `-bff` ile biter. Token taşıyan istemci (mobil
  uygulama, sistem entegrasyonu) ile cookie taşıyan arayüz aynı ön API'yi PAYLAŞMAZ.
- BFF'in API istekleri `X-CSRF` başlığı ister; `SameSite` aynı sitenin başka alt alan
  adından gelen isteği kesmez. Oturumsuz API isteği giriş sayfasına YÖNLENDİRİLMEZ,
  `401` alır; girişi uygulama başlatır.
- **Ön API veritabanına BAĞLANMAZ** ve `WalletService.Core`'a referans vermez. Ledger'a
  giden her istek iç ağdaki `wallet-api`'den geçer. Public process'te `wallet_app`
  parolası durmaz.
- Ön API iç servisin reddini (400, 404, 409, 422) AYNEN aktarır, yeniden yorumlamaz;
  iç servise ulaşılamazsa `503`. POST'u yeniden DENEMEZ: tekrar istemcinin işi, aynı
  `Idempotency-Key` ile. İç servisin adresi dışarı çıkmaz, `Location` dahil.
- Müşteri başına rate limit ön API'de. İç servislerde müşteri limiti YOK: yalnızca ön
  API'lerin adresini görüyorlar ve IP'ye göre bölünen bir kova bütün müşterileri tek
  kovaya koyar. Anahtar token'daki kimlik; kimliksiz istek IP'nin kovasına düşer.
- Ingress'i olmayan ve webhook alan deployable'larda ölçüt ERİŞİM SEVİYESİ
  (`decisions.md` madde 28): `topup-webhook` IP kısıtlı, `wallet-consumer` ingress'siz.
  Farklı erişim seviyesi aynı process'te BİRLEŞTİRİLMEZ. Aynı erişim seviyesi ise ayrı
  process'e BÖLÜNMEZ — `wallet-consumer` kartla yüklemelerin kapanışlarını, havaleleri ve
  çekim komutlarını dinliyor, hepsi ingress'siz ve aynı ledger'a yazıyor.
- `wallet-api` ve `wallet-consumer` ortak kütüphane `WalletService.Core` üstünde.
  Ledger'a yazan kodun tek kopyası orada; ikinci bir kopya AÇILMAZ (madde 25).
- **`WalletService.Core`'a wallet sınırı dışından referans verilmez.**
  `topup-webhook` onu görmez.
- `wallet-api`'nin ve ön API'lerin RabbitMQ bağımlılığı YOK ve eklenmez.
- **`.Fake` son eki yalnızca BAŞKA BİR KURUMUN yerine duran servise konur**
  (`decisions.md` madde 35). Kendi yazdığımız ve canlıda da koşacak servis normal ad
  alır — bugün yalnızca testte koşuyor olması son ek sebebi DEĞİL. `bank-adapter`
  bizim, son ek almaz; `Bank.Fake` bankanın API'sinin yerine duruyor, alır.

**Kimlik doğrulama**
- Kimlik sağlayıcı Keycloak. Token'ı ön API doğrular ve iç servise AYNEN iletir; iç
  servis onu YENİDEN doğrular. Ön API'ye körü körüne güvenilmez: kimlik başlıkla
  taşınmaz, iç servis ön API'nin beyanını değil token'ı okur.
- Her ön API yalnızca KENDİSİ İÇİN verilmiş token'ı kabul eder: token'ın hedef
  kitlesinde (`aud`) ön API'nin adı var. İç servisler `hiwallet-api`'yi arar. Mobil
  uygulamanın token'ı `business-api`'de, işyerinin token'ı mobil ön API'de geçmez.
- BFF'in girişi Keycloak'la kod akışı ve PKCE, BFF'in gizli anahtarlı kendi
  istemcisiyle. Access token dolmak üzereyken BFF yeniler; aynı refresh token için TEK
  istek: Keycloak refresh token'ı her kullanımda değiştiriyor ve eskisini kabul etmiyor,
  eşzamanlı iki yenileme oturumu kapatırdı.
- Varsayılan politika kimlik ister ve ÇALIŞANI DIŞARIDA BIRAKIR: yeni bir uç
  kendiliğinden çalışana kapalı açılır. Kimliksiz açık kalan uç (sağlık, API dokümanı)
  `AllowAnonymous` ile, çalışanın geçebildiği uç kendi politikasıyla bunu söyler.
- Çalışanların Keycloak'ı AYRI KURULUM (`hiwallet-staff-keycloak`, realm'i
  `hiwallet-staff`): kendi veritabanı sunucusu, kendi yöneticisi. Müşterilerinki
  (`hiwallet-keycloak`: bireysel ve işyeri) ile aynı process'te ya da veritabanında
  DURMAZ; bir kurulumun yöneticisi ötekinin kimliklerine dokunamaz. Çalışanın girişi ve
  paneli yalnızca iç ağdan. Kayıt sayfası kapalı, girişte OTP zorunlu. İç servisler iki
  Keycloak'ın token'ını da kabul eder; hangisinden geldiğini token'ın içeriği değil onu
  doğrulayan şema söyler. Müşterinin ön API'leri çalışan token'ını kabul ETMEZ.
- Çalışanın yetkisi İZİNLE kontrol edilir (`StaffPermissions`), rol adıyla DEĞİL. Her
  uç kendi iznini ister; müşteri kaydını görüntülemek de bir izin (`customer.view`).
  İzin token'da YOK: her istekte o anki haliyle okunur (bkz. "Personel yönetimi").
  Müşterinin para hareketi başlatan uçları çalışana KAPALI: çalışan müşteri yerine
  işlem başlatmaz, kendi ucundan ve kendi aktörüyle yapar.
- İşyerinin sistem entegrasyonu Keycloak'ta kendi istemcisi (client credentials);
  istemcinin servis hesabı işyeri hesabının kullanıcısı. İşyeri hesabı ön API'den
  AÇILMAZ: kayıt ve entegrasyonun hesaba bağlanması backoffice'in işi.
- Hangi kimliğin hangi hesabın kullanıcısı olduğu wallet'ta (`account_members`), kimlik
  sağlayıcıda DEĞİL. Hesabı açan kimlik hesabın kullanıcısı olur.
- Müşteri yalnızca kullanıcısı olduğu hesaba erişir. Sahibi olunmayan kaynak `404`,
  `403` DEĞİL: başkasının cüzdanının var olduğu da dışarı verilmez.
- Sahiplik wallet-api'nin uçlarında kontrol edilir, transfer ve promo çekirdeğinde
  DEĞİL: kural çağırana göre değişiyor (çalışan müşterinin hesabında işlem yapabilir).
- Müşteri çekiminde isteyen kimlik (`sub`) saga'ya yazılır ve düşme komutunun
  aktöründe wallet'a gider; wallet ledger'a yazmadan önce üyeliği doğrular.
  Orchestrator hesabın kullanıcılarını bilmez.

**Personel yönetimi**
- İzinler KODDA ve sabit; yeni bir yetki türü yeni bir izin ve kod değişikliği demek.
  Roller panelde tanımlanır: rol bir izin seti. Çalışan yetkiyi YALNIZCA rolden alır, ona
  doğrudan izin verilmez. Grup YOK: rolün kendisi aynı işi yapanları topluyor.
- Roller, çalışanlar ve atamalar `staff-admin`'in VERİTABANINDA. Keycloak'ta yalnızca
  kullanıcı, parola, OTP ve oturum; rol ve izin Keycloak'ta TUTULMAZ, token izin TAŞIMAZ.
  Keycloak'ın konsolu personel işi için KULLANILMAZ; konsolda açılan kullanıcının izni yok.
- Çalışanın izni HER İSTEKTE o anki haliyle okunur, önbellek YOK: rolü alınan ya da
  kapatılan çalışanın aynı token'la gelen bir sonraki isteği reddedilir. `wallet-api` ve
  orchestrator `staff-admin`'in `GET /v1/me` ucuna çalışanın KENDİ token'ıyla sorar;
  başkasının izni sorulamaz. Cevap alınamazsa çalışanın isteği `503`; izni doğrulanamayan
  çalışan işlem YAPAMAZ. Kontrol politikanın handler'ında, uçlarda değil.
- `staff-admin` yalnızca çalışanların Keycloak'ının token'ını tanır (müşteri token'ı
  `401`); `GET /v1/me` dışındaki her ucu `staff.manage` ister. Yönetici rolünü ve ilk
  yöneticiyi açılışta kendisi kurar; kimsede `staff.manage` yoksa ayardaki adrese davet
  gönderir.
- Çalışan kendine yetki VEREMEZ: kendi rollerini değiştiremez, sahip olduğu rolün
  izinlerini değiştiremez ve silemez, kendini kapatamaz.
- Yeni çalışan davetle gelir; parolasını ve OTP'sini davetteki bağlantıdan kendisi kurar.
  Panel parolayı HİÇ görmez. Davette Keycloak'taki kullanıcı ve e-posta kayıttan ÖNCE,
  kapatmada kayıt Keycloak'tan ÖNCE: yarım kalan iş izni olmayan tarafta kalır.
- Her değişiklik işi yapan çalışanla, değişiklikle AYNI transaction'da kayda yazılır
  (`staff_audit_events`); kayıt değişmez ve silinmez (REVOKE).

**Kayıt ve doğrulama**
- Kayıt `onboarding`'de: e-posta kodu, parola, Keycloak'ta kullanıcı, wallet'ta hesap.
  Keycloak'ta kendi kendine kayıt KAPALI; müşterinin kullanıcısını onboarding kendi
  istemcisinin servis hesabıyla yönetim API'sinden açar. Parola bizim kodumuzda
  SAKLANMAZ, yalnızca kullanıcıyı açan istekte geçer.
- Giriş Keycloak'ın sayfasında (HiWallet teması). Parolayla token isteği (password
  grant) YOK: RFC 9700 yasaklıyor ve OTP, parola sıfırlama, hesap kilidi gibi adımları
  bize bırakırdı.
- E-posta kodu doğrulanmadan kullanıcı AÇILMAZ; parola ancak ondan sonra sorulur. Adresin
  kayıtlı olduğu kod doğrulanmadan SÖYLENMEZ: başkasının e-postasının müşteri olup
  olmadığını verirdi.
- Doğrulama kodu (e-posta, SMS) düz SAKLANMAZ; on dakika geçerli, beş yanlış denemede
  kilitlenir.
- Kişisel veri (e-posta, telefon, TCKN, doğum tarihi, onaylar) onboarding'in KENDİ
  Postgres sunucusunda; ledger'la aynı yerde DURMAZ. Wallet yalnızca doğrulamanın
  sonucunu (seviye) bilir. Onaylar değişmez ve silinmez (REVOKE).
- TCKN ve telefon sınırda doğrulanıp tipe dönüşür (`NationalId`, `PhoneNumber`), akışta
  string dolaşmaz, maskeli görünür. Bir TCKN tek müşteride, bir telefon numarası da.
- Temel doğrulamadan sonra numara YALNIZCA telefon değiştirme akışından değişir: son on
  dakikada parolayla giriş (`auth_time`), kod yeni numaraya; eski numaraya kod GİTMEZ,
  değişiklikten sonra eski numaraya ve e-postaya haber gider. Değişince bankaya çekim bir
  süre KAPANIR (transfer ve ödeme açık). Önce wallet'ta kısıt, sonra numara: yarıda kalan
  iş kısıtlı tarafta kalır.
- Bireysel hesabı YALNIZCA onboarding açar (`POST /v1/person-accounts`, token'ın
  `azp`'si onboarding'in istemcisi); müşteri açamaz, açabilseydi doğrulamayı atlardı.
  Kimlik başına tek bireysel hesap (`accounts.holder` UNIQUE), açılış tekrar edilebilir.
- Seviyeler `Unknown = 10`, `Unverified = 20`, `Verified = 30`, `Contracted = 40`.
  Seviye yalnızca YÜKSELİR; yükselten yollar birbirinden habersiz, satır kilitlenerek
  yazılır. İşyeri hesabının seviyesi yok.
- Seviye limitleri AYLIK ve hareket tipine göre (gelen transfer, giden transfer, ödeme,
  çekim, yükleme), günlük tarifenin ÜSTÜNE. Hesabın kendi cüzdanları arası sayılmaz. Alıcının
  limiti de kontrol edilir; hata alıcının hesabını söylemez. Tarifede olmayan satır
  KAPALI sayılır ve eksik tarifeyle uygulama AÇILMAZ.
- Kimliği tespit edilmemiş seviyede (`Unknown`, `Unverified`) ayın toplam girişi ve bakiye
  yasal tavanın altında (MASAK Genel Tebliği Sıra No 5, 2.2.11): yükleme ve gelen transfer
  TEK aylık toplamı paylaşır (`IncomingTotal`), bakiye tavanı her gelen harekette kontrol
  edilir. Tavanı eksik tespit edilmemiş seviyeyle uygulama AÇILMAZ.
- `Verified` uzaktan kimlik tespiti (kimlik kartının çipi, canlılık, yüz karşılaştırması),
  `Contracted` kimliği bir çalışanın doğruladığı ve sözleşmesi kurulmuş müşteri. İkisinin
  tutarları risk kararı. Kendi hesabından gelen havale kimlik tespiti DEĞİL, seviye
  değiştirmez.

**Kartla yükleme**
- Üç deployable: `card-topup` yüklemenin ömrünü tutar (iç servis, kendi veritabanı
  `hiwallet_card_topup`, TEK rol); `topup-webhook` sağlayıcının bildirimini alır; parayı
  `wallet-consumer` yazar. Sağlayıcıyı yalnızca card-topup çağırır, wallet-api ÇAĞIRMAZ.
- Kart limitte öncelikli: ödeme açılmadan ÖNCE wallet'tan limit payı alınır
  (`POST /v1/card-topup-holds`, müşterinin token'ıyla). Limit yetmiyorsa `422`
  (`card_topup_limit`) ve ödeme HİÇ açılmaz, kart çekilmez. Açık pay, hesaba gelen her
  paranın (havale, transfer, başka kart yüklemesi) limit hesabına girer; ödenen yükleme
  cüzdana yazılırken limit YENİDEN sorulmaz.
- Pay SAATLE DÜŞMEZ: yalnızca ödeme kapanınca (ödendi ya da ödenmedi) kapanır. Süresi dolan
  ödemeyi sağlayıcı bildirmez; card-topup'ın taraması sağlayıcıya sorar. Tarama
  KAPATILAMAZ, yalnızca aralığı ayarlanır: koşmasa her terk edilen ödeme sayfası payı
  kalıcı tutardı.
- Pay ve kapanışı wallet'ta insert-only (`card_topup_holds`, `card_topup_hold_closures`,
  UPDATE/DELETE REVOKE). Açık pay = kapanışı olmayan pay. Kapanış satırı idempotency kapısı,
  ledger ile AYNI transaction'da.
- Hesaba gelen paranın limit kararları hesap satırı kilitlenerek (`FOR UPDATE`) sırayla
  verilir: pay, havale ve transferin alıcı tarafı.
- Yüklemenin kimliği üç yerde aynı: card-topup'ın kaydı, wallet'taki pay ve sağlayıcıdaki
  ödemenin referansı. Başlatma üç adım, her biri ayrı commit ve tekrar edilebilir: kayıt,
  pay, ödeme. Aynı `Idempotency-Key` ile tekrar eden istek yarım kalan adımı tamamlar.
- Durumlar `created → pending → paid | failed`; wallet payı vermezse `rejected`. Geçişler
  saga'daki gibi `Applied` / `Ignored` / `Conflict`; `Conflict`'te durum değişmez, alarm.
- Kapanış (`CardTopupClosed`) outbox'la, geçişle AYNI transaction'da. Yükleme kapandıktan
  sonra yazılan pay (geç cevap) ödenmedi kapanışıyla serbest bırakılır.
- Ledger: ödenen yükleme `topup`, cüzdan +, sağlayıcının clearing'i −, kova sağlayıcınınki
  (`card`). Aktör `customer` (hesap). Anahtar `provider:card_topup_id` (`decisions.md`
  madde 27).
- Ödenmedi diye kapanmış yüklemenin parası gelirse ya da tersi, wallet YAZMAZ: dead-letter
  ve ALARM. Tarifesi olmayan sağlayıcı geçici hata (requeue): kendi konfigürasyon
  eksiğimiz yüzünden müşterinin parası dead-letter'a gitmez.
- Dönüş adresine `cardTopupId` eklenir. BFF dönüş adresini kendi adresinden kurar,
  tarayıcıdan ALMAZ.

**topup-webhook**
- AYRI servis, AYRI veritabanı (`hiwallet_topup`), TEK rol — append-only zorlanacak
  tablosu yok.
- İmza: HAM gövde baytları üzerinde HMAC-SHA256, sabit zamanlı karşılaştırma.
  Gövde parse EDİLMEDEN önce doğrulanır. Geçersiz imza, eksik başlık ve tanınmayan
  sağlayıcı → `401`. Tanınmayan sağlayıcıya `404` DÖNÜLMEZ.
- Response `202 Accepted`, `200` DEĞİL: verilen söz "işledim" değil "kalıcı kaydettim"
  (`decisions.md` madde 29). Ve ancak inbox commit'inden SONRA. Tekrar eden event de
  `202` — sağlayıcı için yeniden gönderim başarılı sonuçtur, ayrım gövdedeki
  `duplicate` alanında.
- İki kademe idempotency: inbox `(provider, event_id)` UNIQUE + card-topup'ta yüklemenin
  durumu (aynı sonucun ikinci bildirimi `Ignored`).
- Bildirim card-topup'a gider (`hiwallet.card-payments`), wallet'a DEĞİL. Tek kuyruk,
  `x-single-active-consumer`, tüketicide `prefetch=1`. Bizim açmadığımız ya da kaydımızla
  uyuşmayan ödemenin bildirimi dead-letter; geçici hata (DB kapalı) requeue. İkisi
  karıştırılmaz: kalıcı hatayı requeue etmek kuyruğu süresiz tıkar.
- Relay: `FOR UPDATE SKIP LOCKED` + publisher confirms. Önce publish, sonra işaretle —
  ters sıra kayıp üretir.
- Relay TEK instance: tur `pg_try_advisory_lock` ile korunur (`decisions.md` madde 30).
  İki relay ayrı batch'leri farklı hızda yayınlarsa aynı ödemenin bildirimleri exchange'e
  ters sırada varır ve kuyruk içi sıra garantisi bunu düzeltmez. `SKIP LOCKED` yine de
  kalır: biri sıra için, öbürü çift yayın için. Withdrawal ve card-topup outbox relay'leri
  kilitlenmez — bir saga'nın aynı anda birden fazla bekleyen komutu, bir yüklemenin birden
  fazla kapanışı olamaz.

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
- Tutarı inceleme eşiğinin ÜSTÜNDEKİ çekim düşüldükten sonra bankaya GİTMEZ
  (`under_review`): `withdrawal.review` izni olan bir çalışan serbest bırakır ya da iptal eder.
  Kararı veren saga'ya yazılır; iptalde ters kaydın aktörü o çalışan. İptal banka
  reddinden AYRI bir durumda biter (`cancelled`, `failed` değil). Eşik bölümü
  (`Withdrawals:Review`) eksikse orchestrator AÇILMAZ.
- İncelemedeki saga takılmış SAYILMAZ: sistemi değil bir insanı bekliyor.
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
  `BankTransferSucceeded`, `BankTransferFailed`, `BankDepositReceived`.

**Havale ile yükleme**
- Toplama hesabı: bütün müşteriler aynı IBAN'a gönderir, açıklamadaki hesap numarası paranın
  kime ait olduğunu söyler. Banka parayı açıklamaya bakmadan kabul eder; bildirim geldiğinde
  para ZATEN bankamızda. Wallet havaleyi reddetmez, nereye yazacağına karar verir: cüzdan ya
  da askı. Hiç yazmamak ledger'ı bankadan ayırır.
- Cüzdana yalnızca şu havale geçer: açıklamada tek bir geçerli hesap numarası, hesap
  bireysel, bu para biriminde varsayılan cüzdanı var, gönderenin kimlik numarası hesap
  sahibininki ve seviyenin limiti yetiyor. Geri kalan her şey askıya (`suspense`, banka
  bazında); sebep `suspended_deposits`'te. Askı cüzdan gibi negatife DÜŞEMEZ.
- Kimlik numarası wallet'ta TUTULMAZ: onboarding'e evet/hayır sorulur
  (`POST /v1/holder-checks`, yalnızca wallet-consumer'ın istemcisi, numara gövdede). Cevap
  gelmezse havale kuyruğa döner; tahminle ne cüzdana ne askıya yazılır.
- Gönderenin adı, IBAN'ı ve kimlik numarası banka entegrasyonunun veritabanında
  (`bank_deposits`). Wallet'a giden mesajda ad ve IBAN YOK.
- Ledger: cüzdana geçen havale `topup`, cüzdan +, nostro − (clearing'e UĞRAMAZ: havalenin
  settlement'ı yok). Askıya alınan `suspended_deposit`, askı +, nostro −. Aktör cüzdana
  geçende hesap (`customer`: havaleyi müşteri başlattı, kimlik numarası doğruladı), askıda
  `system`.
- Bildirim asıl yol, hesap hareketi taraması kontrol; tarama KAPATILAMAZ. Aynı havale iki
  yoldan da gelir: `bank_deposits (provider, bank_reference)` UNIQUE, wallet'ta
  `processed_events` ve ledger anahtarı `provider:bank_reference`.
- Ledger'a hiç yazılamayan havale (bankanın o para biriminde sistem hesabı yok, tutar
  bozuk) dead-letter ve ALARM: para bankamızda, ledger'da yok.

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