import type { IssueFormValues } from '../../types/issue'
import { IssueCategory, IssueImpact, IssueModule } from '../../types/issue'

export const emptyIssueForm: IssueFormValues = {
  title: '',
  description: '',
  category: IssueCategory.Bug,
  module: IssueModule.Kpi,
  impact: IssueImpact.Degraded,
}

export const MAX_FILES = 5
export const MAX_FILE_BYTES = 5 * 1024 * 1024
export const IMAGE_TYPES = ['image/png', 'image/jpeg', 'image/gif', 'image/webp']

/** Client-side checks that match what the server enforces, so people hear about mistakes before sending. */
export function validateIssueForm(values: IssueFormValues): Partial<Record<keyof IssueFormValues, string>> {
  const errors: Partial<Record<keyof IssueFormValues, string>> = {}
  if (values.title.trim().length < 3) errors.title = 'กรุณาใส่หัวข้ออย่างน้อย 3 ตัวอักษร'
  if (values.description.trim().length < 10) errors.description = 'กรุณาอธิบายอย่างน้อย 10 ตัวอักษร'
  return errors
}
