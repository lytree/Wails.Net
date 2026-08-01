import { defineComponent } from 'vue'
import { Lybox } from '../../api'
import { t } from '../../i18n'

export default defineComponent({
  name: 'DialogFeedbacks',
  setup() {
    const show = (type: string) =>
      Lybox.showDialog(
        type,
        t('dialog.' + type),
        '这是来自后端的 ' + type + ' 对话框示例。',
        type === 'confirm' ? t('common.confirm') : undefined,
        type === 'confirm' ? t('common.cancel') : undefined,
      )

    return () => (
      <div class="space-y-4">
        <h1 class="text-2xl font-bold">{t('demo.dialog.title')}</h1>
        <div class="flex flex-wrap gap-2">
          <button class="rounded bg-blue-500 px-3 py-2 text-white" onClick={() => show('info')}>
            {t('demo.dialog.info')}
          </button>
          <button class="rounded bg-green-500 px-3 py-2 text-white" onClick={() => show('success')}>
            {t('demo.dialog.success')}
          </button>
          <button class="rounded bg-amber-500 px-3 py-2 text-white" onClick={() => show('warning')}>
            {t('demo.dialog.warning')}
          </button>
          <button class="rounded bg-red-500 px-3 py-2 text-white" onClick={() => show('error')}>
            {t('demo.dialog.error')}
          </button>
          <button class="rounded bg-indigo-600 px-3 py-2 text-white" onClick={() => show('confirm')}>
            {t('demo.dialog.confirm')}
          </button>
        </div>
      </div>
    )
  },
})
