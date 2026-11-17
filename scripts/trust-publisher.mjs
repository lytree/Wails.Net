// 批量给 41 个前端包绑定 npm Trusted Publisher（GitHub Actions / OIDC）。
//
// ⚠ 必须在**你自己的交互终端**里跑，不能进 CI、不能由智能体代理：
//    npm trust 不接受 bypass-2FA 的 granular token，第一次请求会打开浏览器要求 2FA；
//    在 2FA 页面勾选「Skip two-factor authentication for the next 5 minutes」后，
//    后续 41 个包可在窗口内一次性配完（官方口径约可配 80 个 / 5 分钟）。
//
// 用法：
//   node scripts/trust-publisher.mjs                      # 默认：lytree/Wails.Net + ci.yml + 允许 publish
//   node scripts/trust-publisher.mjs --action=stage       # 只允许 npm stage publish（最高安全，需人工审批）
//   node scripts/trust-publisher.mjs --action=both        # 两种都允许
//   node scripts/trust-publisher.mjs --dry-run            # 只打印将执行的命令
//   node scripts/trust-publisher.mjs --only=@wailsnet/runtime
//
// 前置条件（npm 侧硬性要求）：
//   - npm CLI >= 11.15.0（npm trust 子命令）
//   - 包已存在于 registry（41 个包已于 2026-11-17 首发）
//   - 若包上已有 trust 配置，必须先 revoke，registry 目前每包只允许一条配置
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(__dirname, "..");
const packagesDir = path.join(root, "packages");

const arg = (name, fallback) => {
  const hit = process.argv.find((a) => a.startsWith(`--${name}=`));
  return hit ? hit.slice(name.length + 3) : fallback;
};
const repo = arg("repo", "lytree/Wails.Net");
const file = arg("file", "ci.yml");
const environment = arg("env", null);
const action = arg("action", "publish"); // publish | stage | both
const dryRun = process.argv.includes("--dry-run");
const only = arg("only", null);
// 必须显式指定官方 registry：本机 npm 全局 registry 常被配成 npmmirror 镜像，
// trust 请求打到镜像会 404（镜像不支持 /-/package/<pkg>/trust 端点）
const registry = "https://registry.npmjs.org";

const ACTION_FLAGS = {
  publish: ["--allow-publish"],
  stage: ["--allow-stage-publish"],
  both: ["--allow-publish", "--allow-stage-publish"],
};
if (!ACTION_FLAGS[action]) {
  console.error(`--action 只能是 publish / stage / both，收到 ${action}`);
  process.exit(1);
}

// 收集待配置包
const packages = [];
for (const dir of fs.readdirSync(packagesDir).sort()) {
  const pkgJson = path.join(packagesDir, dir, "package.json");
  if (!fs.existsSync(pkgJson)) continue;
  const pkg = JSON.parse(fs.readFileSync(pkgJson, "utf8"));
  if (pkg.private || !pkg.name.startsWith("@wailsnet/")) continue;
  if (only && pkg.name !== only) continue;
  packages.push(pkg.name);
}

// repository.url 必须与 GitHub 仓库一致，否则发布侧校验会失败
const mismatch = packages.filter((name) => {
  const dir = name.replace("@wailsnet/", "");
  const pkg = JSON.parse(
    fs.readFileSync(path.join(packagesDir, `wails-net-${dir}`, "package.json"), "utf8"),
  );
  return !pkg.repository?.url?.includes(`github.com/${repo}`);
});
if (mismatch.length) {
  console.warn(`!! 以下包的 repository.url 与 ${repo} 不一致，npm 发布校验会失败：`);
  for (const m of mismatch) console.warn(`   - ${m}`);
}

console.log(`将配置 ${packages.length} 个包 → ${repo} · ${file} · action=${action} · registry=${registry}`);
console.log("首次请求会打开浏览器要求 2FA，请勾选“Skip for next 5 minutes”后继续。\n");

const failed = [];
let first = true;
for (const name of packages) {
  const args = [
    "trust",
    "github",
    name,
    "--repo",
    repo,
    "--file",
    file,
    ...ACTION_FLAGS[action],
    "--registry",
    registry,
    "--yes",
  ];
  if (environment) args.push("--env", environment);
  console.log(`==> ${name}`);
  if (dryRun) {
    console.log(`    npm ${args.join(" ")}`);
    continue;
  }
  try {
    execFileSync("npm", args, { cwd: root, stdio: "inherit", shell: true });
  } catch {
    console.error(`  !! ${name} 配置失败`);
    failed.push(name);
  }
  // 官方建议每个调用之间留 2 秒，避免限流
  if (!dryRun) await new Promise((r) => setTimeout(r, 2000));
  if (first) {
    first = false;
    console.log("\n（若 2FA 只在第一个包弹出，后续应可无感通过；若每次都弹，说明未勾选跳过。）\n");
  }
}

if (failed.length) {
  console.error(`\n${failed.length} 个包失败：\n${failed.map((n) => "  - " + n).join("\n")}`);
  console.error("排查：① 包必须已存在；② 每包只能一条 trust 配置（先 npm trust list 再 revoke）；")
  console.error("      ③ 仓库/工作流文件名精确匹配；④ registry 必须是 registry.npmjs.org（镜像不支持该端点）");
  process.exit(1);
}
if (!dryRun) {
  console.log("\n全部配置完成。校验：");
  console.log(`  npm trust list @wailsnet/runtime`);
  console.log("注意：配置创建后须在 2 天内完成一次成功发布才会激活，否则过期需重建。");
}
