<script setup>
import { computed, ref, watch } from 'vue'
import { notifyError } from '../utils/notify'
import { api } from '../api'

const props = defineProps({
  connection: { type: Object, required: true },
  databases: { type: Array, default: () => [] },
  connected: { type: Boolean, default: false },
  reloadKey: { type: Number, default: 0 },
})

const emit = defineEmits(['select-database', 'select-schema', 'select-table'])

const treeRef = ref()
const filterText = ref('')
const cache = ref({})
const treeKey = ref(0)
const loading = ref(false)

const treeProps = { label: 'name', children: 'children', isLeaf: 'leaf' }

const rootNodes = computed(() =>
  props.databases.map((d) => ({
    key: `db:${d.name}`,
    name: d.name,
    type: 'db',
    leaf: false,
    charset: d.charset,
  })),
)

// 刷新信号或库列表变化时重建树，确保懒加载的根节点拿到最新数据
watch(
  () => [props.reloadKey, props.databases.map((d) => d.name).join(',')],
  () => {
    cache.value = {}
    treeKey.value += 1
  },
)

watch(filterText, (value) => {
  treeRef.value?.filter(value)
})

function filterNode(value, data) {
  if (!value) return true
  return data.name.toLowerCase().includes(value.toLowerCase())
}

async function loadNode(node, resolve) {
  if (node.level === 0) {
    resolve(rootNodes.value)
    return
  }

  const data = node.data
  if (!['db', 'schema'].includes(data?.type)) {
    resolve([])
    return
  }

  if (cache.value[data.key]) {
    resolve(cache.value[data.key])
    return
  }

  loading.value = true
  try {
    if (data.type === 'db' && ['sqlserver','postgresql','dm8'].includes(props.connection.databaseType)) {
      const res = await api.schemas(props.connection, data.name)
      const children = res.items.map(s => ({ key: JSON.stringify(['schema',data.name,s.name]), name: s.name, type: 'schema', database: data.name, leaf: false }))
      cache.value[data.key] = children; resolve(children); return
    }
    const database = data.type === 'db' ? data.name : data.database
    const schema = data.type === 'schema' ? data.name : ''
    const res = await api.tables(props.connection, database, schema)
    const children = res.items.map((t) => ({
      key: JSON.stringify(['table', database, schema, t.name]),
      name: t.name,
      type: 'table',
      leaf: true,
      database,
      schema,
      tableType: t.type,
      rows: t.rows,
      sizeMb: t.sizeMb,
    }))
    cache.value[data.key] = children
    resolve(children)
  } catch (e) {
    notifyError(e.message, '加载表失败')
    resolve([])
  } finally {
    loading.value = false
  }
}

function onNodeClick(data) {
  if (data.type === 'db') {
    emit('select-database', data.name)
  } else if (data.type === 'schema') {
    emit('select-schema', { database: data.database, schema: data.name })
  } else if (data.type === 'table') {
    emit('select-table', { database: data.database, schema: data.schema, table: data.name })
  }
}
</script>

<template>
  <div class="tree-panel">
    <div class="tree-head">
      <span class="title">对象浏览器</span>
      <el-input v-model="filterText" size="small" placeholder="搜索库 / 表" clearable />
    </div>

    <div v-loading="loading" class="tree-body">
      <el-tree
        v-if="connected"
        ref="treeRef"
        :key="treeKey"
        :data="[]"
        :props="treeProps"
        :load="loadNode"
        :filter-node-method="filterNode"
        lazy
        node-key="key"
        highlight-current
        @node-click="onNodeClick"
      >
        <template #default="{ data }">
          <span class="node">
            <el-icon v-if="data.type === 'db'" class="icon-db"><Coin /></el-icon>
            <el-icon v-else class="icon-tb"><Grid /></el-icon>
            <span class="node-name">{{ data.name }}</span>
            <el-tag v-if="data.type === 'table' && data.tableType === 'VIEW'" size="small" round>
              视图
            </el-tag>
            <span v-else-if="data.type === 'db'" class="hint">{{ data.charset }}</span>
          </span>
        </template>
      </el-tree>
      <div v-else class="empty hint">请先填写连接信息并点击「连接」</div>
    </div>
  </div>
</template>

<style scoped>
.tree-panel {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: #fff;
  border-right: 1px solid var(--border);
}

.tree-head {
  padding: 10px 12px;
  border-bottom: 1px solid var(--border);
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.title {
  font-weight: 600;
  font-size: 14px;
}

.tree-body {
  flex: 1;
  overflow: auto;
  padding: 6px 4px;
}

.node {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
}

.node-name {
  overflow: hidden;
  text-overflow: ellipsis;
}

.icon-db {
  color: #e6a23c;
}

.icon-tb {
  color: #409eff;
}

.empty {
  padding: 24px 12px;
  text-align: center;
}
</style>
