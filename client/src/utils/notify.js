import { ElNotification } from 'element-plus'

/**
 * 统一提示：固定显示在右下角。
 * 顶部居中的 Message 会遮挡连接栏的账号密码输入框，因此全部改用右下角通知。
 */
const BASE = {
  position: 'bottom-right',
  customClass: 'app-notify',
}

export function notifyError(message, title = '操作失败') {
  ElNotification({
    ...BASE,
    type: 'error',
    title,
    message: String(message || '未知错误'),
    duration: 8000,
  })
}

export function notifySuccess(message, title = '成功') {
  ElNotification({
    ...BASE,
    type: 'success',
    title,
    message: String(message || ''),
    duration: 3000,
  })
}

export function notifyInfo(message, title = '提示') {
  ElNotification({
    ...BASE,
    type: 'info',
    title,
    message: String(message || ''),
    duration: 4000,
  })
}

export function notifyWarning(message, title = '注意') {
  ElNotification({
    ...BASE,
    type: 'warning',
    title,
    message: String(message || ''),
    duration: 6000,
  })
}
