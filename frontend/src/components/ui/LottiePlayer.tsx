import { useCallback } from 'react'
import { DotLottieReact, setWasmUrl, type DotLottie } from '@lottiefiles/dotlottie-react'
// The player's engine (WebAssembly) is served with the app: by default it would be fetched from a CDN, which a company network may not reach.
import wasmUrl from '@lottiefiles/dotlottie-web/dotlottie-player.wasm?url'

setWasmUrl(wasmUrl)

interface LottiePlayerProps {
  /** The animation as a Lottie JSON string. */
  data: string
  width: number
  height: number
  onReady: () => void
  onFail: () => void
}

/** Plays one looping Lottie animation. Kept apart from LoadingBar so the player (about 170 kB) is fetched after the first page, not with it. */
export default function LottiePlayer({ data, width, height, onReady, onFail }: LottiePlayerProps) {
  const attach = useCallback((player: DotLottie | null) => {
    if (!player) return
    player.addEventListener('load', onReady)
    player.addEventListener('loadError', onFail)
  }, [onReady, onFail])

  return (
    <DotLottieReact data={data} loop autoplay dotLottieRefCallback={attach} renderConfig={{ autoResize: true }}
      style={{ position: 'absolute', inset: 0, width, height }} />
  )
}
