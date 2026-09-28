import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode } from 'react'

// Yönetim ekranının ortak görsel parçaları: aynı düğme ve kutu stilleri her sekmede tekrar yazılmasın.

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & { variant?: 'primary' | 'secondary' | 'danger' | 'link' }

export function Button({ variant = 'secondary', className = '', ...props }: ButtonProps) {
  const styles = {
    primary: 'bg-stone-900 text-white hover:bg-stone-700 px-4 py-2',
    secondary: 'bg-white text-stone-800 ring-1 ring-stone-300 hover:bg-stone-50 px-3 py-1.5',
    danger: 'bg-white text-red-700 ring-1 ring-red-200 hover:bg-red-50 px-3 py-1.5',
    link: 'text-amber-700 hover:underline px-1',
  }[variant]
  return (
    <button type="button" className={`rounded-lg text-sm font-semibold transition disabled:opacity-40 ${styles} ${className}`} {...props} />
  )
}

export function Input({ className = '', ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      className={`rounded-lg border border-stone-300 bg-white px-3 py-1.5 text-sm focus:border-amber-600 focus:ring-2 focus:ring-amber-200 focus:outline-none ${className}`}
      {...props}
    />
  )
}

export function Card({ title, actions, children }: { title: ReactNode; actions?: ReactNode; children: ReactNode }) {
  return (
    <section className="rounded-2xl bg-white p-4 shadow-sm ring-1 ring-stone-200">
      <header className="mb-3 flex flex-wrap items-center gap-2">
        <h2 className="flex-1 font-semibold text-stone-900">{title}</h2>
        {actions}
      </header>
      {children}
    </section>
  )
}

export function Badge({ tone, children }: { tone: 'gray' | 'green' | 'amber'; children: string }) {
  const styles = { gray: 'bg-stone-100 text-stone-600', green: 'bg-green-100 text-green-800', amber: 'bg-amber-100 text-amber-800' }[tone]
  return <span className={`rounded px-1.5 py-0.5 text-xs font-semibold ${styles}`}>{children}</span>
}

/** Sekmenin üstünde hata veya başarı mesajı. */
export function Message({ error, success }: { error: string | null; success?: string | null }) {
  if (error) return <p className="rounded-xl bg-red-50 px-4 py-2 text-sm text-red-700 ring-1 ring-red-200" role="alert">{error}</p>
  if (success) return <p className="rounded-xl bg-green-50 px-4 py-2 text-sm text-green-800 ring-1 ring-green-200" role="status">{success}</p>
  return null
}
