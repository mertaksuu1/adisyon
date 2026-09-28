import { useSearchParams } from 'react-router'
import { HomeBackLink } from '../components/BackLink'
import { TopBar } from '../components/TopBar'
import { DevicesTab } from './admin/DevicesTab'
import { MenuTab } from './admin/MenuTab'
import { StaffTab } from './admin/StaffTab'
import { TablesTab } from './admin/TablesTab'

const tabs = [
  { key: 'menu', label: 'Menü', Component: MenuTab },
  { key: 'masalar', label: 'Masalar', Component: TablesTab },
  { key: 'personel', label: 'Personel', Component: StaffTab },
  { key: 'cihazlar', label: 'Cihazlar', Component: DevicesTab },
] as const

/** Yönetim: menü, masalar, personel ve cihazlar. Yalnızca işletme sahibi ve yönetici. */
export default function AdminPage() {
  // Seçili sekme adreste tutulur (/yonetim?sekme=personel): sayfa yenilenince aynı sekmede kalınır.
  const [params, setParams] = useSearchParams()
  const current = tabs.find((t) => t.key === params.get('sekme')) ?? tabs[0]

  return (
    <div className="min-h-screen bg-stone-100">
      <TopBar
        title="Yönetim"
        left={<HomeBackLink currentPath="/yonetim" />}
      />
      <main className="mx-auto max-w-4xl p-4">
        <nav className="mb-4 flex gap-1 rounded-xl bg-white p-1 shadow-sm ring-1 ring-stone-200" aria-label="Yönetim bölümleri">
          {tabs.map((t) => (
            <button
              key={t.key}
              type="button"
              onClick={() => setParams({ sekme: t.key })}
              aria-current={t.key === current.key ? 'page' : undefined}
              className={`flex-1 rounded-lg py-2 text-sm font-semibold transition ${
                t.key === current.key ? 'bg-stone-900 text-white' : 'text-stone-600 hover:bg-stone-100'
              }`}
            >
              {t.label}
            </button>
          ))}
        </nav>
        <current.Component />
      </main>
    </div>
  )
}
