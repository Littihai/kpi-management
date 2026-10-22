import axios from 'axios'
import { currentServerUrl } from '../lib/server'

// Which server to talk to is decided in lib/server.ts: the address chosen on this device, else the one the company's IT
// set for the Windows app, else VITE_API_URL given at build time, else the local API (dev) or our hosted API.
const API_URL = currentServerUrl()

const api = axios.create({
  baseURL: API_URL,
})

api.interceptors.request.use((config) => {
  const token = localStorage.getItem('token')
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

api.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      localStorage.removeItem('token')
      window.location.href = '/login'
    }
    return Promise.reject(error)
  }
)

export default api