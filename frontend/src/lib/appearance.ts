/**
 * Everything a person can change about how the app looks and is laid out. It is kept per device (localStorage) and each
 * choice is one attribute on <html>; styles/tokens.css reads them. `public/theme-init.js` sets them before the first
 * paint so nothing flashes. A default has NO attribute, so a cleared storage means exactly the default look.
 * The names and values follow the Lotline platform's Customize panel.
 */

export type ThemePref = 'light' | 'dark' | 'system'
export type Accent = 'steel' | 'azure' | 'teal' | 'green' | 'indigo' | 'violet' | 'rose' | 'amber' | 'slate'
export type FontFamily = 'inter' | 'plex' | 'sarabun' | 'system'
export type GreyBase = 'slate' | 'gray' | 'zinc' | 'neutral' | 'stone'
export type Radius = 'none' | 'sm' | 'md' | 'lg' | 'xl'
export type NavLayout = 'side' | 'rail' | 'top'
export type Container = 'narrow' | 'standard' | 'wide' | 'fluid'
export type Contrast = 'standard' | 'high'
export type Motion = 'auto' | 'reduce' | 'full'
export type TextSize = 's' | 'm' | 'l'

export interface AppearanceState {
  theme: ThemePref
  accent: Accent
  font: FontFamily
  base: GreyBase
  radius: Radius
  nav: NavLayout
  container: Container
  contrast: Contrast
  motion: Motion
  size: TextSize
}

interface Choice<T extends string> {
  /** localStorage key */
  key: string
  /** attribute on <html>; null when the choice is applied some other way */
  attr: string | null
  default: T
  values: readonly T[]
}

const choice = <T extends string>(name: string, attr: string | null, def: T, values: readonly T[]): Choice<T> =>
  ({ key: `kpi.${name}`, attr, default: def, values })

export const CHOICES: { [K in keyof AppearanceState]: Choice<AppearanceState[K]> } = {
  theme: choice<ThemePref>('theme', null, 'light', ['light', 'dark', 'system']),
  accent: choice<Accent>('accent', 'data-accent', 'steel', ['steel', 'azure', 'teal', 'green', 'indigo', 'violet', 'rose', 'amber', 'slate']),
  font: choice<FontFamily>('font', 'data-font', 'inter', ['inter', 'plex', 'sarabun', 'system']),
  base: choice<GreyBase>('base', 'data-base', 'slate', ['slate', 'gray', 'zinc', 'neutral', 'stone']),
  radius: choice<Radius>('radius', 'data-radius', 'sm', ['none', 'sm', 'md', 'lg', 'xl']),
  nav: choice<NavLayout>('nav', 'data-nav', 'side', ['side', 'rail', 'top']),
  container: choice<Container>('container', 'data-container', 'standard', ['narrow', 'standard', 'wide', 'fluid']),
  contrast: choice<Contrast>('contrast', 'data-contrast', 'standard', ['standard', 'high']),
  motion: choice<Motion>('motion', 'data-motion', 'auto', ['auto', 'reduce', 'full']),
  size: choice<TextSize>('size', 'data-size', 'm', ['s', 'm', 'l']),
}

export const DEFAULTS = Object.fromEntries(
  Object.entries(CHOICES).map(([k, c]) => [k, c.default]),
) as unknown as AppearanceState

/** `swatch` is the light-theme colour shown on the picker; the full ramp lives in tokens.css. */
export const ACCENTS: readonly { id: Accent; swatch: string; label: string }[] = [
  { id: 'steel', swatch: '#066fd1', label: 'น้ำเงิน' },
  { id: 'azure', swatch: '#0b78a8', label: 'ฟ้า' },
  { id: 'teal', swatch: '#2f7f86', label: 'เขียวน้ำทะเล' },
  { id: 'green', swatch: '#3f7f58', label: 'เขียว' },
  { id: 'indigo', swatch: '#4c58c4', label: 'คราม' },
  { id: 'violet', swatch: '#6b5fb0', label: 'ม่วง' },
  { id: 'rose', swatch: '#b0566f', label: 'กุหลาบ' },
  { id: 'amber', swatch: '#a8612a', label: 'อำพัน' },
  { id: 'slate', swatch: '#4f6076', label: 'เทาน้ำเงิน' },
]

/** Corner of the radius preview, in px. */
export const RADIUS_PREVIEW: Record<Radius, number> = { none: 0, sm: 4, md: 8, lg: 12, xl: 18 }

function read(key: string): string | null {
  try { return localStorage.getItem(key) } catch { return null }
}

export function savedAll(): AppearanceState {
  const out: Record<string, string> = {}
  for (const k of Object.keys(CHOICES) as (keyof AppearanceState)[]) {
    const c = CHOICES[k] as Choice<string>
    const v = read(c.key)
    out[k] = v && c.values.includes(v) ? v : c.default
  }
  return out as unknown as AppearanceState
}

export function store<K extends keyof AppearanceState>(k: K, v: AppearanceState[K]) {
  const c = CHOICES[k] as Choice<string>
  try {
    if (v === c.default) localStorage.removeItem(c.key)
    else localStorage.setItem(c.key, v)
  } catch { /* not remembered, still applied now */ }
}

const media = '(prefers-color-scheme: dark)'
export const systemDark = (): boolean => {
  try { return window.matchMedia(media).matches } catch { return false }
}
export const resolveTheme = (pref: ThemePref): 'light' | 'dark' =>
  pref === 'dark' || (pref === 'system' && systemDark()) ? 'dark' : 'light'

/** Put every choice on <html>; a default removes its attribute so the stylesheet stays untouched. */
export function applyAppearance(a: AppearanceState) {
  const root = document.documentElement
  for (const k of Object.keys(CHOICES) as (keyof AppearanceState)[]) {
    const c = CHOICES[k] as Choice<string>
    if (!c.attr) continue
    if (a[k] === c.default) root.removeAttribute(c.attr)
    else root.setAttribute(c.attr, a[k])
  }
  if (resolveTheme(a.theme) === 'dark') root.setAttribute('data-theme', 'dark')
  else root.removeAttribute('data-theme')
}

export function isCustomised(a: AppearanceState): boolean {
  return (Object.keys(CHOICES) as (keyof AppearanceState)[]).some((k) => a[k] !== CHOICES[k].default)
}
