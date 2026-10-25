import { SETTINGS_ITEMS, SETTINGS_SECTIONS, type SettingsSectionId } from './settingsNav'

export interface SettingsHit {
  section: SettingsSectionId
  /** what was found: a row name, or the section's own name */
  label: string
  /** the section's name, shown beside the row */
  sectionLabel: string
  anchor?: string
}

/**
 * Case, spacing and Thai tone marks (่ ้ ๊ ๋ and the cancel mark ์) do not matter, so a missed or extra tone mark still
 * finds the row and "Dark" finds "dark".
 */
export function normalize(text: string): string {
  return text.normalize('NFKC').toLowerCase().replace(/[่-์]/g, '').replace(/\s+/g, '')
}

/** Every space-separated word of the query has to appear somewhere in the entry. */
function matches(query: string[], haystack: string): boolean {
  return query.every((q) => haystack.includes(q))
}

/** Rows and sections that match, limited to the sections the person may open. Rows come before the section they live in. */
export function searchSettings(query: string, visible: readonly SettingsSectionId[]): SettingsHit[] {
  const words = query.normalize('NFKC').split(/\s+/).map(normalize).filter(Boolean)
  if (words.length === 0) return []

  const hits: SettingsHit[] = []
  for (const s of SETTINGS_SECTIONS) {
    if (!visible.includes(s.id)) continue
    const own = SETTINGS_ITEMS.filter((i) => i.section === s.id)
    const sectionMatches = matches(words, normalize(`${s.label} ${s.desc}`))
    let rows = 0
    for (const i of own) {
      if (matches(words, normalize(`${i.label} ${i.keywords ?? ''}`))) {
        hits.push({ section: s.id, label: i.label, sectionLabel: s.label, anchor: i.anchor })
        rows++
      }
    }
    // the section itself, unless a row of it already says the same thing
    if (sectionMatches && rows === 0) hits.push({ section: s.id, label: s.label, sectionLabel: s.label })
  }
  return hits
}
