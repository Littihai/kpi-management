/** Size of the drawing the loader is made on (the player scales it to whatever size the page asks for). */
export const LOADER_W = 104
export const LOADER_H = 96

export type Rgb = readonly [number, number, number]

const CX = 52
const CY = 52
const WATER_R = 38       // the glass of water
const ARC_R = 44         // the arc over it, a little wider
const ARC_W = 5
const LOOP = 240         // frames: 4 s at 60 fps
const K = 0.5523         // circle from four Bezier curves
const HALF_COS = 0.3642  // handle length that makes a Bezier between two extremes follow a sine wave

const solid = (value: number | number[]) => ({ a: 0, k: value })
const ease = { i: { x: 0.45, y: 1 }, o: { x: 0.55, y: 0 } }
const linear = { i: { x: 1, y: 1 }, o: { x: 0, y: 0 } }

/** The colour of the lighter back wave: the water colour with a third of white in it. */
export const lighten = ([r, g, b]: Rgb, by = 0.38): Rgb => [r + (1 - r) * by, g + (1 - g) * by, b + (1 - b) * by]

type Point = [number, number]
const bezier = (v: Point[], i: Point[], o: Point[], closed: boolean) => ({ a: 0, k: { i, o, v, c: closed } })

/** A wavy surface that runs from x0 to x1 and is filled down to `bottom`; it repeats every `lambda`, so sliding it by one lambda loops without a jump. */
function wavePath(x0: number, x1: number, lambda: number, amp: number, surface: number, bottom: number) {
  const half = lambda / 2
  const v: Point[] = [], i: Point[] = [], o: Point[] = []
  for (let k = 0; x0 + k * half <= x1 + half; k++) {
    v.push([x0 + k * half, surface + (k % 2 === 0 ? -amp : amp)])
    i.push(k === 0 ? [0, 0] : [-HALF_COS * half, 0])
    o.push([HALF_COS * half, 0])
  }
  o[o.length - 1] = [0, 0]
  const last = v[v.length - 1][0]
  v.push([last, bottom], [x0, bottom])
  i.push([0, 0], [0, 0])
  o.push([0, 0], [0, 0])
  return bezier(v, i, o, true)
}

const fill = (rgb: Rgb, opacity = 100) => ({ ty: 'fl', c: solid([rgb[0], rgb[1], rgb[2], 1]), o: solid(opacity), r: 1, nm: 'Fill' })

const transform = (extra: object = {}) => ({
  ty: 'tr', p: solid([0, 0]), a: solid([0, 0]), s: solid([100, 100]), r: solid(0), o: solid(100), sk: solid(0), sa: solid(0), ...extra,
})

/** One wave: it slides sideways `slide` pixels per loop (the inner group) while the water level rises and falls (the outer group). */
function water(rgb: Rgb, lambda: number, amp: number, surface: number, slide: number, rise: number, name: string) {
  const bottom = CY + WATER_R + 24
  const waves = {
    ty: 'gr', nm: name + ' wave',
    it: [
      { ty: 'sh', nm: 'Surface', ks: wavePath(-20, 240, lambda, amp, surface, bottom) },
      fill(rgb),
      transform({ p: { a: 1, k: [{ t: 0, s: [0, 0], ...linear }, { t: LOOP, s: [-slide, 0] }] } }),
    ],
  }
  return {
    ty: 'gr', nm: name + ' level',
    it: [
      waves,
      transform({ p: { a: 1, k: [{ t: 0, s: [0, 0], ...ease }, { t: LOOP / 2, s: [0, -rise], ...ease }, { t: LOOP, s: [0, 0] }] } }),
    ],
  }
}

const circleMask = () => {
  const k = K * WATER_R
  return [{
    inv: false, mode: 'a', nm: 'Glass', o: solid(100), x: solid(0),
    pt: bezier(
      [[CX, CY - WATER_R], [CX + WATER_R, CY], [CX, CY + WATER_R], [CX - WATER_R, CY]],
      [[-k, 0], [0, -k], [k, 0], [0, k]],
      [[k, 0], [0, k], [-k, 0], [0, -k]],
      true,
    ),
  }]
}

const layer = (index: number, name: string, shape: object, masks?: object[]) => ({
  ddd: 0, ind: index, ty: 4, nm: name, sr: 1,
  ks: { o: solid(100), r: solid(0), p: solid([0, 0, 0]), a: solid([0, 0, 0]), s: solid([100, 100, 100]) },
  ao: 0, shapes: [shape], ip: 0, op: LOOP, st: 0, bm: 0,
  ...(masks ? { hasMask: true, masksProperties: masks } : {}),
})

/** The arc over the water: the top half of a circle, drawn as a line with square ends at the level of the glass's middle. */
function arc(ink: Rgb) {
  const k = K * ARC_R
  return {
    ty: 'gr', nm: 'Arc',
    it: [
      {
        ty: 'sh', nm: 'Half circle',
        ks: bezier([[CX - ARC_R, CY], [CX, CY - ARC_R], [CX + ARC_R, CY]], [[0, 0], [-k, 0], [0, -k]], [[0, -k], [k, 0], [0, 0]], false),
      },
      { ty: 'st', c: solid([ink[0], ink[1], ink[2], 1]), o: solid(100), w: solid(ARC_W), lc: 1, lj: 1, ml: 4, nm: 'Line' },
      transform(),
    ],
  }
}

/**
 * A Lottie animation of a glass of water: an arc over a circle whose lower part is water with waves that keep rolling while the level slowly
 * rises and falls. Drawn here, not downloaded, so it works without internet and takes the page's colours (0..1 RGB).
 */
export function loadingAnimation(water1: Rgb, ink: Rgb): string {
  const front = water(water1, 60, 5, CY + 6, 120, 16, 'Front')
  const back = water(lighten(water1), 84, 5, CY - 1, 84, 14, 'Back')
  return JSON.stringify({
    v: '5.7.0', fr: 60, ip: 0, op: LOOP, w: LOADER_W, h: LOADER_H, nm: 'loader', ddd: 0, assets: [],
    layers: [
      layer(1, 'Arc', arc(ink)),
      layer(2, 'Front water', front, circleMask()),
      layer(3, 'Back water', back, circleMask()),
    ],
  })
}
