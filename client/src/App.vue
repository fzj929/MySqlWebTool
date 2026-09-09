<script setup>
import { reactive, ref } from 'vue'
import { notifyError, notifySuccess } from './utils/notify'
import { api, loadSavedConnections } from './api'
import ConnectionBar from './components/ConnectionBar.vue'
import ObjectTree from './components/ObjectTree.vue'
import QueryPanel from './components/QueryPanel.vue'
import TableDataPanel from './components/TableDataPanel.vue'
import StructurePanel from './components/StructurePanel.vue'
import ConnectionStringPanel from './components/ConnectionStringPanel.vue'
import { defaults, selectSql } from './utils/database'

const connection = reactive({
  ...defaults(),
  host: '127.0.0.1',
  port: 3306,
  user: 'root',
  password: '',
  database: '',
  useSsl: false,
})

const databases = ref([])
const connected = ref(false)
const activeTab = ref('query')
const selected = ref({ database: '', table: '' })
const reloadKey = ref(0)
const queryPanel = ref()
const asideWidth = ref(268)
let connectionGeneration = 0

// 启动时回填最近一次连接（不含密码时仅回填地址与账号）
const saved = loadSavedConnections()
if (saved.length) {
  const last = saved[0]
  Object.assign(connection, defaults(last.databaseType), last)
  connection.host = last.host
  connection.port = last.port
  connection.user = last.user
  connection.password = last.password || ''
  connection.database = last.database || ''
  connection.useSsl = !!last.useSsl
}

async function loadDatabases(generation) {
  try {
    const res = await api.databases(connection)
    if (generation !== connectionGeneration) return false
    databases.value = res.items
    return true
  } catch (e) {
    if (generation !== connectionGeneration) return false
    notifyError(e.message, '获取数据库列表失败')
    databases.value = []
    return false
  }
}

// 先取到库列表，再让对象树进入已连接状态：
// 否则懒加载树挂载时根节点还是空数组，要第二次点连接才会显示
async function onConnected() {
  const generation = ++connectionGeneration
  const ok = await loadDatabases(generation)
  if (generation !== connectionGeneration) return
  connected.value = ok
  reloadKey.value += 1
  if (ok) notifySuccess(`共 ${databases.value.length} 个数据库`, '已连接')
}

// 断开：清空库列表、选中对象与查询结果，对象树回到未连接状态
function handleDisconnected() {
  connectionGeneration++
  connected.value = false
  databases.value = []
  selected.value = { database: '', table: '' }
  reloadKey.value += 1
  queryPanel.value?.clearResult()
  activeTab.value = 'query'
}

function handleSendToEditor(text) {
  queryPanel.value?.setSql(text)
  activeTab.value = 'query'
}

async function onDatabaseCreated() {
  const generation = connectionGeneration
  if (await loadDatabases(generation)) reloadKey.value += 1
}

function onSelectDatabase(name) {
  connection.database = name
  connection.schema = ''
  selected.value = { database: name, table: '' }
}

function onSelectSchema({ database, schema }) {
  connection.database = database
  connection.schema = schema
  selected.value = { database, schema, table: '' }
}

function onSelectTable({ database, schema, table }) {
  connection.database = database
  connection.schema = schema || ''
  selected.value = { database, schema, table }
  activeTab.value = 'data'
  queryPanel.value?.setSqlIfEmpty(selectSql(connection, database, schema, table))
}
</script>

<template>
  <div class="app-shell">
    <ConnectionBar
      :connection="connection"
      :databases="databases"
      :connected="connected"
      @connected="onConnected"
      @disconnected="handleDisconnected"
    />

    <div class="app-body">
      <aside class="aside" :style="{ width: asideWidth + 'px' }">
        <ObjectTree
          :connection="connection"
          :databases="databases"
          :connected="connected"
          :reload-key="reloadKey"
          @select-database="onSelectDatabase"
          @select-schema="onSelectSchema"
          @select-table="onSelectTable"
          @database-created="onDatabaseCreated"
        />
      </aside>

      <main class="main">
        <el-tabs v-model="activeTab" class="main-tabs" type="border-card">
          <el-tab-pane name="query">
            <template #label>
              <span><el-icon><EditPen /></el-icon> SQL 查询</span>
            </template>
            <QueryPanel ref="queryPanel" :connection="connection" :connected="connected" />
          </el-tab-pane>

          <el-tab-pane name="data">
            <template #label>
              <span><el-icon><Grid /></el-icon> 数据浏览</span>
            </template>
            <TableDataPanel
              :key="JSON.stringify([reloadKey, selected.database, selected.schema, selected.table])"
              :connection="connection"
              :database="selected.database"
              :object-schema="selected.schema || ''"
              :table="selected.table"
              :connected="connected"
            />
          </el-tab-pane>

          <el-tab-pane name="structure">
            <template #label>
              <span><el-icon><Tickets /></el-icon> 表结构</span>
            </template>
            <StructurePanel
              :key="JSON.stringify([reloadKey, selected.database, selected.schema, selected.table])"
              :connection="connection"
              :database="selected.database"
              :object-schema="selected.schema || ''"
              :table="selected.table"
              :connected="connected"
              @send-to-editor="handleSendToEditor"
            />
          </el-tab-pane>
          <el-tab-pane name="connection-string" label="连接字符串" lazy>
            <ConnectionStringPanel :connection="connection" :active="activeTab === 'connection-string'" />
          </el-tab-pane>
        </el-tabs>

        <footer class="status-bar">
          <span v-if="selected.database">
            当前对象：<b class="mono">{{ selected.database }}{{ selected.schema ? '.' + selected.schema : '' }}{{ selected.table ? '.' + selected.table : '' }}</b>
          </span>
          <span v-else class="hint">未选择对象</span>
          <div class="spacer" />
          <span class="hint">DataPilot · {{ connection.databaseType }} · {{ connection.database || '未选择数据库' }}{{ connection.schema ? ' / ' + connection.schema : '' }}</span>
        </footer>
      </main>
    </div>
  </div>
</template>

<style scoped>
.aside {
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}

.main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  padding: 10px;
  gap: 8px;
}

.main-tabs {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.main-tabs :deep(.el-tabs__content) {
  flex: 1;
  min-height: 0;
  padding: 0;
}

.main-tabs :deep(.el-tab-pane) {
  height: 100%;
  min-height: 0;
}

.status-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 10px;
  background: #fff;
  border: 1px solid var(--border);
  border-radius: 6px;
  font-size: 12px;
}

.spacer {
  flex: 1;
}
</style>
