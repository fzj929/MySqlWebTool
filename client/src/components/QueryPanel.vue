<script setup>
import { ref } from 'vue'
import { notifyError, notifyInfo, notifySuccess, notifyWarning } from '../utils/notify'
import { api, exportCsv } from '../api'
import ResultTable from './ResultTable.vue'

const props = defineProps({
  connection: { type: Object, required: true },
  connected: { type: Boolean, default: false },
})

const sql = ref('')
const limit = ref(1000)
const loading = ref(false)
const result = ref(null)
const exporting = ref(false)

async function execute() {
  const text = sql.value.trim()
  if (!text) {
    notifyWarning('请输入要执行的 SQL')
    return
  }
  if (!props.connected) {
    notifyWarning('请先连接数据库')
    return
  }

  loading.value = true
  try {
    result.value = await api.query(props.connection, text, limit.value)
  } catch (e) {
    result.value = null
    notifyError(e.message)
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
  result.value = null
}

defineExpose({ setSql, setSqlIfEmpty, clearResult })
</script>

<template>
  <div class="query-panel">
    <div class="toolbar">
      <el-button type="primary" size="small" :loading="loading" @click="execute">
        <el-icon><VideoPlay /></el-icon> 执行
      </el-button>
      <span class="hint">Ctrl + Enter</span>
      <el-button size="small" :loading="exporting" :disabled="!sql.trim()" @click="onExport">
        <el-icon><Download /></el-icon> 导出 CSV
      </el-button>
      <el-button size="small" @click="clearAll">
        <el-icon><Delete /></el-icon> 清空
      </el-button>

      <el-divider direction="vertical" />

      <span class="hint">最大返回行数</span>
      <el-input-number v-model="limit" :min="1" :max="100000" size="small" style="width: 120px" />

      <div class="spacer" />

      <span v-if="result && result.isQuery" class="hint">
        返回 {{ result.rowCount }} 行 · {{ result.elapsedMs }} ms
        <el-tag v-if="result.truncated" type="warning" size="small" style="margin-left: 6px">
          已被截断
        </el-tag>
      </span>
      <span v-else-if="result" class="hint">
        影响行数 {{ result.rowsAffected }} · {{ result.elapsedMs }} ms
      </span>
    </div>

    <div class="editor-wrap">
      <textarea
        v-model="sql"
        class="sql-editor mono"
        spellcheck="false"
        placeholder="在此输入 SQL，例如：SELECT * FROM `mysql`.`user` LIMIT 100;"
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
      <ResultTable
        v-if="result && result.isQuery"
        :columns="result.columns"
        :rows="result.rows"
        :column-types="result.columnTypes"
        :loading="loading"
        max-height="100%"
      />
      <div v-else-if="!result" class="placeholder hint">
        执行查询后，结果将显示在这里
      </div>
    </div>
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

.sql-editor {
  width: 100%;
  height: 160px;
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

.spacer {
  flex: 1;
}
</style>
