import { describe, expect, it } from 'vitest'
import { merchantIds } from './merchants'
import { fakeBff } from './test/fakeBff'

const shop = '0198a0c4-0000-7000-8000-000000000001'
const other = '0198a0c4-0000-7000-8000-000000000002'

describe('merchantIds', () => {
  /** Çalışan işyerini hesap numarasıyla biliyor; istek kimlikle gidiyor. */
  it('numarayı kimliğe çeviriyor, kimliği olduğu gibi bırakıyor', async () => {
    fakeBff({
      'GET /v1/accounts/by-number/1234567897': { status: 200, body: { accountId: shop } },
    })

    expect(await merchantIds(`123 456 7897\n${other}\n`)).toEqual([shop, other])
  })

  it('aynı işyeri iki kez yazılırsa bir kez gönderiyor', async () => {
    fakeBff({
      'GET /v1/accounts/by-number/1234567897': { status: 200, body: { accountId: shop } },
    })

    expect(await merchantIds(`1234567897\n${shop}`)).toEqual([shop])
  })

  it('hesabı olmayan numaranın satırını söylüyor', async () => {
    fakeBff({
      'GET /v1/accounts/by-number/1234567897': { status: 404, body: { title: 'Hesap bulunamadı' } },
    })

    await expect(merchantIds('123 456 7897')).rejects.toThrow('123 456 7897: bu numarada hesap yok.')
  })

  it('ne numara ne kimlik olan satırı söylüyor', async () => {
    fakeBff({})

    await expect(merchantIds('Kahve Dükkanı')).rejects.toThrow('"Kahve Dükkanı" ne hesap numarası ne kimlik.')
  })
})
