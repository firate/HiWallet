import { useState } from 'react'

/** Değeri panoya kopyalıyor; banka uygulamasına elle yazmak hata getirir. */
export function CopyButton({ text, label = 'Kopyala' }: { text: string; label?: string }) {
  const [copied, setCopied] = useState(false)

  async function copy() {
    await navigator.clipboard.writeText(text)
    setCopied(true)
  }

  return (
    <button type="button" className="link" aria-label={label} onClick={copy}>
      {copied ? 'Kopyalandı' : 'Kopyala'}
    </button>
  )
}
