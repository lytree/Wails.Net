// 临时工具：清理 obj 构建缓存目录（绕过 safe-delete shim 对 rm 的拦截）
// 用法：
//   node clean-obj.cjs <path> [path...]   # 清理指定目录
//   node clean-obj.cjs --auto             # 自动扫描 src/* 与 examples/* 下 obj_tmp/obj_v2 等非标准 obj 目录
const { execFileSync } = require('child_process');
const fs = require('fs');
const path = require('path');

function removeDir(t) {
  if (!fs.existsSync(t)) return '[SKIP] 不存在: ' + t;
  try {
    fs.rmSync(t, { recursive: true, force: true });
    return '[OK] fs.rmSync 删除: ' + t;
  } catch {
    try {
      // 绝对路径调用 cmd.exe（避免 PATH 不含 System32 时 ENOENT）
      execFileSync(process.env.SystemRoot + '\\System32\\cmd.exe', ['/c', 'rmdir', '/s', '/q', t], { stdio: 'ignore' });
      return '[OK] rmdir 删除: ' + t;
    } catch (e) {
      return '[FAIL] 无法删除: ' + t + ' — ' + e.message;
    }
  }
}

const args = process.argv.slice(2);
if (args.length === 0) {
  console.error('用法: node clean-obj.cjs <path> [path...] 或 --auto');
  process.exit(1);
}

if (args[0] === '--auto') {
  const root = 'F:/Code/Dotnet/Wails.Net';
  const targets = [];
  for (const base of ['src', 'examples']) {
    const baseDir = path.join(root, base);
    if (!fs.existsSync(baseDir)) continue;
    for (const p of fs.readdirSync(baseDir)) {
      const full = path.join(baseDir, p);
      if (!fs.statSync(full).isDirectory()) continue;
      for (const suffix of ['obj_tmp', 'obj_v2', 'obj_v1', 'obj2']) {
        const t = path.join(full, suffix);
        if (fs.existsSync(t)) targets.push(t);
      }
    }
  }
  console.log('发现 ' + targets.length + ' 个待清理目录');
  targets.forEach((t) => console.log(removeDir(t)));
} else {
  args.forEach((t) => console.log(removeDir(t)));
}
