#!/bin/bash
# Çalışanların realm'inde girişte tek kullanımlık kodu (TOTP) zorunlu kılar.
#
# Realm dosyadan içe aktarıldıktan SONRA koşuyor. Giriş akışı ya da zorunlu işlemler
# realm dosyasına yazılsaydı Keycloak'ın varsayılanlarının YERİNE geçerdi: listede
# olmayan her şey eksik kalır ve Keycloak'ın yeni sürümüyle gelenler hiç eklenmezdi.
# Burada varsayılanlar korunuyor, yalnızca iki ayar değişiyor:
#   - Giriş akışının kopyasında OTP adımı koşullu değil zorunlu: kodu kurmamış
#     kullanıcı girişte kurmak zorunda, kurduktan sonra her girişte soruluyor.
#   - Configure OTP varsayılan işlem: yeni kullanıcıdan ilk girişte isteniyor.
#
# Her açılışta koşuyor ve her seferinde aynı sonucu veriyor.
set -euo pipefail

realm=hiwallet-staff
flow=browser-otp
config=/tmp/kcadm.config

kcadm() { /opt/keycloak/bin/kcadm.sh "$@" --config "$config"; }

kcadm config credentials --server "$KEYCLOAK_URL" --realm master \
  --user "$KEYCLOAK_ADMIN" --password "$KEYCLOAK_ADMIN_PASSWORD"

kcadm update authentication/required-actions/CONFIGURE_TOTP -r "$realm" \
  -s enabled=true -s defaultAction=true

if ! kcadm get authentication/flows -r "$realm" --fields alias --format csv --noquotes | grep -qx "$flow"; then
  kcadm create authentication/flows/browser/copy -r "$realm" -s newName="$flow"
fi

# Kopyadaki "Conditional OTP" alt akışı zorunlu oluyor. Koşul (kullanıcının kodu var
# mı) yalnızca koşullu akışta değerlendiriliyor; zorunlu akışta OTP adımı her zaman
# koşuyor.
while IFS=, read -r id name requirement; do
  if [[ $name == *"Conditional OTP"* && $requirement != "REQUIRED" ]]; then
    kcadm update authentication/flows/"$flow"/executions -r "$realm" \
      -b "{\"id\":\"$id\",\"requirement\":\"REQUIRED\"}"
  fi
done < <(kcadm get authentication/flows/"$flow"/executions -r "$realm" \
  --fields id,displayName,requirement --format csv --noquotes)

kcadm update realms/"$realm" -s browserFlow="$flow"

echo "hiwallet-staff: girişte OTP zorunlu."
