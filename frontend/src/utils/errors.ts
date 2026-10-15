import axios from 'axios'

/** Best human-readable message for a failed API call. */
export function apiError(error: unknown, fallback = 'Something went wrong. Please try again.'): string {
  if (axios.isAxiosError(error)) {
    if (!error.response) return 'Cannot reach the server. It may be waking up — please try again in a moment.'
    const data = error.response.data as { message?: string; detail?: string; title?: string } | undefined
    return data?.message ?? data?.detail ?? data?.title ?? fallback
  }
  return fallback
}
