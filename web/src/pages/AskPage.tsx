import { useState, type FormEvent } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { AiResult } from '../api/types'
import { AiText } from '../components/AiText'
import { HomeBackLink } from '../components/BackLink'
import { TopBar } from '../components/TopBar'

const EXAMPLES = [
  'Bu hafta ciro geçen haftaya göre nasıl?',
  'Geçen ay en çok satan 5 ürün hangisi?',
  'En yoğun saatlerimiz hangileri?',
  'Bu ay kim ne kadar iptal yaptı?',
]

type Exchange = { question: string; answer: AiResult }

/**
 * Rapora soru sorma: işletme sahibi Türkçe sorar, yapay zeka satış verilerinden cevaplar.
 * Sorular ve cevaplar yalnızca bu ekranda tutulur; sayfadan çıkınca silinir.
 */
export default function AskPage() {
  const status = useQuery({ queryKey: ['ai-status'], queryFn: () => api<{ configured: boolean }>('GET', '/ai/status') })
  const [question, setQuestion] = useState('')
  const [history, setHistory] = useState<Exchange[]>([])

  const ask = useMutation({
    mutationFn: (q: string) => api<AiResult>('POST', '/ai/ask', { question: q }),
    onSuccess: (answer, q) => setHistory((h) => [{ question: q, answer }, ...h]),
    onError: (err, q) => setHistory((h) => [{ question: q, answer: { available: false, text: err.message } }, ...h]),
  })

  function submit(e: FormEvent) {
    e.preventDefault()
    const q = question.trim()
    if (!q) return
    ask.mutate(q)
    setQuestion('')
  }

  return (
    <div className="min-h-screen bg-stone-100">
      <TopBar title="Rapora sor" left={<HomeBackLink currentPath="/soru" />} />
      <main className="mx-auto max-w-3xl space-y-4 p-4">
        {status.data && !status.data.configured && (
          <p className="rounded-xl bg-amber-50 px-4 py-3 text-sm text-amber-900 ring-1 ring-amber-200">
            Yapay zeka henüz ayarlanmamış (API anahtarı gerekli). Kurulumu yapan kişiye başvurun.
          </p>
        )}

        <form onSubmit={submit} className="rounded-2xl bg-white p-4 shadow-sm ring-1 ring-stone-200">
          <label htmlFor="question" className="text-sm font-medium text-stone-700">Satışlarınızla ilgili bir soru sorun</label>
          <div className="mt-2 flex gap-2">
            <input
              id="question"
              value={question}
              onChange={(e) => setQuestion(e.target.value)}
              maxLength={500}
              placeholder="ör. Geçen hafta en çok ne sattık?"
              className="flex-1 rounded-xl border border-stone-300 px-4 py-3 focus:border-violet-600 focus:ring-2 focus:ring-violet-200 focus:outline-none"
            />
            <button
              type="submit"
              disabled={!question.trim() || ask.isPending}
              className="rounded-xl bg-violet-700 px-5 font-semibold text-white transition hover:bg-violet-800 disabled:opacity-40"
            >
              Sor
            </button>
          </div>
          <div className="mt-3 flex flex-wrap gap-2">
            {EXAMPLES.map((e) => (
              <button
                key={e}
                type="button"
                disabled={ask.isPending}
                onClick={() => ask.mutate(e)}
                className="rounded-full bg-stone-100 px-3 py-1.5 text-sm text-stone-700 hover:bg-violet-50 hover:text-violet-800 disabled:opacity-40"
              >
                {e}
              </button>
            ))}
          </div>
        </form>

        {ask.isPending && <p className="px-1 text-sm text-stone-500">Veriler inceleniyor… (birkaç saniye sürebilir)</p>}

        {history.map((x, i) => (
          <article key={history.length - i} className="rounded-2xl bg-white p-4 shadow-sm ring-1 ring-stone-200">
            <p className="font-semibold text-stone-900">{x.question}</p>
            <div className="mt-2">
              <AiText result={x.answer} />
            </div>
          </article>
        ))}

        <p className="px-1 text-xs text-stone-500">
          Cevaplar yapay zeka tarafından satış kayıtlarınızdan hazırlanır. Önemli kararlar için rakamları Gün sonu raporundan da kontrol edin.
        </p>
      </main>
    </div>
  )
}
