Her uygulama aşağıdaki 12 zorunlu katmanı içerir. Dominant tema bunun üstüne eklenir.
## Zorunlu Katmanlar

### 1. Configuration & Secrets

- `appsettings.json` + environment variable override.
- Secret koda gömülmez; connection string vb. env'den gelir.
- Startup'ta basit config validation: kritik config eksikse uygulama ayağa kalkmadan patlar (fail fast).
- **Kapsam:** Vault/secret manager yok. `.env` + Docker Compose yeterli. (Üretimde böyle yapılmaz: local'de User Secrets, prod'da Vault/secret manager; `.gitignore` + gitleaks ile sızıntı koruması.)

### 2. Structured Logging

- `ILogger<T>` (Microsoft.Extensions.Logging) + OpenTelemetry Logs. **Serilog yok.**
- Log'lar OTLP ile export edilir; trace_id/span_id log record'una otomatik gömülür (korelasyon bedava).
- Doğru log seviyeleri (Information / Warning / Error).
- **Kapsam:** Makine kanalı OTLP → Collector → Loki. İnsan kanalı local'de ayrı `AddSimpleConsole` (terminalde okunur format).

### 3. Observability — Traces & Metrics

- OpenTelemetry: distributed tracing + temel metrikler (request count, latency, error rate — RED).
- Auto-instrumentation (ASP.NET Core, HttpClient, Npgsql).
- Log–trace korelasyonu: OTel logs sayesinde trace_id otomatik eşleşir; Grafana'da log'dan Tempo trace'ine atlanabilir.
- **Kapsam:** OTLP exporter → OTel Collector → Loki/Tempo/Prometheus → Grafana. Homelab stack'ine bağlanır.

### 4. Health Checks

- `/health/live` (process ayakta mı) ve `/health/ready` (DB/cache/broker hazır mı) ayrımı.
- Readiness gerçek bağımlılıkları kontrol eder.
- **Kapsam:** Built-in `AddHealthChecks()` + ilgili check paketleri yeterli.

### 5. Error Handling

- Global exception handling middleware.
- Tutarlı error response: RFC 7807 ProblemDetails.
- Beklenen hatalar (validation, not found) ile beklenmeyen (unhandled) ayrımı; internal detay client'a sızmaz.
- **Kapsam:** Birkaç custom exception tipi + tek merkezi handler yeterli.

### 6. Input Validation

- Sınırda doğrulama (FluentValidation).
- Geçersiz girdi domain'e ulaşmadan reddedilir, anlamlı mesaj döner.
- **Kapsam:** Sadece kullanılan endpoint'lerin DTO'ları için.

### 7. Rate Limiting

- ASP.NET Core built-in rate limiting middleware (fixed/sliding window veya token bucket).
- Limit aşımında `429` + `Retry-After`.
- **Kapsam:** In-memory limiter yeterli. (Dağıtık/çok-instance senaryoda Redis tabanlı limiter gerekir; bu uygulamalarda şart değil.)

### 8. Data Layer Discipline

- Migration ile şema versiyonlama (EF Core migrations).
- Connection pooling (Npgsql default).
- Net transaction sınırları.
- Concurrency stratejisi açık (örn. optimistic lock — dominant temaysa).
- Unbounded query yok; liste dönen endpoint'lerde pagination.
- **Kapsam:** Repository soyutlaması değer katıyorsa eklenir, yoksa doğrudan DbContext.

### 9. API Contract

- Versiyonlama: `/v1` prefix.
- Liste endpoint'lerinde pagination.
- OpenAPI/Swagger şeması (geliştirmede açık).
- Tutarlı response yapısı (zarf var ya da bilinçli olarak yok — ama tutarlı).
- **Kapsam:** Swagger UI dev ortamda açık yeterli.

### 10. Graceful Shutdown

- SIGTERM'de yeni istek alımı durur, in-flight işler biter.
- Background worker'lar (varsa) temiz kapanır (`IHostedService` + `CancellationToken`).
- **Kapsam:** .NET host'un default graceful shutdown davranışı + worker'larda token'a uyum yeterli.

### 11. Resilience (timeout / retry / circuit breaker)

- Dış bağımlılıklar için Polly: timeout, retry (yalnız idempotent çağrılarda), circuit breaker.
- Bir bağımlılığın çökmesi tüm servisi kilitlemez.
- **Kapsam:** `Microsoft.Extensions.Http.Resilience` (Polly v8) ile standart resilience handler yeterli. Dış bağımlılık yoksa bile DB/cache çağrılarında timeout uygulanır.

### 12. Idempotency & Consistency

- Yazma işlemlerinde idempotency key; at-least-once mesajlaşmada dedup; gerektiğinde Outbox.
- Aynı isteğin/aynı mesajın iki kez işlenmesi engellenir.
- **Kapsam:** Idempotency key store için Redis ya da tek tablo yeterli. Outbox sadece async yayın yapanlarda devreye girer.
- **Not:** Bu katman bazılarının _dominant teması_ olduğunda baseline'dan çıkıp ana konu olur (ör. Wallet — Distributed: idempotency + Outbox + saga consistency).

## Opsiyonel Katman

### A. Authentication & Authorization (katman olarak)

- Authn (kim) ve authz (ne yapabilir) ayrımı, token validation.
- **Neden opsiyonel:** Bir uygulamanın dominant teması güvenlik değilse, endpoint'leri açık bırakıp odakta kalmak odakta kalmak için daha temiz. İhtiyaç olursa diğerlerine eklenir.
- **Eklenirse:** JWT bearer + policy yeterli; token üretimi için merkezi IdP (Keycloak) ya da Auth uygulaması kullanılır.
- **Not:** Bu katmanın kendisi (JWT/RS256, permission-based authz, multi-tenancy, revocation) **Auth uygulamasının** dominant temasıdır; orada baseline'dan çıkıp ana konu olur.

## Genel Çıkış Kriteri (her uygulama)

- Zorunlu katmanların hepsi görünür şekilde var (gerçekten çalışıyor, mock değil).
- Dominant tema en az bir senaryoda gösterilebiliyor.
- `docker compose up` ile ayağa kalkıyor.
- Trace bir uçtan diğerine takip edilebiliyor.
- README'de: ne gösteriyor, nasıl çalıştırılır, hangi senaryo nasıl tetiklenir.