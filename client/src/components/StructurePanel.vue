<script setup>
import { computed, reactive, ref, watch } from 'vue'
import { notifyError, notifyInfo, notifySuccess, notifyWarning } from '../utils/notify'
import { api } from '../api'
import { defaultDdlOptions, generateCreateTable } from '../utils/ddlGenerator'

const props = defineProps({
  connection: { type: Object, required: true },
  database: { type: String, default: '' },
  objectSchema: { type: String, default: '' },
  table: { type: String, default: '' },
  connected: { type: Boolean, default: false },
})

const emit = defineEmits(['send-to-editor'])

const loading = ref(false)
const schema = ref(null)

// DDL 来源：generated = 由表结构本地生成（可调选项）；server = SHOW CREATE TABLE 原文
const ddlMode = ref('server')
const isMySql = computed(() => (props.connection.databaseType || 'mysql') === 'mysql')
const ddlOptions = reactive({ ...defaultDdlOptions })

const generatedDdl = computed(() => {
  if (!schema.value) return ''
  return generateCreateTable({
    database: props.database,
    table: props.table,
    columns: schema.value.columns || [],
    indexDefinitions: schema.value.indexDefinitions || [],
    meta: schema.value.meta,
    options: ddlOptions,
  })
})

const currentDdl = computed(() =>
  !isMySql.value || ddlMode.value === 'server' ? schema.value?.ddl || '' : generatedDdl.value,
)

async function load() {
  if (!props.connected || !props.database || !props.table) { schema.value = null; return }
  loading.value = true
  try {
    schema.value = await api.tableSchema(props.connection, props.database, props.table, props.objectSchema)
  } catch (e) {
    notifyError(e.message, '加载表结构失败')
    schema.value = null
  } finally {
    loading.value = false
  }
}

watch(() => [props.database, props.objectSchema, props.table, props.connected], load, { immediate: true })

async function copyDdl() {
  const text = currentDdl.value
  if (!text) return
  try {
    await navigator.clipboard.writeText(text)
    notifySuccess('建表语句已复制到剪贴板')
  } catch {
    notifyWarning('当前浏览器不支持自动复制，请手动选中文本框内容复制')
  }
}

