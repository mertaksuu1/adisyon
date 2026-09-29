import type { PrintResult } from '../api/types'

/**
 * Yazdırma sonucunu kullanıcıya gösterilecek cümleye çevirir.
 * Yazıcıya ulaşılamadıysa `error` dolu gelir (kırmızı gösterilir); diğer durumlarda `success`.
 */
export function describePrint(result: PrintResult, what: string): { error: string | null; success: string | null } {
  switch (result.status) {
    case 'Printed':
      return { error: null, success: `${what} yazdırıldı.` }
    case 'Preview':
      return { error: null, success: `${what} hazır. Yazıcı ayarlı olmadığı için yalnızca Fişler sayfasında görünüyor.` }
    case 'Failed':
      return { error: `${what} yazdırılamadı! ${result.error ?? ''} Yazıcıyı kontrol edip tekrar deneyin.`, success: null }
  }
}
