import type { UserRole } from '../api/types'

/** Giriş sonrası her rolün açılış ekranı. */
export function homePathFor(role: UserRole) {
  return role === 'Kitchen' ? '/mutfak' : '/garson'
}

export const roleLabels: Record<UserRole, string> = {
  Owner: 'İşletme Sahibi',
  Manager: 'Yönetici',
  Waiter: 'Garson',
  Kitchen: 'Mutfak',
  Cashier: 'Kasa',
}

/** Hesap kapatabilen roller (sunucudaki RoleNames.Checkout ile aynı). */
export const canCheckout = (role: UserRole) => role === 'Owner' || role === 'Manager' || role === 'Cashier'
