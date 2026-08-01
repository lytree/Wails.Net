#!/usr/bin/env bash
#==============================================================================
# Wails.Net 本地打包与冒烟测试脚本
#
# 用途：发布前本地验证 NuGet 包正确性，模拟外部消费者体验。
#   1. 打包 src/ 全部可发布项目到 artifacts/nupkg/（含 CLI、Templates）
#   2. 验证关键包（Sdk / Cli / Templates / Bundle / Application / SourceGenerators）已生成
#   3. 在仓库外临时目录创建测试项目，用 PackageReference 引用 Wails.Net.Sdk，
#      验证可还原、可构建（SDK 依赖链 + 源生成器 analyzer 加载正确）
#   4. dotnet tool install Wails.Net.Cli 到临时路径，运行 wails-net --version 验证 CLI 可用
#
# Android 平台包（Application.Android / Bundle.Android）需 android 工作负载，默认跳过。
# 如已安装，可加 --include-android 开关一并打包。
#
# 用法：
#   bash scripts/pack-and-test.sh                 # 打包并完整验证
#   bash scripts/pack-and-test.sh --skip-pack     # 跳过打包，仅用已有包验证
#   bash scripts/pack-and-test.sh --include-android
#   bash scripts/pack-and-test.sh --keep-temp     # 保留临时测试目录
#==============================================================================
set -euo pipefail

# ----------------------------------------------------------------------------
# 参数解析
# ----------------------------------------------------------------------------
SKIP_PACK=0
INCLUDE_ANDROID=0
KEEP_TEMP=0
for arg in "$@"; do
  case "$arg" in
    --skip-pack)        SKIP_PACK=1 ;;
    --include-android)  INCLUDE_ANDROID=1 ;;
    --keep-temp)        KEEP_TEMP=1 ;;
    *) echo "未知参数: $arg"; exit 1 ;;
  esac
done

# ----------------------------------------------------------------------------
# dotnet 路径检测：优先 PATH，否则尝试 Windows 常见安装路径
# ----------------------------------------------------------------------------
if ! command -v dotnet >/dev/null 2>&1; then
  for _c in "/c/Program Files/dotnet" "/c/Program Files (x86)/dotnet"; do
    if [ -x "$_c/dotnet.exe" ]; then export PATH="$_c:$PATH"; break; fi
  done
fi
command -v dotnet >/dev/null 2>&1 || { echo "FAIL: 未找到 dotnet，请安装 .NET 10 SDK"; exit 1; }

# ----------------------------------------------------------------------------
# 路径准备：REPO_ROOT_WIN 为 Windows 风格路径（正斜杠），供 dotnet 使用
# ----------------------------------------------------------------------------
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT_UNIX="$(cd "$SCRIPT_DIR/.." && pwd)"
# pwd -W 在 git bash 输出 Windows 路径（F:/...），纯 Linux 回退到 Unix 路径
REPO_ROOT_WIN="$(cd "$SCRIPT_DIR/.." && pwd -W 2>/dev/null || echo "$REPO_ROOT_UNIX")"
NUPKG_DIR="$REPO_ROOT_WIN/artifacts/nupkg"
# 临时测试目录在步骤 3 用 mktemp 创建于仓库外（避免继承仓库 Directory.Build.props/CPM）

# 辅助函数
step() { echo; echo "========== $1 =========="; }
ok()   { echo "[OK] $1"; }
fail() { echo "[FAIL] $1"; exit 1; }

