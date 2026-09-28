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
}

export type Order = {
  id: string
  source: 'Qr' | 'Staff'
  status: OrderStatus
  createdAt: string
  total: number
  items: OrderItem[]
}

export type Session = {
  id: string
  tableId: string
  tableName: string
  status: 'Open' | 'Closed'
  openedAt: string
  closedAt: string | null
  total: number
  version: number
  orders: Order[]
}

/** Mutfak kartı (sunucudaki KitchenOrderDto). */
export type KitchenOrder = {
  id: string
  sessionId: string
  tableId: string
  tableName: string
  status: OrderStatus
  createdAt: string
  createdByName: string | null
  items: OrderItem[]
}

export type NewOrderItem = {
  productId: string
  quantity: number
  note?: string | null
}
