import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { api } from '../api/client'
import type { Table } from '../api/types'
import { useAuth } from '../auth/useAuth'
import { TopBar } from '../components/TopBar'
import { formatMoney } from '../lib/format'

type Tile = { to: string; title: string; subtitle: string; primary?: boolean }

/** Sahip ve yöneticinin PIN'den sonra gördüğü ana menü: büyük düğmelerle bölümler. */
export default function HomeMenuPage() {
  const { user } = useAuth()
  const tables = useQuery({ queryKey: ['tables'], queryFn: () => api<Table[]>('GET', '/tables') })

  const open = tables.data?.filter((t) => t.isActive && t.openSession) ?? []
  const openTotal = open.reduce((sum, t) => sum + (t.openSession?.total ?? 0), 0)

  const work: Tile[] = [
    {
      to: '/garson',
      title: 'Masalar',
      subtitle: tables.isSuccess ? (open.length > 0 ? `${open.length} dolu masa · ${formatMoney(openTotal)}` : 'Tüm masalar boş') : 'Sipariş ve ödeme',
      primary: true,
    },
    { to: '/rapor', title: 'Gün sonu raporu', subtitle: 'Z raporu, tahsilat, iptal ve ikramlar' },
    { to: '/fisler', title: 'Fişler', subtitle: 'Mutfak, hesap ve rapor fişleri' },
    { to: '/soru', title: 'Rapora sor', subtitle: 'Satışlarınıza Türkçe soru sorun (yapay zeka)' },
  ]
  const settings: Tile[] = [
    { to: '/yonetim?sekme=menu', title: 'Menü', subtitle: 'Kategori, ürün ve fiyatlar' },
    { to: '/yonetim?sekme=masalar', title: 'Masa düzeni', subtitle: 'Masa ekle, adlandır, kapat' },
    { to: '/yonetim?sekme=personel', title: 'Personel', subtitle: 'Personel, roller ve PIN\'ler' },
    { to: '/yonetim?sekme=cihazlar', title: 'Cihazlar', subtitle: 'Yeni cihaz bağla, bağlantı kes' },
    { to: '/yonetim?sekme=yedekler', title: 'Yedekler', subtitle: 'Son yedek, şimdi yedek al' },
  ]

  return (
    <div className="min-h-screen bg-stone-100">
      <TopBar title="Ana menü" />
      <main className="mx-auto max-w-4xl space-y-6 p-4">
        <p className="text-lg text-stone-700">Hoş geldiniz, <span className="font-semibold">{user?.displayName}</span></p>

        {tables.isSuccess && tables.data.length === 0 && (
          <section className="rounded-2xl bg-amber-50 p-5 ring-1 ring-amber-200">
            <h2 className="font-semibold text-amber-900">Kuruluma devam edin</h2>
            <ol className="mt-2 list-decimal space-y-1 pl-5 text-sm text-amber-900">
              <li><Link to="/yonetim?sekme=menu" className="font-semibold underline">Menüyü girin</Link>: kategoriler, ürünler ve fiyatlar.</li>
              <li><Link to="/yonetim?sekme=masalar" className="font-semibold underline">Masaları ekleyin</Link>.</li>
              <li><Link to="/yonetim?sekme=personel" className="font-semibold underline">Personeli ekleyin</Link>: her birine bir PIN verin.</li>
              <li>Başka bilgisayar veya tablet varsa <Link to="/yonetim?sekme=cihazlar" className="font-semibold underline">eşleştirme kodu oluşturun</Link>.</li>
            </ol>
          </section>
        )}

        <TileGrid tiles={work} />

        <section>
          <h2 className="mb-2 text-sm font-semibold tracking-wide text-stone-500 uppercase">Ayarlar</h2>
          <TileGrid tiles={settings} />
        </section>
      </main>
    </div>
  )
}

function TileGrid({ tiles }: { tiles: Tile[] }) {
  return (
    <ul className="grid grid-cols-2 gap-3 md:grid-cols-4">
      {tiles.map((t) => (
        <li key={t.to} className={t.primary ? 'col-span-2' : ''}>
          <Link
            to={t.to}
            className={`flex h-32 flex-col justify-between rounded-2xl p-5 shadow-sm transition active:scale-[0.98] ${
              t.primary ? 'bg-amber-700 text-white hover:bg-amber-800' : 'bg-white text-stone-900 ring-1 ring-stone-200 hover:ring-amber-400'
            }`}
          >
            <span className="text-xl font-bold">{t.title}</span>
            <span className={`text-sm ${t.primary ? 'text-amber-100' : 'text-stone-500'}`}>{t.subtitle}</span>
          </Link>
        </li>
      ))}
    </ul>
  )
}
