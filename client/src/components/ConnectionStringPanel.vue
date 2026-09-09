<script setup>
import { computed, ref, watch } from 'vue'
import { api } from '../api'
import { engines } from '../utils/database'
const props = defineProps({ connection: Object, active: Boolean })
const draft = ref(''), baseline = ref(''), visible = ref(false), loading = ref(false), testing = ref(false)
const error = ref(''), result = ref(null)
const engine = computed(() => engines.find(x => x.value === props.connection.databaseType)?.label || '')
let generation = 0, loadedKey = ''
async function load() {
  const token = ++generation
  loading.value = true; testing.value = false; error.value = ''; result.value = null
  try {
    const response = await api.connectionString({ ...props.connection })
    if (token !== generation) return
    baseline.value = response.connectionString; draft.value = baseline.value
    loadedKey = JSON.stringify(props.connection)
  } catch { if (token === generation) error.value = '请先在顶部填写完整连接参数，或选择已上传的 SQLite 文件。' }
  finally { if (token === generation) loading.value = false }
}
watch(() => JSON.stringify(props.connection), () => {
  generation++; loadedKey = ''; draft.value = ''; baseline.value = ''; visible.value = false
  result.value = null; error.value = ''; testing.value = false; loading.value = false
  if (props.active) load()
})
watch(() => props.active, active => { if (active && loadedKey !== JSON.stringify(props.connection)) load() }, { immediate: true })
function edited() { generation++; result.value = null; error.value = ''; testing.value = false }
async function test() {
  const token = ++generation
  testing.value = true; error.value = ''; result.value = null
  try {
    const response = await api.testConnectionString({ ...props.connection }, draft.value)
    if (token === generation) result.value = response
  } catch (e) { if (token === generation) error.value = e.message || '连接测试失败' }
  finally { if (token === generation) testing.value = false }
}
</script>

<template>
  <section class="connection-string-panel" v-loading="loading">
    <div class="panel-heading"><div><h3>{{ engine }} 连接字符串</h3><p>根据顶部当前连接参数生成；编辑和测试不会改变正在使用的数据库连接。</p></div><el-tag size="small" :type="draft === baseline ? 'info' : 'warning'">{{ draft === baseline ? '默认参数' : '已编辑' }}</el-tag></div>
    <div class="actions">
      <el-button type="primary" :loading="testing" :disabled="loading || !draft" @click="test">测试连接字符串</el-button>
      <el-button :disabled="loading || testing" @click="load">恢复默认连接字符串</el-button>
      <el-checkbox v-model="visible">显示明文（含密码）</el-checkbox>
    </div>
    <label for="connection-string-editor" class="editor-label">连接字符串</label>
    <el-input v-if="visible" id="connection-string-editor" v-model="draft" @input="edited" type="textarea" :rows="8" :disabled="loading || testing" spellcheck="false" aria-label="连接字符串编辑器" />
    <el-input v-else id="connection-string-editor" v-model="draft" @input="edited" type="password" :disabled="loading || testing" autocomplete="new-password" aria-label="连接字符串编辑器" />
    <p class="hint">字符串可能含账号密码，默认隐藏且不会另行保存到浏览器或服务器。修改顶部参数后会重新生成默认值。仅支持默认字符串中的字段；连接池和测试超时受服务器限制。</p>
    <p v-if="connection.databaseType === 'oracle'" class="hint">Oracle 的 Schema 通过连接后的 ALTER SESSION 设置，不属于连接字符串；这里仅测试原始连接及 SELECT 1。</p>
    <p v-if="connection.databaseType === 'sqlite'" class="hint">SQLite 文件路径和访问模式受保护，请在顶部选择数据库文件或切换只读模式。</p>
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <el-alert v-if="result" :title="'连接正常 · ' + result.serverVersion + ' · ' + result.elapsedMs + ' ms'" type="success" :closable="false" show-icon />
  </section>
</template>

<style scoped>
.connection-string-panel{height:100%;overflow:auto;padding:24px;box-sizing:border-box;max-width:1200px}
.panel-heading{display:flex;align-items:center;justify-content:space-between;gap:20px;margin-bottom:20px}h3{margin:0 0 8px;color:#173b37;font-size:18px}.panel-heading p{margin:0;color:#788681;font-size:13px}
.actions{display:flex;align-items:center;gap:12px;flex-wrap:wrap;margin-bottom:20px}.actions .el-button+.el-button{margin-left:0}.editor-label{display:block;margin-bottom:8px;font-size:13px;font-weight:600;color:#45665d}
:deep(.el-textarea__inner),:deep(.el-input__inner){font-family:Consolas,monospace;line-height:1.7}.hint{font-size:12px;line-height:1.8;color:#788681}.el-alert{margin-top:16px}
</style>
