import { defineComponent, onMounted } from 'vue'
import {
  nav,
  currentRoute,
  loadNav,
  loadPlugins,
  loadTasks,
  loadSettings,
  setupSubscriptions,
} from './store'
import Sidebar from './components/Sidebar'
import TopBar from './components/TopBar'
import DialogHost from './components/DialogHost'
import TaskTray from './components/TaskTray'
import Dashboard from './pages/Dashboard'
import Plugins from './pages/Plugins'
import Settings from './pages/Settings'
import Tasks from './pages/Tasks'
import Template from './pages/demo/Template'
import ButtonsInputs from './pages/demo/ButtonsInputs'
import DateTime from './pages/demo/DateTime'
import DialogFeedbacks from './pages/demo/DialogFeedbacks'
import Downloader from './pages/demo/Downloader'
import NavigationMenus from './pages/demo/NavigationMenus'

const pages: Record<string, any> = {
  dashboard: Dashboard,
  plugins: Plugins,
  settings: Settings,
  tasks: Tasks,
  'demo-template': Template,
  'demo-buttons': ButtonsInputs,
  'demo-datetime': DateTime,
  'demo-dialog': DialogFeedbacks,
  'demo-downloader': Downloader,
  'demo-navmenus': NavigationMenus,
}

export default defineComponent({
  name: 'App',
  setup() {
    onMounted(async () => {
      await loadSettings()
      await loadNav()
      await loadPlugins()
      await loadTasks()
      setupSubscriptions()
    })

    return () => {
      const Page = pages[currentRoute.value] ?? Dashboard
      const active = nav.value.find((n) => n.key === currentRoute.value)
      return (
        <div class="flex h-screen w-screen overflow-hidden bg-slate-100 text-slate-800 dark:bg-slate-900 dark:text-slate-100">
          <Sidebar />
          <div class="flex flex-1 flex-col">
            <TopBar title={active?.titleKey ?? 'app.title'} />
            <main class="flex-1 overflow-auto p-6">
              <Page />
            </main>
          </div>
          <TaskTray />
          <DialogHost />
        </div>
      )
    }
  },
})
