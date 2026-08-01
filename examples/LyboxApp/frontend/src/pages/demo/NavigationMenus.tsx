import { defineComponent, ref, onMounted } from 'vue'
import { Lybox } from '../../api'
import { t } from '../../i18n'

export default defineComponent({
  name: 'NavigationMenus',
  setup() {
    const tree = ref<any[]>([])

    onMounted(async () => {
      tree.value = await Lybox.getMenuTree()
    })

    const renderNode = (node: any) => (
      <li>
        <div class="font-medium">{node.label}</div>
        {node.children && node.children.length ? (
          <ul class="ml-4 mt-1 list-disc">{node.children.map((c: any) => renderNode(c))}</ul>
        ) : null}
      </li>
    )

    return () => (
      <div class="space-y-4">
        <h1 class="text-2xl font-bold">{t('demo.navmenus.title')}</h1>
        <p class="text-slate-500">{t('demo.navmenus.desc')}</p>
        <ul class="space-y-2 rounded-xl bg-white p-4 shadow dark:bg-slate-800">
          {tree.value.map((n) => renderNode(n))}
        </ul>
      </div>
    )
  },
})
