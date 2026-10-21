import { useEffect, useRef, type ReactNode } from 'react'
import { ACCENTS, CHOICES, RADIUS_PREVIEW, type Accent, type Container, type FontFamily, type GreyBase, type NavLayout, type Radius, type ThemePref } from '../lib/appearance'
import { useAppearance } from './appearanceContext'
import Button from './ui/Button'

/**
 * Settings: an off-canvas panel from the right edge that holds every look-and-layout choice (the Lotline "Customize"
 * panel). It does not dim or block the page, because the point is to watch the change happen as it is picked. Close with
 * the button or Esc. Every choice applies at once and is remembered on this device.
 */

const fontStack: Record<FontFamily, string> = {
  inter: '"Inter", "Noto Sans Thai", sans-serif',
  plex: '"IBM Plex Sans Thai", "Noto Sans Thai", sans-serif',
  sarabun: '"Sarabun", "Noto Sans Thai", sans-serif',
  system: 'system-ui, -apple-system, "Segoe UI", "Leelawadee UI", sans-serif',
}

const labels = {
  theme: { light: 'สว่าง', dark: 'มืด', system: 'ตามเครื่อง' } as Record<ThemePref, string>,
  font: { inter: 'Inter', plex: 'IBM Plex', sarabun: 'Sarabun', system: 'ฟอนต์ระบบ' } as Record<FontFamily, string>,
  base: { slate: 'น้ำเงินเทา', gray: 'เทา', zinc: 'ซิงก์', neutral: 'กลาง', stone: 'หิน' } as Record<GreyBase, string>,
  radius: { none: 'เหลี่ยม', sm: 'เล็ก', md: 'กลาง', lg: 'ใหญ่', xl: 'มนมาก' } as Record<Radius, string>,
  nav: { side: 'แถบด้านข้าง', rail: 'ไอคอนอย่างเดียว', top: 'แถบด้านบน' } as Record<NavLayout, string>,
  container: { narrow: 'แคบ', standard: 'มาตรฐาน', wide: 'กว้าง', fluid: 'เต็มจอ' } as Record<Container, string>,
}

function Block({ title, desc, children }: { title: string; desc?: string; children: ReactNode }) {
  return (
    <section>
      <h3 className="text-[15px] font-semibold text-ink">{title}</h3>
      {desc && <p className="mt-0.5 text-[12.5px] text-ink-3">{desc}</p>}
      <div className="pt-2">{children}</div>
    </section>
  )
}

function Group({ title }: { title: string }) {
  return <div className="border-b border-line pb-0.5 text-xs font-bold text-ink-3">{title}</div>
}

/** A row of radio cards: a small picture above a label. */
function OptionCards<T extends string>({ value, options, onChange, label, min = 96 }: {
  value: T
  options: { value: T; label: string; preview: ReactNode }[]
  onChange: (v: T) => void
  label: string
  min?: number
}) {
  return (
    <div role="radiogroup" aria-label={label} className="grid gap-3 pt-1" style={{ gridTemplateColumns: `repeat(auto-fill, minmax(${min}px, 1fr))` }}>
      {options.map((o) => {
        const on = o.value === value
        return (
          <button key={o.value} type="button" role="radio" aria-checked={on} onClick={() => onChange(o.value)}
            className="opt-card flex flex-col gap-1.5 text-left text-ink">
            <span className="opt-preview">{o.preview}</span>
            <span className={`flex items-center gap-1 text-[13px] ${on ? 'font-semibold' : 'font-medium'}`}>
              {on && <i className="ti ti-check text-sm text-accent" aria-hidden="true" />}
              {o.label}
            </span>
          </button>
        )
      })}
    </div>
  )
}

/** A short row of text choices (width, text size, ...), drawn as Lotline's pill tabs. */
function Choose<T extends string>({ value, options, onChange, label }: {
  value: T
  options: { value: T; label: string }[]
  onChange: (v: T) => void
  label: string
}) {
  return (
    <div role="radiogroup" aria-label={label} className="flex flex-wrap gap-1.5">
      {options.map((o) => {
        const on = o.value === value
        return (
          <button key={o.value} type="button" role="radio" aria-checked={on} onClick={() => onChange(o.value)}
            className={`min-h-8 rounded-full border px-3.5 text-[13px] font-semibold transition-colors
              ${on ? 'border-accent bg-accent text-on-accent' : 'border-line bg-card text-ink-2 hover:border-line-strong hover:text-ink'}`}>
            {o.label}
          </button>
        )
      })}
    </div>
  )
}

