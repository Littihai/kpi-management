/** Width and height of the drawing the loading bar is made on (the player scales it to whatever size the page asks for). */
export const BAR_W = 240
export const BAR_H = 12

type Rgb = readonly [number, number, number]

const ease = { i: { x: 0.45, y: 1 }, o: { x: 0.55, y: 0 } }

const solid = (value: number | number[]) => ({ a: 0, k: value })

const color = ([r, g, b]: Rgb) => ({ ty: 'fl', c: solid([r, g, b, 1]), o: solid(100), r: 1, nm: 'Fill' })

const capsule = (width: number, fill: Rgb, center: [number, number], transform: object, name: string) => ({
  ty: 'gr',
  nm: name,
  it: [
    { ty: 'rc', d: 1, s: solid([width, 6]), p: solid([0, 0]), r: solid(3), nm: 'Capsule' },
    color(fill),
    { ty: 'tr', p: solid(center), a: solid([0, 0]), s: solid([100, 100]), r: solid(0), o: solid(100), sk: solid(0), sa: solid(0), ...transform },
  ],
})

const layer = (index: number, name: string, group: object) => ({
  ddd: 0, ind: index, ty: 4, nm: name, sr: 1,
  ks: { o: solid(100), r: solid(0), p: solid([0, 0, 0]), a: solid([0, 0, 0]), s: solid([100, 100, 100]) },
  ao: 0, shapes: [group], ip: 0, op: 120, st: 0, bm: 0,
})

/**
 * A Lottie animation of an indeterminate loading bar: a rounded track with a capsule that runs from one end to the other and back,
 * stretching in the middle and easing at both ends. Made here, not downloaded, so it works without internet and takes the page's colours.
 * Colours are 0..1 RGB.
 */
export function loadingBarAnimation(accent: Rgb, track: Rgb): string {
  const mid = BAR_H / 2
  const runner = capsule(90, accent, [45, mid], {
    p: { a: 1, k: [
      { t: 0, s: [45, mid], ...ease, to: [0, 0], ti: [0, 0] },
      { t: 60, s: [195, mid], ...ease, to: [0, 0], ti: [0, 0] },
      { t: 120, s: [45, mid] },
    ] },
    s: { a: 1, k: [
      { t: 0, s: [100, 100], ...ease },
      { t: 30, s: [150, 100], ...ease },
      { t: 60, s: [100, 100], ...ease },
      { t: 90, s: [150, 100], ...ease },
      { t: 120, s: [100, 100] },
    ] },
  }, 'Runner')
  const rail = capsule(BAR_W, track, [BAR_W / 2, mid], {}, 'Track')

  return JSON.stringify({
    v: '5.7.0', fr: 60, ip: 0, op: 120, w: BAR_W, h: BAR_H, nm: 'loading-bar', ddd: 0, assets: [],
    layers: [layer(1, 'Runner', runner), layer(2, 'Track', rail)],
  })
}
