<script setup>
import { ref, watch } from 'vue'
import { notifyError, notifySuccess, notifyWarning } from '../utils/notify'
import { api, exportCsv } from '../api'
import ResultTable from './ResultTable.vue'

const props = defineProps({
  connection: { type: Object, required: true },
  database: { type: String, default: '' },
  table: { type: String, default: '' },
  connected: { type: Boolean, default: false },
})

const rows = ref([])
const columns = ref([])
const columnTypes = ref([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(100)
const where = ref('')
const orderBy = ref('')
const loading = ref(false)
const exporting = ref(false)
const elapsed = ref(0)

async function load() {
  if (!props.database || !props.table) return
  loading.value = true
  try {
    const res = await api.tableData(props.connection, {
      database: props.database,
      table: props.table,
      page: page.value,
      pageSize: pageSize.value,
      where: where.value.trim(),
      orderBy: orderBy.value.trim(),
    })
    columns.value = res.columns
    columnTypes.value = res.columnTypes
    rows.value = res.rows
    total.value = res.total
    elapsed.value = res.elapsedMs
    if (res.rows.length === 0 && res.total > 0 && page.value > 1) {
      page.value = 1
      await load()
    }
  } catch (e) {
    notifyError(e.message)
    rows.value = []
    columns.value = []
    total.value = 0
  } finally {
    loading.value = false
  }
}

watch(
  () => [props.database, props.table],
  () => {
    page.value = 1
    where.value = ''
    orderBy.value = ''
    load()
  },
  { immediate: true },
)

watch([page, pageSize], load)

function refresh() {
  load()
}

async function onExport() {
  exporting.value = true
  try {
    const sql = buildSql(50000)
    const file = await exportCsv(props.connection, sql, 50000)
    notifySuccess(`已导出：${file}`)
  } catch (e) {
    notifyError(e.message)
  } finally {
    exporting.value = false
  }
}

function buildSql(limit) {
  const qualified = `\`${props.database}\`.\`${props.table}\``
  const w = where.value.trim() ? ` WHERE ${where.value.trim()}` : ''
  const o = orderBy.value.trim() ? ` ORDER BY ${orderBy.value.trim()}` : ''
  return `SELECT * FROM ${qualified}${w}${o} LIMIT ${limit}`
}
</script>

<template>
  <div class="data-panel">
    <template v-if="!database || !table">
      <div class="placeholder hint">请从左侧对象浏览器中选择一张表</div>
    </template>

    <template v-else>
      <div class="toolbar">
        <span class="table-name mono">{{ database }}.{{ table }}</span>

        <el-input
          v-model="where"
          size="small"
          placeholder="WHERE 条件（不含 WHERE）"
          style="width: 220px"
          clearable
          @keyup.enter="refresh"
        />
        <el-input
          v-model="orderBy"
          size="small"
          placeholder="ORDER BY（不含 ORDER BY）"
          style="width: 200px"
          clearable
          @keyup.enter="refresh"
        />
        <el-button size="small" :loading="loading" @click="refresh">
          <el-icon><Refresh /></el-icon> 查询
        </el-button>
        <el-button size="small" :loading="exporting" @click="onExport">
          <el-icon><Download /></el-icon> 导出 CSV
        </el-button>

        <div class="spacer" />

        <span class="hint">共 {{ total }} 行 · {{ elapsed }} ms</span>
      </div>

      <div class="table-wrap">
        <ResultTable
          :columns="columns"
          :rows="rows"
          :column-types="columnTypes"
          :loading="loading"
          max-height="100%"
        />
      </div>

      <div class="pager">
        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          :page-sizes="[50, 100, 200, 500, 1000]"
          :total="total"
          layout="total, sizes, prev, pager, next, jumper"
          size="small"
        />
      </div>
    </template>
  </div>
</template>

<style scoped>
.data-panel {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
}

.table-name {
  font-weight: 600;
  font-size: 13px;
  color: var(--brand);
}

.table-wrap {
  flex: 1;
  min-height: 0;
  padding: 8px 12px 0;
  overflow: hidden;
}

.pager {
  padding: 8px 12px;
  display: flex;
  justify-content: flex-end;
}

.placeholder {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  border: 1px dashed var(--border);
  border-radius: 6px;
  margin: 12px;
}

.spacer {
  flex: 1;
}
</style>
