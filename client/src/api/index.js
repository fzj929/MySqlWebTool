import axios from 'axios'

const http = axios.create({
  baseURL: '/api',
  timeout: 180000,
})

http.interceptors.response.use(
  (response) => response.data,
  (error) => {
    const data = error?.response?.data
    const message = data?.error || data?.title || error.message || '请求失败'
    return Promise.reject(new Error(message))
  },
)

export const api = {
  testConnection: (connection) => http.post('/connection/test', connection),
  databases: (connection) => http.post('/schema/databases', connection),
  tables: (connection, database) => http.post('/schema/tables', { connection, database }),
  tableSchema: (connection, database, table) =>
    http.post('/schema/table', { connection, database, table }),
  query: (connection, sql, limit) => http.post('/query', { connection, sql, limit }),
  tableData: (connection, payload) => http.post('/query/data', { connection, ...payload }),
}

/** 导出查询结果（或整表数据）为 CSV 并触发下载 */
export async function exportCsv(connection, sql, limit) {
  const response = await axios.post(
    '/api/query/export',
    { connection, sql, limit },
    { responseType: 'blob', timeout: 180000 },
  )

  const disposition = response.headers['content-disposition'] || ''
  const matched = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)
  const fileName = matched ? decodeURIComponent(matched[1]) : `export_${Date.now()}.csv`

  const url = URL.createObjectURL(new Blob([response.data], { type: 'text/csv;charset=utf-8' }))
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
  return fileName
}

// ------------------------------------------------------------ 本地连接记录

const STORAGE_KEY = 'mysql-web-tool:connections'

export function loadSavedConnections() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    const list = raw ? JSON.parse(raw) : []
    return Array.isArray(list) ? list : []
  } catch {
    return []
  }
}

export function persistConnection(item, rememberPassword) {
  const list = loadSavedConnections()
  const existing = list.findIndex(
    (x) => x.host === item.host && x.port === item.port && x.user === item.user,
  )

  const record = {
    id: `${item.host}:${item.port}:${item.user}`,
    host: item.host,
    port: item.port,
    user: item.user,
    database: item.database || '',
    useSsl: !!item.useSsl,
    password: rememberPassword ? item.password || '' : '',
  }

  if (existing >= 0) list.splice(existing, 1, record)
  else list.unshift(record)

  localStorage.setItem(STORAGE_KEY, JSON.stringify(list.slice(0, 20)))
  return list
}

export function forgetConnection(id) {
  const list = loadSavedConnections().filter((x) => x.id !== id)
  localStorage.setItem(STORAGE_KEY, JSON.stringify(list))
  return list
}