/** A tiny app window drawn with the target palette's own tokens. */
function ThemePreview({ scope, half, base }: { scope: 'theme-light' | 'theme-dark'; half?: 'left' | 'right'; base?: GreyBase }) {
  return (
    <span className={scope} data-base={base && base !== CHOICES.base.default ? base : undefined} aria-hidden="true"
      style={{ flex: 1, height: '100%', display: 'flex', background: 'var(--lt-bg)', borderLeft: half === 'right' ? '1px solid var(--lt-n300)' : undefined }}>
      <span style={{ width: half ? '30%' : '26%', background: 'var(--lt-surface)', borderRight: '1px solid var(--lt-n200)', padding: 7, display: 'flex', flexDirection: 'column', gap: 5 }}>
        <span style={{ height: 5, borderRadius: 3, background: 'var(--lt-accent)' }} />
        <span style={{ height: 4, borderRadius: 3, background: 'var(--lt-n300)' }} />
        <span style={{ height: 4, borderRadius: 3, background: 'var(--lt-n300)' }} />
      </span>
      <span style={{ flex: 1, padding: 8, display: 'flex', flexDirection: 'column', gap: 6 }}>
        <span style={{ height: 6, width: '55%', borderRadius: 3, background: 'var(--lt-text)', opacity: 0.8 }} />
        <span style={{ flex: 1, borderRadius: 4, background: 'var(--lt-surface)', border: '1px solid var(--lt-n200)' }} />
      </span>
    </span>
  )
}

/** The three menu layouts as wireframes. */
function NavPreview({ kind }: { kind: NavLayout }) {
  const line = { height: 4, borderRadius: 3, background: 'var(--lt-n300)' } as const
  const strip = { background: 'var(--lt-surface)', borderBottom: '1px solid var(--lt-n200)' } as const
  const card = <span style={{ flex: 1, borderRadius: 4, background: 'var(--lt-surface)', border: '1px solid var(--lt-n200)' }} />
  if (kind === 'top') {
    return (
      <span aria-hidden="true" style={{ display: 'flex', flexDirection: 'column', height: '100%', background: 'var(--lt-bg)' }}>
        <span style={{ ...strip, height: 11, padding: '3px 7px', display: 'flex', alignItems: 'center' }}>
          <span style={{ width: 7, height: 5, borderRadius: 2, background: 'var(--lt-accent)' }} />
        </span>
        <span style={{ ...strip, height: 10, padding: '3px 7px', display: 'flex', alignItems: 'center', gap: 6 }}>
          <span style={{ ...line, width: 16, background: 'var(--lt-text)', opacity: 0.7 }} /><span style={{ ...line, width: 16 }} /><span style={{ ...line, width: 16 }} />
        </span>
        <span style={{ flex: 1, padding: 7, display: 'flex' }}>{card}</span>
      </span>
    )
  }
  const wide = kind === 'side'
  return (
    <span aria-hidden="true" style={{ display: 'flex', height: '100%', background: 'var(--lt-bg)' }}>
      <span style={{ width: wide ? '30%' : '13%', background: 'var(--lt-surface)', borderRight: '1px solid var(--lt-n200)', padding: '7px 6px', display: 'flex', flexDirection: 'column', gap: 6, alignItems: wide ? 'stretch' : 'center' }}>
        <span style={{ height: 5, width: wide ? '60%' : 8, borderRadius: 2, background: 'var(--lt-accent)' }} />
        {[0, 1, 2].map((i) => <span key={i} style={wide ? line : { height: 5, width: 5, borderRadius: 2, background: 'var(--lt-n300)' }} />)}
      </span>
      <span style={{ flex: 1, display: 'flex', flexDirection: 'column' }}>
        <span style={{ ...strip, height: 11 }} />
        <span style={{ flex: 1, padding: 7, display: 'flex' }}>{card}</span>
      </span>
    </span>
  )
}

