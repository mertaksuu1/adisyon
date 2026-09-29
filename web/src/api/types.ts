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
}

/** Sanal yazıcının bastığı fiş (sunucudaki PrintedTicket). */
export type PrintedTicket = {
  id: string
  kind: 'Kitchen' | 'Bill' | 'Report'
  branchId: string
  /** Masa adı. */
  title: string
  printedAt: string
  /** Kâğıda basılacak düz metin, 48 karakter genişliğinde. */
  text: string
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
