// 前端包发布脚本（npm org: wailsnet）。
//
// 用法：
//   node scripts/publish-npm.mjs             # 只打包，产出 artifacts/npm/*.tgz（供人工上传）
//   node scripts/publish-npm.mjs --publish   # 打包 + 逐个 npm publish 到 registry.npmjs.org
//   node scripts/publish-npm.mjs --staged --publish  # 只推到 npm 暂存队列，人工 2FA 审批后生效
//   node scripts/publish-npm.mjs --publish --dry-run   # 演练，不真正上传
//   node scripts/publish-npm.mjs --publish --otp=123456  # 需要 2FA 时传一次性密码
//   node scripts/publish-npm.mjs --publish --tag=beta    # 强制指定 dist-tag
//
// dist-tag 规则：预发布版本（0.1.0-alpha.1）自动打 `alpha`，避免顶掉 `latest`；
// 正式版本（0.1.0）打 `latest`。
//
// 为什么先 pnpm pack 再 npm publish：
//   1. 插件包依赖 "@wailsnet/runtime": "workspace:*"，npm 不认 workspace: 协议，
//      pnpm pack 会在打包时把它改写成真实版本号；
//   2. npm CLI 对 Trusted Publishing / --provenance 支持最完整，pnpm publish 相对滞后。
//
// 认证：本地发布前需 `npm login`（或 NPM_TOKEN 写入 ~/.npmrc）。
// 目标 registry 固定 registry.npmjs.org —— 本地 .npmrc 指向 npmmirror 镜像，镜像不可发布。
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(__dirname, "..");
const packagesDir = path.join(root, "packages");
const outDir = path.join(root, "artifacts", "npm");
const registry = "https://registry.npmjs.org";

const argv = process.argv.slice(2);
const doPublish = argv.includes("--publish");
const dryRun = argv.includes("--dry-run");
const staged = argv.includes("--staged");
const otpArg = argv.find((a) => a.startsWith("--otp="));
const otp = otpArg ? otpArg.slice("--otp=".length) : process.env.NPM_OTP;
const tagArg = argv.find((a) => a.startsWith("--tag="));
const forcedTag = tagArg ? tagArg.slice("--tag=".length) : null;

/**
 * 推导 dist-tag：预发布版本（0.1.0-alpha.1）默认打 `latest` 会把正式版位顶掉，
 * 因此取预发布标识作为 tag（alpha / beta / rc / next…），正式版才落 `latest`。
 */
function distTagFor(version) {
  if (forcedTag) return forcedTag;
  const m = version.match(/^\d+\.\d+\.\d+-([a-zA-Z]+)/);
  return m ? m[1].toLowerCase() : "latest";
}

/** 读取需要发布的前端包（packages/*，跳过 private）。 */
function collectPackages() {
  const list = [];
  for (const dir of fs.readdirSync(packagesDir).sort()) {
    const pkgJson = path.join(packagesDir, dir, "package.json");
    if (!fs.existsSync(pkgJson)) continue;
    const pkg = JSON.parse(fs.readFileSync(pkgJson, "utf8"));
    if (pkg.private) continue;
    if (!pkg.name.startsWith("@wailsnet/")) {
      throw new Error(`${pkg.name} 不在 @wailsnet 作用域内，无法发布到 wailsnet 组织`);
    }
    list.push({ dir: path.join(packagesDir, dir), name: pkg.name, version: pkg.version, pkg });
  }
  return list;
}

const packages = collectPackages();

// 构建产物预检：dist 缺失说明没构建，打包出来会是空壳
const missing = packages.filter((p) => !fs.existsSync(path.join(p.dir, "dist", "index.js")));
if (missing.length) {
  console.error(`以下包缺少 dist/index.js，请先执行 pnpm -r build：\n${missing.map((p) => "  - " + p.name).join("\n")}`);
  process.exit(1);
}

fs.mkdirSync(outDir, { recursive: true });

console.log(`共 ${packages.length} 个包，输出目录 ${path.relative(root, outDir)}`);
const tarballs = [];
for (const p of packages) {
  const out = execFileSync("pnpm", ["pack", "--pack-destination", outDir], {
    cwd: p.dir,
    encoding: "utf8",
    shell: true,
  });
  const file = out.trim().split(/\r?\n/).filter(Boolean).pop();
  const abs = path.resolve(p.dir, file);
  tarballs.push({ ...p, tarball: abs });
  console.log(`  打包 ${p.name}@${p.version} → ${path.relative(root, abs)}`);
}

