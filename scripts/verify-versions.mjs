// M4 版本一致性校验：Directory.Build.props 的 WailsNetVersion == 所有 packages/*/package.json 的 version。
// 用法：node scripts/verify-versions.mjs [--fix]
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(__dirname, "..");

// 读 Directory.Build.props 的 WailsNetVersion
function readWailsNetVersion() {
  const props = fs.readFileSync(path.join(root, "Directory.Build.props"), "utf8");
  const m = props.match(/<WailsNetVersion>([^<]+)<\/WailsNetVersion>/);
  if (!m) throw new Error("Directory.Build.props 中未找到 WailsNetVersion");
  return m[1];
}

const expected = readWailsNetVersion();
const packagesDir = path.join(root, "packages");
const failures = [];
let checked = 0;

for (const dir of fs.readdirSync(packagesDir)) {
  if (!dir.startsWith("wails-net-")) continue;
  const pkgJson = path.join(packagesDir, dir, "package.json");
  if (!fs.existsSync(pkgJson)) continue;
  const pkg = JSON.parse(fs.readFileSync(pkgJson, "utf8"));
  checked++;
  if (pkg.version !== expected) {
    failures.push(`${pkg.name}: ${pkg.version} ≠ ${expected}`);
    if (process.argv.includes("--fix")) {
      pkg.version = expected;
      fs.writeFileSync(pkgJson, JSON.stringify(pkg, null, 2) + "\n");
      console.log(`  fixed ${pkg.name} → ${expected}`);
    }
  }
}

console.log(`检查 ${checked} 个前端包，期望版本 ${expected}`);
if (failures.length) {
  console.error("版本不一致：\n" + failures.map(f => "  ✗ " + f).join("\n"));
  if (!process.argv.includes("--fix")) process.exit(1);
} else {
  console.log("全部一致 ✓");
}