function FontSample({ id }: { id: FontFamily }) {
  return (
    <span aria-hidden="true" style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 2, height: '100%', background: 'var(--lt-surface)', fontFamily: fontStack[id] }}>
      <span style={{ fontSize: 22, fontWeight: 600, lineHeight: 1.2 }}>Aa กข</span>
      <span style={{ fontSize: 11, color: 'var(--lt-n600)' }}>Helpdesk 0123</span>
    </span>
  )
}

function RadiusSample({ id }: { id: Radius }) {
  return (
    <span aria-hidden="true" style={{ display: 'grid', placeItems: 'center', height: '100%', background: 'var(--lt-n50)' }}>
      <span style={{ width: 38, height: 38, borderTop: '3px solid var(--lt-accent)', borderLeft: '3px solid var(--lt-accent)', borderTopLeftRadius: Math.round(RADIUS_PREVIEW[id] * 1.6), background: 'var(--lt-surface)' }} />
    </span>
  )
}

export default function SettingsPanel({ open, onClose, returnFocusId }: { open: boolean; onClose: () => void; returnFocusId?: string }) {
  const ap = useAppearance()
  const closeRef = useRef<HTMLButtonElement>(null)
  const wasOpen = useRef(false)

  useEffect(() => {
    if (open) closeRef.current?.focus()
    else if (wasOpen.current && returnFocusId) document.getElementById(returnFocusId)?.focus()
    wasOpen.current = open
  }, [open, returnFocusId])

  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [open, onClose])

  if (!open) return null
  const scope = ap.resolvedTheme === 'dark' ? 'theme-dark' : 'theme-light'

  return (
    <aside id="settings-panel" role="dialog" aria-modal="false" aria-labelledby="settings-title"
      className="offcanvas-in fixed inset-y-0 right-0 z-[60] flex w-[min(400px,100vw)] flex-col border-l border-line bg-card text-ink shadow-pop">
      <div className="flex items-start justify-between gap-3 border-b border-line px-[18px] pb-3 pt-4">
        <div className="min-w-0">
          <h2 id="settings-title" className="text-lg font-semibold">ตั้งค่า</h2>
          <p className="mt-0.5 text-xs text-ink-3">ปรับหน้าตาและการจัดวาง เปลี่ยนแล้วเห็นผลทันที และจำไว้ในเครื่องนี้</p>
        </div>
        <button ref={closeRef} type="button" onClick={onClose} aria-label="ปิด" title="ปิด (Esc)"
          className="flex h-9 w-9 flex-none items-center justify-center rounded-control text-ink-2 hover:bg-surface">
          <i className="ti ti-x text-lg" aria-hidden="true" />
        </button>
      </div>

      <div className="flex-1 space-y-6 overflow-y-auto px-[18px] pb-6 pt-4">
        <Group title="หน้าตา" />

        <Block title="ธีม" desc="สว่าง มืด หรือตามการตั้งค่าของเครื่อง">
          <OptionCards<ThemePref> label="ธีม" value={ap.theme} onChange={(v) => ap.set('theme', v)}
            options={CHOICES.theme.values.map((v) => ({
              value: v, label: labels.theme[v],
              preview: v === 'system'
                ? <span style={{ display: 'flex', height: '100%' }}><ThemePreview scope="theme-light" half="left" /><ThemePreview scope="theme-dark" half="right" /></span>
                : <ThemePreview scope={v === 'dark' ? 'theme-dark' : 'theme-light'} />,
            }))} />
        </Block>

        <Block title="สีหลัก" desc="สีของปุ่ม ลิงก์ และเมนูที่เลือก">
          <div role="radiogroup" aria-label="สีหลัก" className="flex flex-wrap gap-x-2.5 gap-y-3 pt-1">
            {ACCENTS.map((a) => {
              const on = ap.accent === a.id
              return (
                <button key={a.id} type="button" role="radio" aria-checked={on} title={a.label}
                  onClick={() => ap.set('accent', a.id as Accent)}
                  className="flex w-14 flex-col items-center gap-1.5 text-ink">
                  <span aria-hidden="true" className="grid h-[34px] w-[34px] place-items-center rounded-full text-white"
                    style={{ background: a.swatch, outline: on ? `2px solid ${a.swatch}` : '1px solid var(--lt-n300)', outlineOffset: on ? 3 : 0 }}>
                    {on && <i className="ti ti-check text-base" />}
                  </span>
                  <span className={`text-center text-xs leading-tight ${on ? 'font-semibold' : 'font-medium'}`}>{a.label}</span>
                </button>
              )
            })}
          </div>
        </Block>

        <Block title="ฟอนต์" desc="อ่านภาษาไทยได้ทุกแบบ">
          <OptionCards<FontFamily> label="ฟอนต์" min={140} value={ap.font} onChange={(v) => ap.set('font', v)}
            options={CHOICES.font.values.map((v) => ({ value: v, label: labels.font[v], preview: <FontSample id={v} /> }))} />
        </Block>

        <Block title="โทนสีเทา" desc="สีพื้นหลังและเส้นขอบทั้งหมด">
          <OptionCards<GreyBase> label="โทนสีเทา" value={ap.base} onChange={(v) => ap.set('base', v)}
            options={CHOICES.base.values.map((v) => ({ value: v, label: labels.base[v], preview: <ThemePreview scope={scope} base={v} /> }))} />
        </Block>

        <Block title="ความโค้งของมุม">
          <OptionCards<Radius> label="ความโค้งของมุม" min={54} value={ap.radius} onChange={(v) => ap.set('radius', v)}
            options={CHOICES.radius.values.map((v) => ({ value: v, label: labels.radius[v], preview: <RadiusSample id={v} /> }))} />
        </Block>

        <Group title="การจัดวาง" />

        <Block title="เมนูหลัก" desc="แถบด้านข้าง แถบไอคอน หรือแถบใต้หัวหน้า (บนมือถือเป็นเมนูเลื่อนลงเสมอ)">
          <OptionCards<NavLayout> label="เมนูหลัก" value={ap.nav} onChange={(v) => ap.set('nav', v)}
            options={CHOICES.nav.values.map((v) => ({ value: v, label: labels.nav[v], preview: <NavPreview kind={v} /> }))} />
        </Block>

        <Block title="ความกว้างของหน้า">
          <Choose<Container> label="ความกว้างของหน้า" value={ap.container} onChange={(v) => ap.set('container', v)}
            options={CHOICES.container.values.map((v) => ({ value: v, label: labels.container[v] }))} />
        </Block>

        <Group title="การเข้าถึง" />

        <Block title="ขนาดตัวอักษร" desc="ขยายหรือย่อทั้งหน้า (ตัวอักษร ปุ่ม และระยะห่าง)">
          <Choose<'s' | 'm' | 'l'> label="ขนาดตัวอักษร" value={ap.size} onChange={(v) => ap.set('size', v)}
            options={[{ value: 's', label: 'เล็ก' }, { value: 'm', label: 'กลาง' }, { value: 'l', label: 'ใหญ่' }]} />
        </Block>

        <Block title="ความคมชัดสูง" desc="ตัวอักษรเข้มขึ้นและเส้นขอบชัดขึ้น เหมาะกับจอที่มีแสงสะท้อน">
          <Choose<'standard' | 'high'> label="ความคมชัด" value={ap.contrast} onChange={(v) => ap.set('contrast', v)}
            options={[{ value: 'standard', label: 'มาตรฐาน' }, { value: 'high', label: 'สูง' }]} />
        </Block>

        <Block title="การเคลื่อนไหว" desc="อัตโนมัติ = ตามที่เครื่องตั้งไว้">
          <Choose<'auto' | 'reduce' | 'full'> label="การเคลื่อนไหว" value={ap.motion} onChange={(v) => ap.set('motion', v)}
            options={[{ value: 'auto', label: 'อัตโนมัติ' }, { value: 'reduce', label: 'ลดลง' }, { value: 'full', label: 'เต็มที่' }]} />
        </Block>
      </div>

      <div className="flex items-center justify-between gap-3 border-t border-line bg-surface px-[18px] py-3">
        <Button onClick={ap.reset} disabled={!ap.changed} title="คืนทุกอย่างเป็นค่าเริ่มต้น">คืนค่าเริ่มต้น</Button>
        <Button variant="ghost" onClick={onClose}>เสร็จสิ้น</Button>
      </div>
    </aside>
  )
}