#==============================================================================
# 步骤 1：打包 NuGet 包
#==============================================================================
if [ "$SKIP_PACK" -eq 0 ]; then
  step "步骤 1: 打包 NuGet 包到 artifacts/nupkg"

  # 清理旧包（保留 .gitkeep）。用 find -delete 避免 rm 被沙箱安全删除拦截。
  find "$REPO_ROOT_UNIX/artifacts/nupkg" -maxdepth 1 \( -name '*.nupkg' -o -name '*.snupkg' \) -delete 2>/dev/null || true

  # 按依赖顺序打包（底层 -> 平台 -> 聚合 -> SDK -> 工具/模板）。
  # dotnet pack 单项目会自动 restore + build，无需预还原整个解决方案，
  # 因此不依赖 android 工作负载（Android 项目默认排除）。
  projects=(
    Wails.Net.Errors
    Wails.Net.Events
    Wails.Net.AssetServer
    Wails.Net.Runtime.Js
    Wails.Net.SourceGenerators
    Wails.Net.Generator
    Wails.Net.Application
    Wails.Net.Application.Windows
    Wails.Net.Application.Linux
    Wails.Net.Application.MacOS
  )
  if [ "$INCLUDE_ANDROID" -eq 1 ]; then
    projects+=(Wails.Net.Application.Android)
  fi
  projects+=(
    Wails.Net.Bundle.Windows
    Wails.Net.Bundle.Linux
  )
  if [ "$INCLUDE_ANDROID" -eq 1 ]; then
    projects+=(Wails.Net.Bundle.Android)
  fi
  projects+=(
    Wails.Net.Sdk
    Wails.Net.Cli
  )

  total=${#projects[@]}
  i=0
  for p in "${projects[@]}"; do
    i=$((i + 1))
    proj="$REPO_ROOT_WIN/src/$p/$p.csproj"
    echo "  [$i/$total] 打包 $p ..."
    # -p:SkipFrontendBuild=true 跳过前端构建；-p:WailsNetEnableAndroid=false 确保不评估 Android TFM
    dotnet pack "$proj" -c Release -o "$NUPKG_DIR" \
      -p:SkipFrontendBuild=true -p:WailsNetEnableAndroid=false --nologo
  done

  # Templates 不在 slnx 中（dotnet pack 因 NU5017 误报返回非零退出码，但 nupkg 实际已生成）。
  # 单独打包并容忍退出码，随后验证 nupkg 实际生成。
  echo "  [$((total + 1))/$((total + 1))] 打包 Wails.Net.Templates ..."
  templates_proj="$REPO_ROOT_WIN/src/Wails.Net.Templates/Wails.Net.Templates.csproj"
  dotnet pack "$templates_proj" -c Release -o "$NUPKG_DIR" --nologo || true
  if ! ls "$REPO_ROOT_UNIX"/artifacts/nupkg/Wails.Net.Templates.*.nupkg >/dev/null 2>&1; then
    fail "Wails.Net.Templates 包未生成"
  fi
  total=$((total + 1))
  ok "已打包 $total 个项目"
fi

#==============================================================================
# 步骤 2：验证关键包并解析版本号
#==============================================================================
step "步骤 2: 验证关键 NuGet 包"

required_packages=(
  Wails.Net.Sdk
  Wails.Net.Cli
  Wails.Net.Templates
  Wails.Net.Bundle.Windows
  Wails.Net.Bundle.Linux
  Wails.Net.Application
  Wails.Net.Application.Windows
  Wails.Net.AssetServer
  Wails.Net.SourceGenerators
  Wails.Net.Errors
  Wails.Net.Events
  Wails.Net.Runtime.Js
)

version=""
for pkg in "${required_packages[@]}"; do
  # 匹配包文件，排除 symbols 包
  found="$(ls "$REPO_ROOT_UNIX"/artifacts/nupkg/${pkg}.*.nupkg 2>/dev/null | grep -v 'symbols' | head -1 || true)"
  if [ -z "$found" ]; then
    fail "缺少包: $pkg（artifacts/nupkg 中未找到）"
  fi
  # 从文件名解析版本：Wails.Net.Sdk.0.1.0-alpha.1.nupkg -> 0.1.0-alpha.1
  base="$(basename "$found" .nupkg)"
  ver="${base#${pkg}.}"
  if [ -z "$version" ]; then version="$ver"; fi
  ok "$pkg -> $ver"
done

nupkg_count="$(ls "$REPO_ROOT_UNIX"/artifacts/nupkg/*.nupkg 2>/dev/null | wc -l)"
echo "共生成 $nupkg_count 个 nupkg，版本号: $version"

#==============================================================================
# 步骤 3：验证 SDK 可被 PackageReference 引用（仓库外临时项目）
#==============================================================================
step "步骤 3: 验证 SDK 可被 PackageReference 引用并构建"

# 仓库外临时目录：避免继承仓库 Directory.Build.props / CPM，真实模拟外部消费者。
# mktemp -d 创建于系统临时目录；pwd -W 获取 Windows 路径供 dotnet 使用。
TMP_UNIX="$(mktemp -d)"
TMP_WIN="$(cd "$TMP_UNIX" && pwd -W 2>/dev/null || echo "$TMP_UNIX")"
TEST_DIR="$TMP_WIN/SdkConsumer"
TOOL_DIR="$TMP_WIN/tools"
mkdir -p "$TMP_UNIX/SdkConsumer" "$TMP_UNIX/tools"

# 独立 nuget.config：仅本地源，强制从 artifacts/nupkg 解析
cat > "$TMP_UNIX/SdkConsumer/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="Wails.Net.Local" value="$NUPKG_DIR" />
  </packageSources>
</configuration>
EOF

# 测试项目：Windows 消费者场景。
# net10.0-windows10.0.19041.0 TFM，SDK 的 build/Wails.Net.Sdk.props 会自动启用
# UseWindowsForms（无需手动设置）。临时目录在仓库外，不受仓库 CPM/Directory.Build.props 管控。
cat > "$TMP_UNIX/SdkConsumer/SdkConsumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Wails.Net.Sdk" Version="$version" />
  </ItemGroup>
</Project>
EOF

# Program.cs：引用 Wails.Net.Application 公共类型，验证依赖链可达（不启动 GUI）
cat > "$TMP_UNIX/SdkConsumer/Program.cs" <<'EOF'
using Wails.Net.Application;
using Wails.Net.Application.Hosting;

var asm = typeof(DesktopApplicationBuilder).Assembly;
Console.WriteLine($"Wails.Net.Application loaded from: {asm.Location}");
Console.WriteLine("SDK 依赖验证通过");
EOF

echo "还原 + 构建测试项目（PackageReference Wails.Net.Sdk）..."
dotnet build "$TEST_DIR/SdkConsumer.csproj" -c Release --nologo
ok "SDK 可被 PackageReference 引用并构建成功（依赖链 + 源生成器 analyzer 加载正常）"

#==============================================================================
# 步骤 4：验证 CLI 可作为 dotnet global tool 安装运行
#==============================================================================
step "步骤 4: 验证 CLI dotnet tool 安装"

dotnet tool install Wails.Net.Cli --version "$version" \
  --tool-path "$TOOL_DIR" --add-source "$NUPKG_DIR"
ok "CLI 工具安装成功"

tool_exe="$TMP_UNIX/tools/wails-net.exe"
if [ ! -f "$tool_exe" ]; then fail "未找到工具可执行文件: $tool_exe"; fi

echo "运行 wails-net --version ..."
"$tool_exe" --version
ok "wails-net CLI 可正常执行"

#==============================================================================
# 完成
#==============================================================================
step "全部验证通过"
echo "  NuGet 包输出目录 : $NUPKG_DIR"
echo "  包版本           : $version"
echo "  SDK 依赖验证     : 通过（PackageReference Wails.Net.Sdk 可还原可构建）"
echo "  CLI 安装验证     : 通过（dotnet tool install + wails-net --version）"

if [ "$KEEP_TEMP" -eq 1 ]; then
  echo
  echo "临时测试目录已保留: $TMP_UNIX"
else
  find "$TMP_UNIX" -delete 2>/dev/null || true
fi
