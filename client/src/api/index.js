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
  connectionString: connection => http.post('/connection/string', connection),
  testConnectionString: (connection, connectionString) => http.post('/connection/string/test', { connection, connectionString }),
  testConnection: (connection) => http.post('/connection/test', connection),
  databases: (connection) => http.post('/schema/databases', connection),
  createDatabase: (connection, name) => http.post('/schema/databases/create', { connection, name }),
  schemas: (connection, database) => http.post('/schema/schemas', { connection, database }),
  tables: (connection, database, schema = '') => http.post('/schema/tables', { connection, database, schema }),
  tableSchema: (connection, database, table, schema = '') =>
    http.post('/schema/table', { connection, database, schema, table }),
  query: (connection, sql, limit) => http.post('/query', { connection, sql, limit }),
  tableData: (connection, payload) => http.post('/query/data', { connection, ...payload }),
  sqliteFiles: () => http.get('/sqlite/files'),
  uploadSqlite: file => { const form = new FormData(); form.append('file', file); return http.post('/sqlite/files', form) },
  deleteSqlite: id => http.delete('/sqlite/files/' + encodeURIComponent(id)),
  downloadSqlite: async id => {
    const response = await axios.get('/api/sqlite/files/' + encodeURIComponent(id) + '/download', { responseType: 'blob' })
    const url = URL.createObjectURL(response.data)
    const link = document.createElement('a'); link.href = url; link.download = 'DataPilot-' + id.slice(0,8) + '.db'
    document.body.appendChild(link); link.click(); link.remove(); URL.revokeObjectURL(url)
  },
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

const STORAGE_KEY = 'datapilot:connections'

export function loadSavedConnections() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY) ?? localStorage.getItem('mysql-web-tool:connections')
    const list = raw ? JSON.parse(raw) : []
    return Array.isArray(list) ? list.map(x => ({ ...x, databaseType: x.databaseType || 'mysql' })) : []
  } catch {
    return []
  }
}

export function persistConnection(item, rememberPassword) {
  const list = loadSavedConnections()
  const identity = [item.databaseType || 'mysql', item.host, item.port, item.user, item.databaseType === 'dm8' ? item.schema || '' : item.database, item.fileId || '']
  if (item.databaseType === 'oracle') identity.push(item.oracleConnectionType || 'service', item.schema || '')
  const id = JSON.stringify(identity)
  const existing = list.findIndex(x => x.id === id)

  const record = {
    ...item,
    id,
    host: item.host,
    port: item.port,
    user: item.user,
    database: item.database || '',
    useSsl: !!item.useSsl,
    password: rememberPassword ? item.password || '' : '',
  }

  if (existing >= 0) list.splice(existing, 1)
  list.unshift(record)

  localStorage.setItem(STORAGE_KEY, JSON.stringify(list.slice(0, 20)))
  return list.slice(0, 20)
}

export function forgetConnection(id) {
  const list = loadSavedConnections().filter((x) => x.id !== id)
  localStorage.setItem(STORAGE_KEY, JSON.stringify(list))
  return list
}
