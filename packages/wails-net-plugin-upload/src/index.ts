/** M3 runtime 变薄：从 @wails-net/runtime 迁移，按需安装 */
import { call } from "@wails-net/runtime";

export const upload = {
  /** 上传本地文件到目标 URL。 */
  upload: (url: string, filePath: string) => call<boolean>("upload.upload", [url, filePath]),
  /** 上传本地文件（预留进度上报）。 */
  uploadWithProgress: (url: string, filePath: string) =>
    call<boolean>("upload.uploadWithProgress", [url, filePath]),
  /** 从 URL 下载文件到本地路径。 */
  download: (url: string, path: string) => call<boolean>("upload.download", [url, path]),
  /** 从 URL 下载文件（预留进度上报）。 */
  downloadWithProgress: (url: string, path: string) =>
    call<boolean>("upload.downloadWithProgress", [url, path]),
};