function downloadDdl() {
  const text = currentDdl.value
  if (!text) return

  const blob = new Blob([text], { type: 'text/plain;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `${props.table}.sql`
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
  notifySuccess(`已下载 ${props.table}.sql`)
}

function sendToEditor() {
  const text = currentDdl.value
  if (!text) return
  emit('send-to-editor', text)
  notifyInfo('已发送到「SQL 查询」面板，可直接执行')
}

function keyTag(key) {
  if (key === 'PRI') return { type: 'danger', text: '主键' }
  if (key === 'UNI') return { type: 'warning', text: '唯一' }
  if (key === 'MUL') return { type: 'info', text: '索引' }
  return null
}
</script>

<template>
  <div class="struct-panel">
    <template v-if="!database || !table">
      <div class="placeholder hint">请从左侧对象浏览器中选择一张表</div>
    </template>

    <template v-else>
      <div class="toolbar">
        <span class="table-name mono">{{ database }}.{{ table }}</span>
        <el-button size="small" :loading="loading" @click="load">
          <el-icon><Refresh /></el-icon> 刷新
        </el-button>
        <div class="spacer" />
        <span v-if="schema" class="hint">共 {{ schema.columns.length }} 个字段</span>
      </div>

      <div v-loading="loading" class="struct-body">
        <el-card shadow="never" class="block">
          <template #header><span class="block-title">字段</span></template>
          <el-table :data="schema?.columns || []" size="small" border stripe height="320">
            <el-table-column type="index" label="#" width="50" />
            <el-table-column prop="name" label="字段名" min-width="140" show-overflow-tooltip />
            <el-table-column prop="columnType" label="类型" min-width="150" show-overflow-tooltip />
            <el-table-column label="可空" width="80" align="center">
              <template #default="{ row }">
                <el-tag :type="row.nullable ? 'info' : 'success'" size="small">
                  {{ row.nullable ? 'YES' : 'NO' }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="defaultValue" label="默认值" min-width="110" show-overflow-tooltip />
            <el-table-column label="键" width="90" align="center">
              <template #default="{ row }">
                <el-tag v-if="keyTag(row.key)" :type="keyTag(row.key).type" size="small">
                  {{ keyTag(row.key).text }}
                </el-tag>
                <span v-else class="hint">-</span>
              </template>
            </el-table-column>
            <el-table-column prop="extra" label="额外" min-width="110" show-overflow-tooltip />
            <el-table-column prop="comment" label="注释" min-width="160" show-overflow-tooltip />
          </el-table>
        </el-card>

        <el-card shadow="never" class="block">
          <template #header><span class="block-title">索引</span></template>
          <el-table :data="schema?.indexes || []" size="small" border stripe max-height="220">
            <el-table-column prop="name" label="索引名" min-width="160" show-overflow-tooltip />
            <el-table-column prop="column" label="字段" min-width="140" show-overflow-tooltip />
            <el-table-column label="唯一" width="80" align="center">
              <template #default="{ row }">
                <el-tag :type="row.unique ? 'success' : 'info'" size="small">
                  {{ row.unique ? '是' : '否' }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="seq" label="顺序" width="80" align="center" />
            <el-table-column prop="cardinality" label="基数" min-width="100" />
          </el-table>
        </el-card>

        <el-card shadow="never" class="block">
          <template #header>
            <div class="ddl-head">
              <div class="ddl-title">
                <span class="block-title">建表语句（DDL）</span>
                <el-radio-group v-if="isMySql" v-model="ddlMode" size="small">
                  <el-radio-button value="generated">按结构生成</el-radio-button>
                  <el-radio-button value="server">服务端原文</el-radio-button>
                </el-radio-group>
              </div>
              <div class="ddl-actions">
                <el-button size="small" :disabled="!currentDdl" @click="sendToEditor">
                  <el-icon><Promotion /></el-icon> 发送到查询
                </el-button>
                <el-button size="small" :disabled="!currentDdl" @click="downloadDdl">
                  <el-icon><Download /></el-icon> 下载 .sql
                </el-button>
                <el-button size="small" type="primary" :disabled="!currentDdl" @click="copyDdl">
                  <el-icon><CopyDocument /></el-icon> 复制
                </el-button>
              </div>
            </div>
          </template>

          <el-alert v-for="warning in schema?.warnings || []" :key="warning" :title="warning" type="info" :closable="false" />
          <p v-if="!isMySql" class="hint">{{ schema?.ddlSource === 'native' ? '数据库定义' : schema?.ddlSource === 'unavailable' ? '对象定义不可用' : '根据系统目录生成' }}</p>
          <div v-if="isMySql && ddlMode === 'generated'" class="ddl-options">
            <el-checkbox v-model="ddlOptions.ifNotExists">IF NOT EXISTS</el-checkbox>
            <el-checkbox v-model="ddlOptions.dropIfExists">前置 DROP TABLE</el-checkbox>
            <el-checkbox v-model="ddlOptions.includeIndexes">包含索引</el-checkbox>
            <el-checkbox v-model="ddlOptions.includeComments">包含注释</el-checkbox>
            <el-checkbox v-model="ddlOptions.includeAutoIncrement">包含 AUTO_INCREMENT</el-checkbox>
            <el-checkbox v-model="ddlOptions.qualifyName">带库名前缀</el-checkbox>
          </div>

          <pre class="ddl mono">{{ currentDdl || '暂无' }}</pre>
        </el-card>
      </div>
    </template>
  </div>
</template>

<style scoped>
.struct-panel {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
}

.struct-body {
  flex: 1;
  min-height: 0;
  overflow: auto;
  padding: 8px 12px 12px;
}

.block {
  margin-bottom: 12px;
}

.block-title {
  font-weight: 600;
  font-size: 13px;
}

.ddl-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 8px;
}

.ddl-title {
  display: flex;
  align-items: center;
  gap: 12px;
}

.ddl-actions {
  display: flex;
  align-items: center;
  gap: 6px;
}

.ddl-options {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 16px;
  padding: 2px 0 10px;
  font-size: 12.5px;
}

.ddl {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
  font-size: 12.5px;
  line-height: 1.7;
  background: #fbfcfe;
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 12px;
}

.table-name {
  font-weight: 600;
  font-size: 13px;
  color: var(--brand);
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
