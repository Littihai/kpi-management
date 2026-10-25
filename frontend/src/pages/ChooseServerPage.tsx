import BrandName from '../components/BrandName'
import ServerAddressDialog from '../components/ServerAddressDialog'

/**
 * The Windows app has no server of its own and none is built in: no company's system is the default. Until the person (or the
 * company's IT, through server.txt / HELPDESK_SERVER_URL) says which server to use, this is all there is.
 */
export default function ChooseServerPage() {
  return (
    <main className="grid min-h-[var(--vh100)] place-items-center bg-canvas px-4 py-12">
      <div className="text-center">
        <span className="mx-auto flex h-12 w-12 items-center justify-center rounded-xl bg-accent text-on-accent" aria-hidden="true">
          <i className="ti ti-server text-2xl" />
        </span>
        <BrandName className="mt-3 block text-lg font-semibold text-ink" />
        <p className="mt-1 text-sm text-ink-3">ยังไม่ได้เลือกเซิร์ฟเวอร์</p>
      </div>
      <ServerAddressDialog open required onClose={() => undefined} />
    </main>
  )
}
