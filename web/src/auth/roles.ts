import type { UserRole } from '../api/types'

/** Sahip ve yönetici: ana menüden rapor, fiş ve yönetim bölümlerine geçer. */
export const isManagement = (role: UserRole) => role === 'Owner' || role === 'Manager'

/**
 * Giriş sonrası her rolün açılış ekranı. Tek bölümü olan roller (garson, kasa, mutfak) ana menüyü
 * atlayıp doğrudan işine gider; aradaki menü yalnızca fazladan dokunuş olurdu.
 */
export function homePathFor(role: UserRole) {
  if (isManagement(role)) return '/ana-menu'
  return role === 'Kitchen' ? '/fisler' : '/garson'
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
