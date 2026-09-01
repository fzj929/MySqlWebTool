const NUMERIC_TYPES = new Set([
  'tinyint',
  'smallint',
  'mediumint',
  'int',
  'integer',
  'bigint',
  'decimal',
  'numeric',
  'float',
  'double',
  'real',
  'bit',
  'year',
])

function quote(identifier) {
  return '`' + String(identifier).replace(/`/g, '``') + '`'
}

function escapeLiteral(value) {
  return String(value).replace(/\\/g, '\\\\').replace(/'/g, "''")
}

function charsetOf(collation) {
  return String(collation || '').split('_')[0] || ''
}

/** 默认值：数值与函数原样输出，其余加引号 */
function formatDefault(raw, dataType) {
  const value = String(raw)
  if (/^NULL$/i.test(value)) return 'NULL'
  if (/^(CURRENT_TIMESTAMP|NOW\(\))(\([0-9]*\))?$/i.test(value)) return value.toUpperCase()

  const base = String(dataType || '').toLowerCase().replace(/\(.*\)$/, '').trim()
  if (NUMERIC_TYPES.has(base)) return value
  return `'${escapeLiteral(value)}'`
}

function buildColumn(column, options) {
  const parts = [quote(column.name), column.columnType]

  if (column.nullable === false) parts.push('NOT NULL')

  if (column.defaultValue !== null && column.defaultValue !== undefined) {
    parts.push(`DEFAULT ${formatDefault(column.defaultValue, column.dataType || column.columnType)}`)
  }

  const extra = String(column.extra || '').toLowerCase()
  if (extra.includes('auto_increment')) parts.push('AUTO_INCREMENT')
  if (extra.includes('on update current_timestamp')) parts.push('ON UPDATE CURRENT_TIMESTAMP')

  if (options.includeComments && column.comment) {
    parts.push(`COMMENT '${escapeLiteral(column.comment)}'`)
  }

  // 只有 STORED/VIRTUAL GENERATED 才是生成列；
  // DEFAULT_GENERATED 只是表达式默认值（如 DEFAULT CURRENT_TIMESTAMP），不算生成列
  let note = ''
  if (/\b(stored|virtual)\s+generated\b/.test(extra)) {
    note = ' /* 生成列：请手动补充 AS (表达式) */'
  } else if (extra.includes('default_generated') && !extra.includes('on update')) {
    note = ' /* 表达式默认值：请手动补充 */'
  }

  return '  ' + parts.join(' ') + note
}

function buildIndex(index) {
  const columns = (index.columns || []).map(quote).join(', ')
  if (index.primary) return `  PRIMARY KEY (${columns})`
  if (index.unique) return `  UNIQUE KEY ${quote(index.name)} (${columns})`
  return `  KEY ${quote(index.name)} (${columns})`
}

export const defaultDdlOptions = {
  ifNotExists: true,
  dropIfExists: false,
  includeIndexes: true,
  includeComments: true,
  includeAutoIncrement: false,
  qualifyName: true,
}

/**
 * 由表结构（字段 + 聚合索引 + 表属性）生成 CREATE TABLE 语句。
 */
export function generateCreateTable({
  database = '',
  table = '',
  columns = [],
  indexDefinitions = [],
  meta = null,
  options = {},
}) {
  const opts = { ...defaultDdlOptions, ...options }
  const name = opts.qualifyName && database ? `${quote(database)}.${quote(table)}` : quote(table)

  const body = columns.map((column) => buildColumn(column, opts))
  if (opts.includeIndexes) indexDefinitions.forEach((index) => body.push(buildIndex(index)))

  const tableOptions = []
  tableOptions.push(`ENGINE=${meta?.engine || 'InnoDB'}`)

  if (opts.includeAutoIncrement && meta?.autoIncrement) {
    tableOptions.push(`AUTO_INCREMENT=${meta.autoIncrement}`)
  }

  const collation = meta?.collation
  if (collation) {
    const charset = charsetOf(collation)
    if (charset) tableOptions.push(`DEFAULT CHARSET=${charset}`)
    tableOptions.push(`COLLATE=${collation}`)
  }

  if (opts.includeComments && meta?.comment) {
    tableOptions.push(`COMMENT='${escapeLiteral(meta.comment)}'`)
  }

  let sql = `CREATE TABLE ${opts.ifNotExists ? 'IF NOT EXISTS ' : ''}${name} (\n`
  sql += body.join(',\n')
  sql += `\n) ${tableOptions.join(' ')};`

  if (opts.dropIfExists) {
    sql = `DROP TABLE IF EXISTS ${name};\n${sql}`
  }

  return sql
}

export function generateDropTable(database = '', table = '', options = {}) {
  const name = options.qualifyName && database ? `${quote(database)}.${quote(table)}` : quote(table)
  return `DROP TABLE IF EXISTS ${name};`
}
