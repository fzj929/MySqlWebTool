<script setup>
import { computed, ref, watch } from 'vue'
import { ElMessageBox } from 'element-plus'
import { notifyError, notifySuccess, notifyWarning } from '../utils/notify'
import { api, loadSavedConnections, persistConnection, forgetConnection } from '../api'
import { defaults, engines } from '../utils/database'
const props = defineProps({ connection: Object, databases: Array, connected: Boolean })
const emit = defineEmits(['connected', 'disconnected'])
const saved = ref(loadSavedConnections()), savedId = ref(props.connection.id || ''), remember = ref(true)
const loading = ref(''), errorMessage = ref(''), sqliteFiles = ref([]), fileInput = ref()
const uploadLimit = ref(104857600)
const engine = computed(() => engines.find(e => e.value === props.connection.databaseType) || engines[0])
const sqlite = computed(() => props.connection.databaseType === 'sqlite')
function disconnect() { emit('disconnected'); errorMessage.value = '' }
function changeEngine(type) { disconnect(); Object.assign(props.connection, defaults(type)); savedId.value = '' }
function selectSaved(id) {
  const item = saved.value.find(x => x.id === id)
  if (item) { disconnect(); Object.assign(props.connection, defaults(item.databaseType), item); remember.value = true }
}
function connectionLabel(item) {
  const type = engines.find(e => e.value === item.databaseType)?.label || 'MySQL'
  const target = item.databaseType === 'sqlite'
    ? (item.fileName || item.fileId?.slice(0,8))
    : item.user + '@' + item.host + ':' + item.port + (item.databaseType === 'dm8' ? (item.schema ? ' / ' + item.schema : '') : (item.database ? ' / ' + item.database : ''))
  const detail = item.databaseType === 'oracle' ? ' [' + (item.oracleConnectionType === 'sid' ? 'SID' : 'Service') + (item.schema ? ' / ' + item.schema : '') + ']' : ''
  return type + ' · ' + target + detail
}
async function refreshFiles() {
  try { const data = await api.sqliteFiles(); sqliteFiles.value = data.items; uploadLimit.value = data.maxUploadBytes }
  catch (e) { notifyError(e.message) }
}
watch(sqlite, value => { if (value) refreshFiles() }, { immediate: true })
async function connect(testOnly) {
  loading.value = testOnly ? 'test' : 'connect'; disconnect()
  try {
    const result = await api.testConnection(props.connection)
    if (testOnly) notifySuccess(engine.value.label + ' ' + result.serverVersion + ' · ' + result.elapsedMs + ' ms')
    else {
      if (remember.value) {
        try {
          saved.value = persistConnection({
            ...props.connection,
            fileName: sqliteFiles.value.find(x => x.id === props.connection.fileId)?.name || props.connection.fileName || '',
          }, true)
          savedId.value = saved.value[0].id
        } catch { notifyWarning('已连接，但浏览器无法保存连接，请检查浏览器存储设置') }
      }
      emit('connected', result)
    }
  } catch (e) { errorMessage.value = e.message; notifyError(e.message, '连接失败') }
  finally { loading.value = '' }
}
async function upload(event) {
  const file = event.target.files?.[0]
  if (!file) return
  if (file.size > uploadLimit.value) { notifyError('文件超过上传大小上限'); event.target.value = ''; return }
  loading.value = 'upload'; disconnect()
  try {
    const item = await api.uploadSqlite(file); await refreshFiles(); props.connection.fileId = item.id
    notifySuccess('已上传 ' + item.name + '，点击连接即可访问')
  } catch (e) { notifyError(e.message) }
  finally { loading.value = ''; event.target.value = '' }
}
async function removeFile() {
  try {
    await ElMessageBox.confirm('将永久删除服务器上的这个数据库文件及其数据，是否继续？', '删除 SQLite 文件', { type: 'warning' })
    await api.deleteSqlite(props.connection.fileId); disconnect(); props.connection.fileId = ''; await refreshFiles()
  } catch (e) { if (e instanceof Error) notifyError(e.message) }
}
async function forget() {
  if (!savedId.value) return
  try { await ElMessageBox.confirm('删除已保存的连接参数和密码？数据库本身不会删除。', '删除连接'); saved.value = forgetConnection(savedId.value); savedId.value = ''; notifySuccess('已删除保存的连接') }
  catch (e) { if (e instanceof Error) notifyError(e.message) }
}
</script>
<template>
  <header class="connection-deck" :style="{ '--engine-color': engine.color }">
    <div class="deck-top">
      <div class="wordmark"><span class="pilot-mark">D<span>↗</span></span><div><strong>DataPilot</strong><small>数据库工作台</small></div></div>
      <div class="engines" role="group" aria-label="数据库类型">
        <button v-for="item in engines" :key="item.value" :class="{ active: connection.databaseType === item.value }"
          :aria-pressed="connection.databaseType === item.value" :disabled="!!loading" @click="changeEngine(item.value)">{{ item.label }}</button>
      </div>
      <el-tag :type="connected ? 'success' : 'info'" size="small">{{ connected ? '已连接' : '未连接' }}</el-tag>
      <div class="history">
        <span class="hint">连接管理</span>
        <el-select v-model="savedId" size="small" placeholder="选择已保存连接" clearable filterable class="saved-select" :disabled="!!loading" @change="selectSaved">
          <el-option v-for="item in saved" :key="item.id" :value="item.id" :label="connectionLabel(item)" />
        </el-select>
        <el-button size="small" text :disabled="!savedId || !!loading" @click="forget">删除连接</el-button>
      </div>
    </div>
    <el-form :model="connection" inline size="small" class="conn-form" :disabled="!!loading">
      <template v-if="!sqlite">
        <el-form-item label="主机"><el-input v-model="connection.host" style="width:150px" @change="disconnect" /></el-form-item>
        <el-form-item label="端口"><el-input-number v-model="connection.port" :min="1" :max="65535" controls-position="right" style="width:100px" @change="disconnect" /></el-form-item>
        <el-form-item label="用户"><el-input v-model="connection.user" style="width:110px" @change="disconnect" /></el-form-item>
        <el-form-item label="密码"><el-input v-model="connection.password" type="password" show-password style="width:140px" @change="disconnect" /></el-form-item>
        <el-form-item v-if="connection.databaseType === 'dm8'" label="模式"><el-input v-model="connection.schema" style="width:135px" placeholder="留空使用默认模式" title="按服务器实际大小写填写模式名，不需要加双引号" @change="disconnect" /></el-form-item>
        <template v-else-if="connection.databaseType === 'oracle'">
          <el-form-item label="连接方式"><el-select v-model="connection.oracleConnectionType" style="width:135px" @change="disconnect"><el-option label="Service Name" value="service" /><el-option label="SID" value="sid" /></el-select></el-form-item>
          <el-form-item :label="connection.oracleConnectionType === 'sid' ? 'SID' : '服务名'"><el-input v-model="connection.database" style="width:135px" placeholder="例如 FREEPDB1" @change="disconnect" /></el-form-item>
          <el-form-item label="模式"><el-input v-model="connection.schema" style="width:135px" placeholder="账号默认模式" @change="disconnect" /></el-form-item>
        </template>
        <el-form-item v-else label="数据库"><el-input v-model="connection.database" style="width:135px" placeholder="默认数据库" @change="disconnect" /></el-form-item>
        <el-form-item v-if="connection.databaseType === 'mysql'" label="SSL"><el-switch v-model="connection.useSsl" @change="disconnect" /></el-form-item>
        <template v-if="connection.databaseType === 'sqlserver'">
          <el-form-item label="加密"><el-switch v-model="connection.encrypt" @change="disconnect" /></el-form-item>
          <el-form-item><el-checkbox v-model="connection.trustServerCertificate" @change="disconnect">信任服务器证书</el-checkbox></el-form-item>
        </template>
        <el-form-item v-if="connection.databaseType === 'postgresql'" label="SSL">
          <el-select v-model="connection.sslMode" style="width:125px" @change="disconnect"><el-option v-for="mode in ['Disable','Prefer','Require','VerifyCA','VerifyFull']" :key="mode" :value="mode" :label="mode" /></el-select>
        </el-form-item>
      </template>
      <template v-else>
        <el-form-item label="数据库文件">
          <el-select v-model="connection.fileId" style="width:260px" placeholder="上传或选择数据库文件" @change="disconnect">
            <el-option v-for="item in sqliteFiles" :key="item.id" :value="item.id" :label="item.name + ' · ' + (item.size/1048576).toFixed(2) + ' MB · ' + item.id.slice(0,6)" />
          </el-select>
        </el-form-item>
        <el-form-item><el-button :loading="loading === 'upload'" :title="'SQLite 3 · 最大 ' + uploadLimit/1048576 + ' MB · 上传关闭数据库后的完整文件或备份快照'" @click="fileInput.click()">上传数据库</el-button><el-button @click="refreshFiles">刷新</el-button></el-form-item>
        <el-form-item><el-checkbox v-model="connection.readOnly" @change="disconnect">只读访问</el-checkbox></el-form-item>
        <el-form-item>
          <el-button :disabled="!connection.fileId" @click="api.downloadSqlite(connection.fileId).catch(e => notifyError(e.message))">下载数据库</el-button>
          <el-button type="danger" text :disabled="!connection.fileId" @click="removeFile">删除文件</el-button>
        </el-form-item>
      </template>
      <el-form-item>
        <el-checkbox v-model="remember" style="margin-right:12px" title="勾选后在此浏览器保存连接参数和密码；取消勾选仅本次不保存，已有记录可通过删除连接移除">记住连接</el-checkbox>
        <el-button type="primary" :loading="loading === 'connect'" @click="connect(false)">连接</el-button>
        <el-button :loading="loading === 'test'" @click="connect(true)">测试</el-button>
        <el-button v-if="connected" @click="disconnect">断开</el-button>
      </el-form-item>
    </el-form>
    <input ref="fileInput" hidden type="file" accept=".db,.sqlite,.sqlite3" @change="upload" />
    <div v-if="errorMessage" class="connection-error">{{ errorMessage }}</div>
  </header>
