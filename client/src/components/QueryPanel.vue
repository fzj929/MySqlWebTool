<script setup>
import { computed, nextTick, ref, watch } from 'vue'
import { notifyError, notifySuccess, notifyWarning } from '../utils/notify'
import { loadSqlHistory, saveSqlHistory, clearSqlHistory } from '../utils/sqlHistory'
import { api, exportCsv } from '../api'
import ResultTable from './ResultTable.vue'

const props = defineProps({
  connection: { type: Object, required: true },
  connected: { type: Boolean, default: false },
})

const sql = ref('')
const sqlEditor = ref(null)
const sqlHistory = ref(loadSqlHistory())
const selectedHistory = ref(null)

async function selectHistory(text) {
  sql.value = text
  await nextTick()
  selectedHistory.value = null
  sqlEditor.value?.focus()
  sqlEditor.value?.setSelectionRange(0, 0)
}

function clearHistory() {
  try {
    clearSqlHistory()
    sqlHistory.value = []
    selectedHistory.value = null
    notifySuccess('本地 SQL 历史已清空')
  } catch {
    notifyError('无法清空本地 SQL 历史，请检查浏览器存储设置')
  }
}
const limit = ref(1000)
const loading = ref(false)
const result = ref(null)
const resultSets = computed(() => result.value?.resultSets?.length
  ? result.value.resultSets : result.value?.isQuery ? [result.value] : [])
const totalRows = computed(() => resultSets.value.reduce((sum, set) => sum + set.rowCount, 0))
const fullscreenVisible = ref(false)
const fullscreenIndex = ref(0)
const fullscreenSet = computed(() => resultSets.value[fullscreenIndex.value])
watch(result, () => { fullscreenVisible.value = false })

function expandResult(index) {
  fullscreenIndex.value = index
  fullscreenVisible.value = true
}
const exporting = ref(false)
let requestGeneration = 0
watch(() => JSON.stringify(props.connection), clearResult)

async function execute() {
  const editor = sqlEditor.value
  const hasSelection = editor && editor.selectionStart !== editor.selectionEnd
  const text = (hasSelection
    ? editor.value.slice(editor.selectionStart, editor.selectionEnd)
    : sql.value).trim()
  if (!text) {
    notifyWarning(hasSelection ? '选中内容为空，请选择要执行的 SQL' : '请输入要执行的 SQL')
    return
  }
  if (!props.connected) {
    notifyWarning('请先连接数据库')
    return
  }

  loading.value = true
  const generation = ++requestGeneration
  try {
    sqlHistory.value = saveSqlHistory(text)
  } catch {
    notifyWarning('无法保存 SQL 历史，请检查浏览器存储空间或设置；SQL 将继续执行')
  }
  try {
    const response = await api.query(props.connection, text, limit.value)
    if (generation === requestGeneration) result.value = response
  } catch (e) {
    if (generation === requestGeneration) { result.value = null; notifyError(e.message) }
  } finally {
    loading.value = false
  }
}

async function onExport() {
  const text = sql.value.trim()
  if (!text) return
  exporting.value = true
  try {
    const file = await exportCsv(props.connection, text, 50000)
    notifySuccess(`已导出：${file}`)
  } catch (e) {
    notifyError(e.message)
  } finally {
    exporting.value = false
  }
}

function clearAll() {
  sql.value = ''
  result.value = null
}

/** 供父组件在选中表时预填查询语句（不覆盖用户已写内容） */
function setSqlIfEmpty(text) {
  if (!sql.value.trim()) sql.value = text
}

function onTabKey(event) {
  event.preventDefault()
  const el = event.target
  const start = el.selectionStart
  const end = el.selectionEnd
  sql.value = sql.value.slice(0, start) + '  ' + sql.value.slice(end)
  requestAnimationFrame(() => {
    el.selectionStart = el.selectionEnd = start + 2
  })
}

function setSql(text) {
  sql.value = text
}

/** 断开连接时清空上一次的查询结果 */
function clearResult() {
  requestGeneration++
  result.value = null
}

defineExpose({ setSql, setSqlIfEmpty, clearResult })
</script>

