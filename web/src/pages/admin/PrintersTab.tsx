import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { PrinterSettings, PrintResult } from '../../api/types'
import { describePrint } from '../../lib/print'
import { Button, Card, Input, Message } from './ui'

/**
 * Yazıcılar: ağ (Ethernet/Wi-Fi) fiş yazıcılarının adresi. Mutfak fişleri mutfak yazıcısından; hesap fişi ve
 * Z raporu kasa yazıcısından çıkar (kasa yazıcısı yoksa mutfak yazıcısından). Adres boşsa fişler yalnızca
 * Fişler sayfasında görünür.
 */
export function PrintersTab() {
  const settings = useQuery({ queryKey: ['printer-settings'], queryFn: () => api<PrinterSettings>('GET', '/print/settings') })
  // Form, ayar sunucudan yüklenince bir kez doldurulur; kayıttan sonra yazılanlar zaten sunucudakiyle aynıdır.
  return settings.data ? <PrintersForm initial={settings.data} /> : <p className="text-stone-500">Yükleniyor…</p>
}

function PrintersForm({ initial }: { initial: PrinterSettings }) {
  const queryClient = useQueryClient()
  const [kitchen, setKitchen] = useState(initial.kitchenPrinterAddress ?? '')
  const [receipt, setReceipt] = useState(initial.receiptPrinterAddress ?? '')
  const [codePage, setCodePage] = useState(String(initial.printerCodePage))
  const [message, setMessage] = useState<{ error: string | null; success: string | null }>({ error: null, success: null })

  const dirty =
    kitchen.trim() !== (initial.kitchenPrinterAddress ?? '') ||
    receipt.trim() !== (initial.receiptPrinterAddress ?? '') ||
    codePage !== String(initial.printerCodePage)

  const save = useMutation({
    mutationFn: () =>
      api<PrinterSettings>('PUT', '/print/settings', {
        kitchenPrinterAddress: kitchen.trim() || null,
        receiptPrinterAddress: receipt.trim() || null,
        printerCodePage: Number(codePage),
      }),
    onSuccess: (saved) => {
      setMessage({ error: null, success: 'Yazıcı ayarları kaydedildi.' })
      queryClient.setQueryData(['printer-settings'], saved)
    },
    onError: (err) => setMessage({ error: err.message, success: null }),
  })

  const test = useMutation({
    mutationFn: (target: 'kitchen' | 'receipt') => api<PrintResult>('POST', `/print/test?target=${target}`),
    onSuccess: (result, target) => {
      setMessage(describePrint(result, target === 'kitchen' ? 'Mutfak test fişi' : 'Kasa test fişi'))
      queryClient.invalidateQueries({ queryKey: ['tickets'] })
    },
    onError: (err) => setMessage({ error: err.message, success: null }),
  })

  return (
    <div className="space-y-4">
      <Message error={message.error} success={message.success} />

      <Card title="Yazıcı adresleri">
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault()
            save.mutate()
          }}
        >
          <PrinterField
            label="Mutfak yazıcısı"
            hint="Mutfak fişleri, iptal ve masa değişikliği fişleri."
            value={kitchen}
            onChange={setKitchen}
            onTest={() => test.mutate('kitchen')}
            testDisabled={dirty || test.isPending || !initial.kitchenPrinterAddress}
          />
          <PrinterField
            label="Kasa yazıcısı (isteğe bağlı)"
            hint="Hesap fişi ve Z raporu. Boş bırakılırsa bunlar da mutfak yazıcısından çıkar."
            value={receipt}
            onChange={setReceipt}
            onTest={() => test.mutate('receipt')}
            testDisabled={dirty || test.isPending || !(initial.receiptPrinterAddress ?? initial.kitchenPrinterAddress)}
          />

          <label className="block text-sm font-medium text-stone-700">
            Türkçe karakter tablosu
            <Input
              type="number"
              min={0}
              max={255}
              value={codePage}
              onChange={(e) => setCodePage(e.target.value)}
              className="mt-1 block w-24"
            />
            <span className="mt-1 block text-xs font-normal text-stone-500">
              Çoğu yazıcıda 13 (PC857). Test fişinde ş, ğ, ı harfleri bozuk çıkarsa yazıcının kılavuzundaki numarayı girin.
            </span>
          </label>

          <div className="flex items-center gap-3">
            <Button type="submit" variant="primary" disabled={!dirty || save.isPending}>
              {save.isPending ? 'Kaydediliyor…' : 'Kaydet'}
            </Button>
            {dirty && <span className="text-xs text-stone-500">Test fişi için önce kaydedin.</span>}
          </div>
        </form>
      </Card>

      <Card title="Yazıcının adresini bulma">
        <ol className="list-decimal space-y-1 pl-5 text-sm text-stone-700">
          <li>Yazıcıyı ağ kablosuyla modeme bağlayıp açın.</li>
          <li>Kapalıyken besleme (FEED) düğmesine basılı tutup açın: yazıcı, IP adresinin yazdığı bir ayar fişi basar.</li>
          <li>Adresi (ör. 192.168.1.50) buraya yazıp kaydedin, sonra "Test fişi" ile deneyin.</li>
        </ol>
        <p className="mt-2 rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-900">
          Modem yeniden başlayınca adres değişmesin diye modemden yazıcıya sabit adres (DHCP rezervasyonu) verin.
        </p>
      </Card>
    </div>
  )
}

function PrinterField(props: {
  label: string
  hint: string
  value: string
  onChange: (value: string) => void
  onTest: () => void
  testDisabled: boolean
}) {
  return (
    <div>
      <label className="block text-sm font-medium text-stone-700">
        {props.label}
        <div className="mt-1 flex gap-2">
          <Input
            value={props.value}
            onChange={(e) => props.onChange(e.target.value)}
            placeholder="ör. 192.168.1.50"
            maxLength={100}
            className="flex-1"
          />
          <Button onClick={props.onTest} disabled={props.testDisabled}>
            Test fişi
          </Button>
        </div>
      </label>
      <p className="mt-1 text-xs text-stone-500">{props.hint}</p>
    </div>
  )
}