</template>
<style scoped>
.connection-deck{background:#fff;border-bottom:1px solid var(--border);padding:16px 22px 10px;border-top:3px solid var(--engine-color)}
.deck-top{display:flex;gap:20px;align-items:center;margin-bottom:12px;flex-wrap:wrap}
.wordmark{display:flex;align-items:center;gap:10px;min-width:186px}.wordmark strong{display:block;font:700 24px Georgia,serif;letter-spacing:-1px;color:#173b37}.wordmark small{font-size:10px;letter-spacing:3px;color:#6a7f7a}
.pilot-mark{display:grid;place-items:center;position:relative;width:36px;height:36px;border:2px solid #173b37;font:700 23px Georgia;color:#173b37}.pilot-mark span{position:absolute;right:-6px;top:-9px;background:#fff;color:var(--engine-color);font-size:18px}
.engines{display:flex;gap:4px;background:#f0f4f2;padding:4px;border-radius:6px}.engines button{border:0;background:transparent;padding:8px 16px;color:#61716d;border-radius:4px;cursor:pointer;font-weight:600}.engines button.active{background:var(--engine-color);color:white;box-shadow:0 2px 5px #0002}.engines button:focus-visible{outline:2px solid #173b37;outline-offset:2px}
.conn-form{display:flex;flex-wrap:wrap;gap:0 4px}.conn-form :deep(.el-form-item){margin-bottom:10px;margin-right:12px}
.history{display:flex;gap:10px;align-items:center;margin-left:auto;max-width:100%}.history>.hint{white-space:nowrap}.saved-select{width:clamp(220px,22vw,320px);min-width:0}.connection-error{color:#a12525;background:#fff2ee;padding:8px 12px;margin-top:8px;border-left:3px solid #c45544;font-size:13px}
@media(max-width:800px){.connection-deck{padding:12px}.deck-top{gap:12px}.engines button{padding:8px}.history{flex-wrap:wrap}}
</style>
