import type { NotificationLevel } from '../types/notification'

/** The icon and colour bar of each level, as on the live toast. */
export const levelStyle: Record<NotificationLevel, { icon: string; bar: string }> = {
  info: { icon: 'info-circle', bar: 'bg-blue-dot' },
  success: { icon: 'circle-check', bar: 'bg-green-dot' },
  warning: { icon: 'alert-triangle', bar: 'bg-amber-dot' },
  error: { icon: 'alert-circle', bar: 'bg-red-dot' },
}

export function levelOf(level: string): { icon: string; bar: string } {
  return levelStyle[level as NotificationLevel] ?? levelStyle.info
}
