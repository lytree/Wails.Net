// 冒烟测试：验证 L2 命令抽象层 defineCommand 的三种 pack 模式 wire 行为。
// 运行：node smoke-commands.mjs（需先 tsc 构建出 dist/）
// 说明：vitest 测试（commands.test.ts）依赖安装环境，此脚本在 node 直跑。

const calls = [];
globalThis.__wailsTransport = {
  invoke(type, payload) {
    calls.push({ type, payload });
    // 真实 transport 在收到响应后 resolve 解包后的业务值；此处直接模拟该行为
    return Promise.resolve("ok");
  },
};

const { defineCommand } = await import("./dist/core/commands.js");

let failed = 0;
function assert(cond, msg) {
  if (cond) {
    console.log(`  PASS  ${msg}`);
  } else {
    failed++;
    console.error(`  FAIL  ${msg}`);
  }
}

console.log("none 模式：无参数 → []");
{
  const getCurrentVersion = defineCommand("updater.getCurrentVersion", "none");
  await getCurrentVersion();
  const c = calls.at(-1);
  assert(c.payload.name === "updater.getCurrentVersion", `命令名正确 (${c.payload.name})`);
  assert(JSON.stringify(c.payload.args) === "[]", `wire 为 [] (${JSON.stringify(c.payload.args)})`);
}

console.log("single 模式：单参数 → [{...}]");
{
  const checkForUpdate = defineCommand("updater.checkForUpdate", "single");
  await checkForUpdate("beta");
  const c = calls.at(-1);
  assert(c.payload.name === "updater.checkForUpdate", `命令名正确 (${c.payload.name})`);
  assert(JSON.stringify(c.payload.args) === '["beta"]', `wire 为 ["beta"] (${JSON.stringify(c.payload.args)})`);
}

console.log("spread 模式：多位置参数 → [...args]");
{
  const setSize = defineCommand("window.setSize", "spread");
  await setSize(800, 600);
  const c = calls.at(-1);
  assert(c.payload.name === "window.setSize", `命令名正确 (${c.payload.name})`);
  assert(JSON.stringify(c.payload.args) === "[800,600]", `wire 为 [800,600] (${JSON.stringify(c.payload.args)})`);
}

console.log("single 模式：对象参数保持引用");
{
  const opts = { width: 800, height: 600 };
  const setSize = defineCommand("window.setSize", "single");
  await setSize(opts);
  const c = calls.at(-1);
  assert(c.payload.args[0] === opts, "对象引用未被拷贝");
}

console.log("返回类型透传：Promise.resolve 结果");
{
  const r = await defineCommand("x.y", "none")();
  assert(r === "ok", `返回值为 invoke 结果 (${r})`);
}

console.log(failed === 0 ? "\nALL PASS" : `\n${failed} FAILED`);
process.exit(failed === 0 ? 0 : 1);
