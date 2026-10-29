import { Link, useLocation } from 'react-router-dom'
import { isItemActive, useNavGroups } from './useNavGroups'

/** The menu as a bar under the top bar (Lotline's default layout): one underlined tab per page. */
export default function TopNav() {
  const { groups } = useNavGroups()
  const { pathname, search } = useLocation()
  const items = groups.flatMap((g) => g.items)

  return (
    <nav aria-label="เมนูหลัก" className="hidden border-b border-line bg-card lg:block">
      <ul className="mx-auto flex max-w-[var(--page-max)] gap-1 overflow-x-auto px-6">
        {items.map((item) => {
          const isActive = isItemActive(item, pathname, search)
          return (
            <li key={item.to}>
              <Link to={item.to} aria-current={isActive ? 'page' : undefined}
                className={`relative flex items-center gap-2 whitespace-nowrap rounded-t-control px-3 py-2.5 text-sm font-medium transition-colors
                  ${isActive ? 'font-semibold text-accent-ink' : 'text-ink-3 hover:bg-surface hover:text-ink'}`}>
                <i className={`ti ti-${item.icon} text-base`} aria-hidden="true" />
                {item.label}
                {item.badge ? (
                  <span className={`tabular rounded-full px-1.5 text-[10.5px] font-bold leading-[1.6]
                    ${item.hot ? 'bg-red-dot text-white' : isActive ? 'bg-accent text-on-accent' : 'bg-surface-2 text-ink-2'}`}
                    aria-label={`${item.badge} เรื่อง`}>
                    {item.badge}
                  </span>
                ) : null}
                {isActive && <span className="absolute inset-x-2 bottom-0 h-0.5 rounded-t bg-accent" aria-hidden="true" />}
              </Link>
            </li>
          )
        })}
      </ul>
    </nav>
  )
}
