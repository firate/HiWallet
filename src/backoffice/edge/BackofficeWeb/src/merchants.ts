import { api, ApiError } from './api'

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/** On hane; gruplama boşlukları atılıyor. Kontrol hanesine wallet-api bakıyor. */
const accountNumber = /^[0-9]{10}$/

/**
 * Satır başına bir işyeri hesabı: hesap numarası (gruplu ya da bitişik) ya da kimlik.
 * Çalışan işyerini numarasıyla biliyor; numara wallet-api'ye sorularak kimliğe çevriliyor
 * ve istek kimlikle gidiyor. Aynı işyeri bir kez. Satır ikisine de benzemiyorsa ya da
 * numaranın hesabı yoksa hangi satır olduğu söyleniyor.
 */
export async function merchantIds(text: string): Promise<string[]> {
  const lines = text
    .split(/[\n,]/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0)

  const ids = await Promise.all(lines.map(resolve))

  return [...new Set(ids.map((id) => id.toLowerCase()))]
}

async function resolve(line: string): Promise<string> {
  if (guid.test(line)) {
    return line
  }

  const digits = line.replaceAll(' ', '')

  if (!accountNumber.test(digits)) {
    throw new Error(`"${line}" ne hesap numarası ne kimlik.`)
  }

  try {
    return (await api.accountByNumber(digits)).accountId
  } catch (error) {
    // 400 kontrol hanesi tutmayan numara, 404 numaranın hesabı yok.
    if (error instanceof ApiError && (error.status === 400 || error.status === 404)) {
      throw new Error(`${line}: bu numarada hesap yok.`)
    }

    throw error
  }
}
