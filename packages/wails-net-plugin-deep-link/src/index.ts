/** M3 runtime 变薄：从 @wailsnet/runtime 迁移，按需安装 */
import { call } from "@wailsnet/runtime";

export const deeplink = {
  getCurrent: () => call<string | null>("deeplink.getCurrent", []),
  register: (scheme: string) => call<void>("deeplink.register", [scheme]),
  unregister: (scheme: string) => call<void>("deeplink.unregister", [scheme]),
};
