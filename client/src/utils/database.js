export const engines = [
  { value: 'mysql', label: 'MySQL', port: 3306, user: 'root', database: '', color: '#00758f' },
  { value: 'sqlserver', label: 'SQL Server', port: 1433, user: 'sa', database: 'master', color: '#b23b3b' },
  { value: 'postgresql', label: 'PostgreSQL', port: 5432, user: 'postgres', database: 'postgres', color: '#336791' },
  { value: 'sqlite', label: 'SQLite', port: 0, user: '', database: 'main', color: '#426d58' },
  { value: 'dm8', label: '达梦 DM8', port: 5236, user: 'SYSDBA', database: '', color: '#a64832' },
]
export function defaults(type = 'mysql') {
  const engine = engines.find(x => x.value === type) || engines[0]
  return { databaseType: engine.value, host: '127.0.0.1', port: engine.port, user: engine.user,
    password: '', database: engine.database, schema: '', fileId: '', readOnly: false,
    useSsl: false, encrypt: true, trustServerCertificate: false, sslMode: 'Prefer' }
}
export function quote(type, name) {
  const s = String(name)
  if (type === 'mysql') return '`' + s.replaceAll('`', '``') + '`'
  if (type === 'sqlserver') return '[' + s.replaceAll(']', ']]') + ']'
  return '"' + s.replaceAll('"', '""') + '"'
}
export function selectSql(connection, database, schema, table, limit = 200, where = '', orderBy = '') {
  const type = connection.databaseType || 'mysql'
  const q = name => quote(type, name)
  const name = type === 'sqlite' ? q(table) : type === 'mysql' ? q(database) + '.' + q(table)
    : (schema || connection.schema ? q(schema || connection.schema) + '.' : type === 'dm8' ? '' : q(type === 'sqlserver' ? 'dbo' : 'public') + '.') + q(table)
  return 'SELECT ' + (type === 'sqlserver' ? 'TOP (' + limit + ') ' : '') + '* FROM ' + name +
    (where ? ' WHERE ' + where : '') + (orderBy ? ' ORDER BY ' + orderBy : '') +
    (type === 'sqlserver' ? '' : ' LIMIT ' + limit) + ';'
}
