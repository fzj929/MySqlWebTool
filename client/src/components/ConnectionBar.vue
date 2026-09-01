<script setup>
import { ref, watch } from 'vue'
import { ElMessageBox } from 'element-plus'
import { notifyError, notifyInfo, notifySuccess } from '../utils/notify'
import {
  api,
  loadSavedConnections,
  persistConnection,
  forgetConnection,
} from '../api'

const props = defineProps({
  connection: { type: Object, required: true },
  databases: { type: Array, default: () => [] },
  connected: { type: Boolean, default: false },
})

const emit = defineEmits(['connected', 'disconnected'])

const saved = ref(loadSavedConnections())
const savedId = ref('')
const remember = ref(false)
const loading = ref('')
// 连接错误就地展示（右下角通知 + 连接栏内联提示），避免弹框遮挡账号密码输入框
const errorMessage = ref('')

watch(savedId, (id) => {
  const item = saved.value.find((x) => x.id === id)
  if (!item) return
  Object.assign(props.connection, {
    host: item.host,
    port: item.port,
    user: item.user,
    password: item.password || '',
    database: item.database || '',
    useSsl: !!item.useSsl,
  })
})

async function onTest() {
  loading.value = 'test'
  errorMessage.value = ''
  try {
    const result = await api.testConnection(props.connection)
    notifySuccess(`MySQL ${result.serverVersion} · ${result.elapsedMs} ms`, '连接成功')
  } catch (e) {
    errorMessage.value = e.message
    notifyError(e.message, '连接失败')
  } finally {
    loading.value = ''
  }
}

async function onConnect() {
  loading.value = 'connect'
  errorMessage.value = ''
  try {
    const result = await api.testConnection(props.connection)
    persistConnection(props.connection, remember.value)
    saved.value = loadSavedConnections()
    emit('connected', result)
  } catch (e) {
    errorMessage.value = e.message
    notifyError(e.message, '连接失败')
  } finally {
    loading.value = ''
  }
}

function onDisconnect() {
  errorMessage.value = ''
  savedId.value = ''
  emit('disconnected')
  notifyInfo('已断开连接，可修改连接信息后重新连接')
}

async function onForget() {
  if (!savedId.value) return
  try {
    await ElMessageBox.confirm('确定删除这条连接记录？', '提示', { type: 'warning' })
    saved.value = forgetConnection(savedId.value)
    savedId.value = ''
  } catch {
    /* 用户取消 */
  }
}
</script>

<template>
  <div class="conn-bar">
    <div class="conn-title">
      <span class="logo">MySQL</span>
      <span class="hint">Web 管理工具</span>
    </div>

    <el-form :model="connection" inline size="small" class="conn-form">
      <el-form-item label="主机">
        <el-input v-model="connection.host" style="width: 130px" placeholder="127.0.0.1" />
      </el-form-item>
      <el-form-item label="端口">
        <el-input-number
          v-model="connection.port"
          :min="1"
          :max="65535"
          controls-position="right"
          style="width: 110px"
        />
      </el-form-item>
      <el-form-item label="用户">
        <el-input v-model="connection.user" style="width: 110px" />
      </el-form-item>
      <el-form-item label="密码">
        <el-input
          v-model="connection.password"
          type="password"
          show-password
          style="width: 130px"
          placeholder="可为空"
        />
      </el-form-item>
      <el-form-item label="默认库">
        <el-select
          v-model="connection.database"
          filterable
          clearable
          allow-create
          placeholder="（不限）"
          style="width: 150px"
        >
          <el-option v-for="db in databases" :key="db.name" :label="db.name" :value="db.name" />
        </el-select>
      </el-form-item>
      <el-form-item label="SSL">
        <el-switch v-model="connection.useSsl" />
      </el-form-item>

      <el-form-item>
        <el-button type="primary" :loading="loading === 'connect'" @click="onConnect">
          <el-icon><Connection /></el-icon> 连接
        </el-button>
        <el-button :loading="loading === 'test'" @click="onTest">
          <el-icon><Link /></el-icon> 测试
        </el-button>
        <el-button v-if="connected" type="danger" plain @click="onDisconnect">
          <el-icon><SwitchButton /></el-icon> 断开
        </el-button>
      </el-form-item>

      <el-form-item label="历史">
        <el-select v-model="savedId" placeholder="选择已保存连接" style="width: 170px" clearable>
          <el-option
            v-for="item in saved"
            :key="item.id"
            :label="`${item.user}@${item.host}:${item.port}`"
            :value="item.id"
          />
        </el-select>
      </el-form-item>
      <el-form-item>
        <el-checkbox v-model="remember">记住密码</el-checkbox>
        <el-button text type="danger" :disabled="!savedId" @click="onForget">删除</el-button>
      </el-form-item>

      <el-form-item>
        <el-tag v-if="connected" type="success" effect="dark" size="small">已连接</el-tag>
        <el-tag v-else type="info" size="small">未连接</el-tag>
      </el-form-item>
    </el-form>

    <div v-if="errorMessage" class="conn-error">
      <el-icon><WarningFilled /></el-icon>
      <span class="msg mono">{{ errorMessage }}</span>
      <el-button text size="small" type="danger" @click="errorMessage = ''">关闭</el-button>
    </div>
  </div>
</template>

<style scoped>
.conn-bar {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 8px 16px;
  background: #fff;
  border-bottom: 1px solid var(--border);
  flex-wrap: wrap;
}

.conn-title {
  display: flex;
  align-items: baseline;
  gap: 8px;
}

.logo {
  font-weight: 700;
  font-size: 18px;
  color: var(--brand);
  letter-spacing: 0.5px;
}

.conn-form {
  display: flex;
  flex-wrap: wrap;
  gap: 0 4px;
}

.conn-form :deep(.el-form-item) {
  margin-right: 6px;
  margin-bottom: 4px;
}

.conn-error {
  width: 100%;
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 10px;
  margin-top: 2px;
  background: #fef0f0;
  border: 1px solid #fde2e2;
  border-radius: 6px;
  color: #f56c6c;
  font-size: 12.5px;
}

.conn-error .msg {
  flex: 1;
  word-break: break-all;
}
</style>
