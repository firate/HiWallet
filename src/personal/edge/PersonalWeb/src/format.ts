const dateFormat = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })

export function money(amount: number, currency: string): string {
  return new Intl.NumberFormat('tr-TR', { style: 'currency', currency }).format(amount)
}

export function date(value: string): string {
  return dateFormat.format(new Date(value))
}

/** Hesap numarası okunsun diye üçlü gruplar halinde: 123 456 7890. */
export function accountNumber(value: string): string {
  return `${value.slice(0, 3)} ${value.slice(3, 6)} ${value.slice(6)}`
}

/** IBAN dörtlü gruplar halinde, bankaların yazdığı gibi: TR28 0009 9000 .... */
export function iban(value: string): string {
  return value.replace(/(.{4})/g, '$1 ').trim()
}

const fundTypes: Record<string, string> = {
  cash: 'Nakit',
  card: 'Kart',
  promo: 'Promo',
}

export function fundType(value: string): string {
  return fundTypes[value] ?? value
}

const movementTypes: Record<string, string> = {
  p2p: 'Transfer',
  p2b: 'İşyerine transfer',
  b2p: 'İşyerinden gelen',
  b2b: 'İşyerleri arası',
  payment: 'Ödeme',
  topup: 'Para yükleme',
  withdrawal: 'Para çekme',
  refund: 'İade',
  promo_grant: 'Promo yüklemesi',
  promo_expiry: 'Promo süresi doldu',
}

export function movementType(value: string): string {
  return movementTypes[value] ?? value
}

const kycLevels: Record<string, string> = {
  Unknown: 'Doğrulanmadı',
  Unverified: 'Temel doğrulama',
  Verified: 'Doğrulanmış',
  Contracted: 'Sözleşmeli',
}

export function kycLevel(value: string): string {
  return kycLevels[value] ?? value
}

const kycMovements: Record<string, string> = {
  OutgoingTransfer: 'Başka birine gönderim',
  Payment: 'İşyerine ödeme',
  Withdrawal: 'Banka hesabına çekim',
  IncomingTransfer: 'Gelen transfer',
  Deposit: 'Para yükleme',
  IncomingTotal: 'Toplam giriş (yükleme ve gelen transfer)',
}

export function kycMovement(value: string): string {
  return kycMovements[value] ?? value
}

const withdrawalStates: Record<string, string> = {
  initiated: 'Alındı',
  rejected: 'Reddedildi',
  debited: 'Cüzdandan düşüldü',
  bank_transfer_pending: 'Bankada',
  settling: 'Gönderildi',
  completed: 'Gönderildi',
  compensating: 'İade ediliyor',
  failed: 'Gönderilemedi, iade edildi',
  under_review: 'İncelemede',
  cancelling: 'İptal ediliyor',
  cancelled: 'İptal edildi, iade edildi',
}

export function withdrawalState(value: string): string {
  return withdrawalStates[value] ?? value
}

/**
 * Çekim neden yapılmadı ya da geri döndü. Anahtar sunucunun kural adı; sunucunun sebep metni
 * destek için yazıldı ve hesap kimliği taşıyabiliyor, müşteriye gösterilmiyor.
 */
const withdrawalFailures: Record<string, string> = {
  insufficient_funds: 'Çekilebilir bakiyen bu tutara ve komisyonuna yetmedi.',
  'Withdrawal.PerTransaction': 'Tutar tek seferde çekilebilecek tutarı aşıyor.',
  'Withdrawal.Daily': 'Bugünkü çekim limitin bu tutara yetmedi.',
  'Kyc.Withdrawal.Monthly': 'Doğrulama seviyenin aylık çekim limiti bu tutara izin vermedi.',
  withdrawal_hold: 'Telefon numaran yakın zamanda değiştiği için banka hesabına çekim bir süre kapalı.',
  bank_rejected: 'Banka transferi kabul etmedi; para komisyonuyla birlikte cüzdanına geri döndü.',
  review_cancelled: 'Çekim incelemede iptal edildi; para komisyonuyla birlikte cüzdanına geri döndü.',
}

export function withdrawalFailure(rule: string | null | undefined): string {
  return (rule && withdrawalFailures[rule]) || 'Çekim yapılamadı.'
}

/** Sonucu artık değişmeyen durumlar; izleme burada duruyor. */
export function isFinalWithdrawalState(value: string): boolean {
  return value === 'rejected' || value === 'completed' || value === 'failed' || value === 'cancelled'
}

const cardTopupStates: Record<string, string> = {
  created: 'Başlatıldı',
  pending: 'Ödeme bekleniyor',
  paid: 'Ödendi',
  failed: 'Ödenmedi',
  rejected: 'Reddedildi',
}

export function cardTopupState(value: string): string {
  return cardTopupStates[value] ?? value
}

/** Sonucu artık değişmeyen durumlar; izleme burada duruyor. */
export function isFinalCardTopupState(value: string): boolean {
  return value === 'paid' || value === 'failed' || value === 'rejected'
}

/**
 * Ödenmeden kapanan yüklemenin sebebi. Reddedilen yüklemede sebep wallet'ın kural adı;
 * limit dışındakiler müşterinin düzeltebileceği bir şey söylemiyor.
 */
const cardTopupFailures: Record<string, string> = {
  canceled: 'Ödeme tamamlanmadı: vazgeçildi ya da kart reddedildi.',
  expired: 'Ödeme süresi içinde yapılmadı.',
  abandoned: 'Ödeme başlatılamadı.',
  payment_not_opened: 'Ödeme başlatılamadı.',
  provider_rejected: 'Kart sağlayıcısı ödemeyi başlatmadı.',
  card_topup_limit: 'Bu yükleme doğrulama seviyenin limitine sığmıyor.',
}

export function cardTopupFailure(value: string): string {
  return cardTopupFailures[value] ?? 'Yükleme yapılamadı.'
}
