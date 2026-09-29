import type { AiResult } from '../api/types'

/**
 * Yapay zeka cevabını gösterir. "- " ile başlayan satırlar madde olarak, diğerleri paragraf olarak çizilir.
 * Kullanılamıyorsa (anahtar yok, internet yok) nedeni sade bir notla gösterilir.
 */
export function AiText({ result }: { result: AiResult }) {
  if (!result.available) {
    return <p className="rounded-lg bg-stone-100 px-3 py-2 text-sm text-stone-600">{result.text}</p>
  }

  const lines = result.text.split('\n').map((l) => l.trim()).filter(Boolean)
  const bullets = lines.filter((l) => l.startsWith('- '))
  const paragraphs = lines.filter((l) => !l.startsWith('- '))

  return (
    <div className="space-y-2 text-stone-800">
      {paragraphs.map((p, i) => <p key={i}>{p}</p>)}
      {bullets.length > 0 && (
        <ul className="list-disc space-y-1 pl-5">
          {bullets.map((b, i) => <li key={i}>{b.slice(2)}</li>)}
        </ul>
      )}
    </div>
  )
}
