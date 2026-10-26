/** Turns any CSS colour (a hex code, rgb(), oklch()...) into 0..1 red, green and blue by letting the browser paint one pixel with it. */
export function cssColorRgb(value: string, fallback: string): [number, number, number] {
  const canvas = document.createElement('canvas')
  canvas.width = canvas.height = 1
  const context = canvas.getContext('2d', { willReadFrequently: true })
  if (!context) return [0.15, 0.4, 0.85]
  context.fillStyle = fallback
  context.fillStyle = value || fallback        // a value the browser does not understand leaves the fallback in place
  context.fillRect(0, 0, 1, 1)
  const [r, g, b] = context.getImageData(0, 0, 1, 1).data
  return [r / 255, g / 255, b / 255]
}

/** The current value of a CSS custom property of the page (already resolved from var(...)), or '' when it is not set. */
export function cssVariable(name: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim()
}
