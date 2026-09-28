# Kapının yapılandırması imajın içinde: Caddyfile değişince `docker compose up --build`
# imajı yeniden kuruyor ve kapı yeni dosyayla kalkıyor. Dosya bind mount'la bağlıyken
# değişiklik kapıya geçmiyordu: git dosyayı yeniden yazınca konteyner eskisini görmeye
# devam ediyor, Caddy de dosyayı kendiliğinden yeniden okumuyor.
FROM caddy:2.11.4-alpine
COPY Caddyfile /etc/caddy/Caddyfile
