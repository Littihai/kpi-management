import { useEffect, useState } from 'react'
import { Card, CardHeader } from '../ui/Card'
import { Alert } from '../ui/Layout'
import { SwitchLine } from '../ui/SettingLine'
import { chooseWebNotifications, webNotificationsChosen, webNotificationsSupported } from '../../lib/notify'

/** Why a browser said no, in words that tell the person what to do next. */
function blockedReason(permission: NotificationPermission): string {
  if (permission === 'denied') {
    return window.isSecureContext
      ? 'เบราว์เซอร์บล็อกการแจ้งเตือนของเว็บไซต์นี้ กดไอคอนข้างแถบที่อยู่ เปลี่ยนการแจ้งเตือนเป็น "อนุญาต" แล้วเปิดสวิตช์อีกครั้ง'
      : 'เบราว์เซอร์ไม่อนุญาตการแจ้งเตือนบนที่อยู่แบบ http:// ต้องเปิดระบบผ่าน https:// (หรือ localhost) จึงจะใช้ได้'
  }
  return 'ยังไม่ได้อนุญาต เลือก "อนุญาต" ในกล่องที่เบราว์เซอร์แสดง แล้วเปิดสวิตช์อีกครั้ง'
}

/**
 * Whether to be told, outside the page, about what concerns the signed-in person. In the Windows app the choices live in
 * the app (they apply to this computer); in a browser the choice is kept in that browser and also needs its permission.
 */
export default function NotificationControls() {
  const desktop = window.desktop
  const [prefs, setPrefs] = useState<DesktopPrefs | null>(null)
  const [web, setWeb] = useState(webNotificationsChosen)
  const [problem, setProblem] = useState('')

  useEffect(() => {
    desktop?.getPrefs().then(setPrefs).catch(() => setProblem('อ่านการตั้งค่าของแอปไม่สำเร็จ'))
  }, [desktop])

  const change = (patch: Partial<DesktopPrefs>) => {
    setProblem('')
    desktop?.setPrefs(patch).then(setPrefs).catch(() => setProblem('บันทึกการตั้งค่าของแอปไม่สำเร็จ'))
  }

  const changeWeb = async (on: boolean) => {
    setProblem('')
    if (on && Notification.permission === 'default') await Notification.requestPermission()
    if (on && Notification.permission !== 'granted') {
      setProblem(blockedReason(Notification.permission))
      return
    }
    chooseWebNotifications(on)
    setWeb(on)
  }

  const supported = webNotificationsSupported()

  return (
    <Card id="setting-notify">
      <CardHeader eyebrow="เครื่องนี้" title="การแจ้งเตือน" />
      <div className="space-y-5 px-5 pb-5 pt-4">
        {problem && <Alert tone="error" onClose={() => setProblem('')}>{problem}</Alert>}
        {desktop ? (
          <>
            <SwitchLine title="แจ้งเตือนบนเดสก์ท็อป" disabled={!prefs} checked={prefs?.notifications ?? false}
              desc="แสดงการแจ้งเตือนของ Windows เมื่อมีเรื่องที่เกี่ยวกับคุณ ขณะที่หน้าต่างถูกย่อหรืออยู่เบื้องหลัง คลิกที่การแจ้งเตือนเพื่อเปิดเรื่องนั้น"
              onChange={(on) => change({ notifications: on })} />
            <SwitchLine title="ทำงานต่อที่ถาดระบบเมื่อปิดหน้าต่าง" disabled={!prefs} checked={prefs?.closeToTray ?? false}
              desc="เมื่อกดปิดหน้าต่าง โปรแกรมยังทำงานอยู่เพื่อรับการแจ้งเตือน เปิดกลับได้จากไอคอนที่ถาดระบบ"
              onChange={(on) => change({ closeToTray: on })} />
            <SwitchLine title="เปิดโปรแกรมเมื่อเริ่ม Windows" disabled={!prefs} checked={prefs?.startWithWindows ?? false}
              desc="เปิดโปรแกรมให้เองทุกครั้งที่เข้าสู่ระบบ Windows"
              onChange={(on) => change({ startWithWindows: on })} />
          </>
        ) : (
          <SwitchLine title="แจ้งเตือนบนเดสก์ท็อป" disabled={!supported} checked={web && supported && Notification.permission === 'granted'}
            desc={supported
              ? 'แสดงการแจ้งเตือนของเบราว์เซอร์เมื่อมีเรื่องที่เกี่ยวกับคุณ ขณะที่คุณไม่ได้เปิดหน้านี้อยู่ตรงหน้า คลิกที่การแจ้งเตือนเพื่อเปิดเรื่องนั้น'
              : 'เบราว์เซอร์นี้ไม่รองรับการแจ้งเตือน'}
            onChange={changeWeb} />
        )}
      </div>
    </Card>
  )
}