if (!doPublish) {
  console.log(`\n打包完成：${tarballs.length} 个 tarball，位于 ${path.relative(root, outDir)}`);
  console.log("可人工上传，或加 --publish 直接发布到 registry.npmjs.org");
  process.exit(0);
}

// 发布顺序：runtime 必须在最前，插件包依赖它
tarballs.sort((a, b) => (a.name === "@wailsnet/runtime" ? -1 : b.name === "@wailsnet/runtime" ? 1 : 0));

// 暂存模式的 stage id 台账（granular token 无法用 npm stage list 反查，只能本地记录）
const stagedFile = path.join(outDir, "staged-ids.json");
let stagedIds = {};
if (staged && fs.existsSync(stagedFile)) {
  try {
    stagedIds = JSON.parse(fs.readFileSync(stagedFile, "utf8"));
  } catch {
    stagedIds = {};
  }
}

const failed = [];
for (const t of tarballs) {
  const tag = distTagFor(t.version);
  // --staged：只上传到 npm 暂存队列，由人在网页端 / npm stage approve 用 2FA 确认后生效
  const args = staged
    ? ["stage", "publish", t.tarball, "--access", "public", "--registry", registry, `--tag=${tag}`]
    : ["publish", t.tarball, "--access", "public", "--registry", registry, `--tag=${tag}`];
  if (dryRun) args.push("--dry-run");
  if (otp) args.push(`--otp=${otp}`);
  console.log(`\n==> ${dryRun ? "[dry-run] " : ""}${staged ? "[staged] " : ""}${t.name}@${t.version} (tag: ${tag})`);
  let output = "";
  try {
    output = execFileSync("npm", args, { cwd: root, encoding: "utf8", shell: true, stdio: ["ignore", "pipe", "pipe"] });
  } catch (e) {
    output = `${e.stdout ?? ""}${e.stderr ?? ""}`;
    process.stdout.write(output);
    // EOTP 是账号级配置问题，后面 40 个包必然同样失败——立即中止并给出修复指引
    if (/EOTP|one-time password/i.test(output)) {
      console.error(
        [
          "",
          "!! npm 要求一次性密码（EOTP），当前认证未开启 bypass 2FA，已中止后续发布。",
          "   修复：npmjs.com → Access Tokens → Generate New Token → Granular Access Token",
          "         勾选 “Bypass two-factor authentication for automation”，更新 ~/.npmrc-npmjs-wailsnet",
          "   或改用暂存模式（由你本人 2FA 审批）：pnpm publish:npm:staged",
        ].join("\n"),
      );
      process.exit(2);
    }
    console.error(`  !! ${t.name}@${t.version} 发布失败`);
    failed.push(t.name);
    continue;
  }
  process.stdout.write(output);
  // 暂存模式：记录 stage id（granular token 调 npm stage list 会 401，只能自己记账）
  if (staged) {
    const m = output.match(/staged with id ([0-9a-f-]{36})/i);
    if (m) {
      stagedIds[`${t.name}@${t.version}`] = m[1];
      fs.writeFileSync(stagedFile, JSON.stringify(stagedIds, null, 2));
    }
  }
}

if (staged) {
  const entries = Object.entries(stagedIds);
  console.log(`\n暂存条目 ${entries.length} 个（已写入 ${path.relative(root, stagedFile)}）：`);
  for (const [spec, id] of entries) console.log(`  ${spec}  ${id}`);
}

if (staged && !failed.length) {
  console.log("\n已全部进入暂存队列。下一步（必须人工，带 2FA）：");
  console.log("  npm stage list                     # 查看待审包");
  console.log("  npm stage approve <id>             # CLI 审批并发布");
  console.log("  或 https://www.npmjs.com → Staged Packages → Approve");
}

if (failed.length) {
  console.error(`\n${failed.length} 个包发布失败：\n${failed.map((n) => "  - " + n).join("\n")}`);
  process.exit(1);
}
console.log(`\n全部发布完成：${tarballs.length} 个包 → ${registry}`);
