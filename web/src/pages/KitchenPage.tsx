import { TopBar } from '../components/TopBar'

/** Mutfak ekranı. Faz 2b'de SignalR ile gerçek zamanlı sipariş kartları gelecek. */
export default function KitchenPage() {
  return (
    <div className="min-h-screen bg-stone-100">
      <TopBar title="Mutfak" />
      <main className="p-8 text-center text-stone-500">Mutfak ekranı bir sonraki adımda (Faz 2b) gelecek.</main>
    </div>
  )
}