<template>
  <div class="query-panel">
    <div class="toolbar">
      <el-button type="primary" size="small" :loading="loading" title="Ctrl + Enter · 有选区时执行选中内容，否则执行全部" @click="execute">
        <el-icon><VideoPlay /></el-icon> 执行
      </el-button>
      <el-select v-model="selectedHistory" class="history-select" size="small" filterable
        placeholder="历史 SQL（本地）" aria-label="历史 SQL"
        :title="`最近 ${sqlHistory.length} / 100 条 · 选择后回填编辑器`"
        no-data-text="暂无执行历史" no-match-text="没有匹配的 SQL"
        @visible-change="visible => { if (visible) sqlHistory = loadSqlHistory() }"
        @change="selectHistory">
        <el-option v-for="(text, index) in sqlHistory" :key="index" :value="text" :label="text">
          <span :title="text" class="history-preview mono">{{ text.replace(/\s+/g, ' ') }}</span>
        </el-option>
      </el-select>
      <el-button size="small" :disabled="!sqlHistory.length" @click="clearHistory">清空历史</el-button>
      <el-button size="small" :loading="exporting" :disabled="!sql.trim()" title="执行编辑器全部 SQL 并导出第一个结果集" @click="onExport">
        <el-icon><Download /></el-icon> 导出 CSV
      </el-button>
      <el-button size="small" @click="clearAll">
        <el-icon><Delete /></el-icon> 清空
      </el-button>

      <el-divider direction="vertical" />

      <span class="hint">每个结果集行数上限</span>
      <el-input-number v-model="limit" :min="1" :max="100000" size="small" style="width: 120px" />

      <div class="spacer" />

      <span v-if="result && result.isQuery" class="hint">
        {{ resultSets.length }} 个结果集 · 共 {{ totalRows }} 行 · {{ result.elapsedMs }} ms
      </span>
      <span v-else-if="result" class="hint">
        影响行数 {{ result.rowsAffected }} · {{ result.elapsedMs }} ms
      </span>
    </div>

    <div class="editor-wrap">
      <textarea
        ref="sqlEditor"
        v-model="sql"
        class="sql-editor mono"
        spellcheck="false"
        placeholder="输入当前数据库支持的 SQL，或从表结构面板发送 DDL。执行后按顺序显示全部结果集。"
        @keydown.ctrl.enter.prevent="execute"
        @keydown.tab="onTabKey"
      />
    </div>

    <div class="result-wrap">
      <el-alert
        v-if="result && !result.isQuery"
        type="success"
        :closable="false"
        show-icon
        :title="`执行成功，影响行数 ${result.rowsAffected}`"
        style="margin-bottom: 8px"
      />
      <div v-if="resultSets.length" class="result-sets" :class="{ multiple: resultSets.length > 1 }">
        <section v-for="(set, index) in resultSets" :key="index" class="result-set" :aria-label="`结果集 ${index + 1}`">
          <div class="result-set-heading">
            <b>结果集 {{ index + 1 }}</b>
            <span class="hint">{{ set.rowCount }} 行</span>
            <el-tag v-if="set.truncated" type="warning" size="small">已达到行数上限，结果已截断</el-tag>
            <div class="spacer" />
            <el-button size="small" circle :aria-label="`全屏查看结果集 ${index + 1}`"
              title="全屏查看" @click="expandResult(index)">
              <el-icon><FullScreen /></el-icon>
            </el-button>
          </div>
          <ResultTable :columns="set.columns" :rows="set.rows" :column-types="set.columnTypes"
            :loading="loading" :height="resultSets.length > 1 ? Math.min(320, Math.max(100, 42 + set.rowCount * 32)) : '100%'" />
        </section>
      </div>
      <div v-else-if="!result" class="placeholder hint">
        执行查询后，结果将显示在这里
      </div>
    </div>

    <el-dialog v-model="fullscreenVisible" class="query-result-fullscreen" fullscreen append-to-body destroy-on-close
      :title="`结果集 ${fullscreenIndex + 1} · ${fullscreenSet?.rowCount ?? 0} 行`"
      :close-on-click-modal="false" :close-on-press-escape="true">
      <template v-if="fullscreenSet">
        <div class="fullscreen-result-info">
          <span class="hint">按 Esc 或点击右上角关闭，返回查询页面</span>
          <el-tag v-if="fullscreenSet.truncated" type="warning" size="small">已达到行数上限，结果已截断</el-tag>
        </div>
        <div class="fullscreen-result-table">
          <ResultTable :columns="fullscreenSet.columns" :rows="fullscreenSet.rows"
            :column-types="fullscreenSet.columnTypes" :loading="loading" height="100%" />
        </div>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.query-panel {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
}

.editor-wrap {
  padding: 8px 12px;
}

.toolbar {
  flex-wrap: nowrap;
  flex-shrink: 0;
  overflow-x: auto;
  white-space: nowrap;
}

.toolbar > :deep(*) {
  flex-shrink: 0;
}

.toolbar :deep(.el-button + .el-button) {
  margin-left: 0;
}

.history-select {
  width: 220px;
}

.history-preview {
  display: block;
  max-width: 580px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.sql-editor {
  width: 100%;
  height: 192px;
  resize: vertical;
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 10px 12px;
  font-size: 13px;
  line-height: 1.6;
  outline: none;
  background: #fbfcfe;
}

.sql-editor:focus {
  border-color: var(--brand);
  background: #fff;
}

.result-wrap {
  flex: 1;
  min-height: 0;
  padding: 0 12px 12px;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

.placeholder {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  border: 1px dashed var(--border);
  border-radius: 6px;
}

.result-sets {
  flex: 1;
  min-height: 0;
  overflow: auto;
}

.result-set {
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.multiple .result-set {
  height: auto;
  margin-bottom: 16px;
}

.result-set-heading {
  display: flex;
  align-items: center;
  flex-shrink: 0;
  gap: 12px;
  padding: 8px 0;
  font-size: 13px;
}

.spacer {
  flex: 1;
}

:global(.query-result-fullscreen.el-dialog) {
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

:global(.query-result-fullscreen .el-dialog__body) {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.fullscreen-result-info {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  padding-bottom: 12px;
}

.fullscreen-result-table {
  flex: 1;
  min-height: 0;
}
</style>
