import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'

/** Yapay zeka ücretli bir ek pakettir. Bu kurulumda açık mı? (Yüklenirken undefined.) */
export function useAiPackage() {
  const status = useQuery({
    queryKey: ['ai-status'],
    queryFn: () => api<{ configured: boolean }>('GET', '/ai/status'),
    staleTime: 5 * 60_000,
  })
  return status.data?.configured
}

export const AI_PACKAGE_OFF_TEXT = 'Yapay zeka ek paketi bu işletmede açık değil. Açtırmak için satıcınızla görüşün.'
