const STORAGE_KEY = 'datapilot:sql-history'
const MAX_HISTORY = 100

export function loadSqlHistory() {
  try {
    const items = JSON.parse(localStorage.getItem(STORAGE_KEY) || '[]')
    return Array.isArray(items)
      ? [...new Set(items.filter(item => typeof item === 'string' && item.trim()))].slice(0, MAX_HISTORY)
      : []
  } catch {
    return []
  }
}

export function saveSqlHistory(text) {
  const items = [text, ...loadSqlHistory().filter(item => item !== text)].slice(0, MAX_HISTORY)
  localStorage.setItem(STORAGE_KEY, JSON.stringify(items))
  return items
}

export function clearSqlHistory() {
  localStorage.removeItem(STORAGE_KEY)
}
