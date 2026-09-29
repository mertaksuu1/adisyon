// Sunucudaki C# kayıtlarının (record) TypeScript karşılıkları.
// Alan adları API'nin JSON çıktısıyla birebir aynı olmalı.

export type UserRole = 'Owner' | 'Manager' | 'Waiter' | 'Kitchen' | 'Cashier'

export type User = {
  id: string
  tenantId: string
  branchId: string | null
  displayName: string
  role: UserRole
  isActive: boolean
}

export type PairDeviceResponse = {
  deviceToken: string
  deviceId: string
  tenantName: string
  branchName: string
}

/** İlk kurulum sonucu: bu bilgisayar eşleştirilmiş ve işletme sahibi giriş yapmış olur. */
export type SetupResponse = {
  device: PairDeviceResponse
  login: LoginResponse
}

export type LoginResponse = {
  token: string
  expiresAt: string
  user: User
}

export type Product = {
  id: string
  categoryId: string
  name: string
  description: string | null
  price: number
  sortOrder: number
  isActive: boolean
}

export type Category = {
  id: string
  name: string
  sortOrder: number
  isActive: boolean
  products: Product[]
}

export type OpenSessionSummary = {
  sessionId: string
  openedAt: string
  total: number
}

export type Table = {
  id: string
  name: string
  sortOrder: number
  isActive: boolean
  qrToken: string
  /** null ise masa boş. */
  openSession: OpenSessionSummary | null
}

export type OrderStatus = 'New' | 'Preparing' | 'Ready' | 'Served' | 'Cancelled'

export type OrderItem = {
  id: string
  productId: string
  productName: string
  unitPrice: number
  quantity: number
  note: string | null
  /** İptal edildi: hesaba yansımaz. */
  isVoided: boolean
  /** İkram edildi: fişte görünür, ücreti alınmaz. */
  isComped: boolean
}

export type Order = {
  id: string
  source: 'Qr' | 'Staff'
  status: OrderStatus
  createdAt: string
  total: number
  items: OrderItem[]
}

export type PaymentMethod = 'Cash' | 'Card'

export type Payment = {
  id: string
  method: PaymentMethod
  amount: number
  createdAt: string
}

export type Session = {
  id: string
  tableId: string
  tableName: string
  status: 'Open' | 'Closed'
  openedAt: string
  closedAt: string | null
  total: number
  paid: number
  remaining: number
  version: number
  orders: Order[]
  payments: Payment[]
  /** Yalnızca sipariş gönderince: mutfak fişi yazdırılamadıysa uyarı ve tekrar yazdırılacak fişin kimliği. */
  printWarning?: string | null
  failedTicketId?: string | null
}

/** Preview: yazıcı ayarlı değil, yalnızca ekranda. Printed: yazıcıya gitti. Failed: yazıcıya ulaşılamadı. */
export type PrintStatus = 'Preview' | 'Printed' | 'Failed'

/** Yazdırılan (veya yazdırılamayan) fiş (sunucudaki PrintedTicket). */
export type PrintedTicket = {
  id: string
  kind: 'Kitchen' | 'Bill' | 'Report'
  branchId: string
  /** Masa adı. */
  title: string
  printedAt: string
  /** Kâğıda basılacak düz metin, 48 karakter genişliğinde. */
  text: string
  status: PrintStatus
  error: string | null
}

/** Yazdırma isteğinin sonucu (hesap fişi, Z raporu, tekrar yazdırma, test fişi). */
export type PrintResult = {
  ticketId: string
  status: PrintStatus
  error: string | null
}

/** Şubenin yazıcı ayarları. Adres boşsa o fişler yalnızca Fişler sayfasında görünür. */
export type PrinterSettings = {
  kitchenPrinterAddress: string | null
  receiptPrinterAddress: string | null
  printerCodePage: number
}

export type NewOrderItem = {
  productId: string
  quantity: number
  note?: string | null
}

/** Gün sonu (Z) raporu (sunucudaki ZReport). */
export type ZReport = {
  /** İş günü, "2026-09-28". */
  date: string
  from: string
  to: string
  cashTotal: number
  cardTotal: number
  paymentsTotal: number
  closedSessionCount: number
  salesTotal: number
  averageBill: number
  voids: AdjustmentLine[]
  voidsTotal: number
  comps: AdjustmentLine[]
  compsTotal: number
  topProducts: { productName: string; quantity: number; amount: number }[]
  openTableCount: number
  openTablesTotal: number
}

export type AdjustmentLine = {
  at: string
  tableName: string
  productName: string
  quantity: number
  amount: number
  byName: string | null
}

export type Branch = {
  id: string
  name: string
  address: string | null
}

export type Device = {
  id: string
  name: string
  isActive: boolean
  createdAt: string
  lastSeenAt: string | null
  /** Şu an kullanılan cihaz (bağlantısı kesilemez). */
  isCurrent: boolean
}

export type BackupInfo = {
  fileName: string
  createdAt: string
  sizeBytes: number
}

export type SystemInfo = {
  /** Diğer cihazların açacağı adresler, ör. "http://192.168.1.20:5000". */
  addresses: string[]
  lastBackup: BackupInfo | null
  backupDirectory: string
}

/** Yapay zeka cevabı. available false ise text, neden kullanılamadığını açıklar (anahtar yok, internet yok…). */
export type AiResult = {
  available: boolean
  text: string
}
