import * as signalR from '@microsoft/signalr'
import api from './api'

/** Pushed to everyone who may see the KPI so lists and dashboards can refresh. */
export interface KpiChangedEvent {
  kpiId: string
  name: string
  action: string
  status: number
  statusLabel: string
  trafficLight: string
  progress: number
  departmentId: string
  ownerId: string
  actorName: string | null
  at: string
}

/** Pushed to everyone who may see the issue so queues and detail pages can refresh. */
export interface IssueChangedEvent {
  issueId: string
  number: string
  title: string
  action: string
  status: number
  priority: number
  reporterUserId: string | null
  assigneeId: string | null
  departmentId: string | null
  actorName: string | null
  at: string
}

/** A toast-style message addressed to specific people. */
export interface RealtimeNotification {
  title: string
  message: string
  level: 'info' | 'success' | 'warning' | 'error'
  kpiId: string | null
  issueId: string | null
}

export function createKpiConnection(): signalR.HubConnection {
  return new signalR.HubConnectionBuilder()
    .withUrl(`${api.defaults.baseURL}/hubs/kpi`, {
      // Read on every (re)connect so a refreshed token is picked up.
      accessTokenFactory: () => localStorage.getItem('token') ?? '',
    })
    // Render's free tier sleeps; keep retrying with a growing delay instead of giving up.
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000, 60000])
    .configureLogging(signalR.LogLevel.Warning)
    .build()
}
