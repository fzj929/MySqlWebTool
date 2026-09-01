<script setup>
import { computed } from 'vue'

const props = defineProps({
  columns: { type: Array, default: () => [] },
  rows: { type: Array, default: () => [] },
  columnTypes: { type: Array, default: () => [] },
  loading: { type: Boolean, default: false },
  maxHeight: { type: String, default: '100%' },
})

const typeMap = computed(() => {
  const map = {}
  props.columnTypes.forEach((c) => {
    map[c.name] = c.dataType
  })
  return map
})

function formatValue(value) {
  if (value === null || value === undefined) return ''
  if (typeof value === 'object') return JSON.stringify(value)
  return String(value)
}

function columnLabel(col) {
  const type = typeMap.value[col]
  return type ? `${col}  ·  ${type.toLowerCase()}` : col
}
</script>

<template>
  <el-table
    v-loading="loading"
    :data="rows"
    border
    stripe
    size="small"
    :max-height="maxHeight"
    highlight-current-row
  >
    <el-table-column
      v-for="(col, index) in columns"
      :key="`${col}-${index}`"
      min-width="150"
      show-overflow-tooltip
    >
      <template #header>
        <span class="col-header">{{ columnLabel(col) }}</span>
      </template>
      <template #default="scope">
        <span v-if="scope.row[index] === null || scope.row[index] === undefined" class="null-value">
          NULL
        </span>
        <span v-else class="mono">{{ formatValue(scope.row[index]) }}</span>
      </template>
    </el-table-column>

    <template #empty>
      <span class="hint">暂无数据</span>
    </template>
  </el-table>
</template>

<style scoped>
.col-header {
  font-weight: 600;
  color: #303133;
}
</style>
