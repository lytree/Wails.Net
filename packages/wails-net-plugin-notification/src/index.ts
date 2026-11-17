/** M3 runtime 变薄：从 @wailsnet/runtime 迁移，按需安装 */
import { call } from "@wailsnet/runtime";
import type { NotificationOptions } from "@wailsnet/runtime";

export const notification = {
  show: (options: NotificationOptions) => call<void>("notification.show", [options]),
  showWithId: (options: NotificationOptions, id?: string) =>
    call<string>("notification.showWithId", [options, id]),
  cancel: (id: string) => call<boolean>("notification.cancel", [id]),
  requestPermission: () => call<boolean>("notification.requestPermission", []),
  isPermissionGranted: () => call<boolean>("notification.isPermissionGranted", []),
  hasPermission: () => call<boolean>("notification.hasPermission", []),
};
