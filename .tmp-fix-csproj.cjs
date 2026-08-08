const fs = require("fs");

const files = [
  "examples/Wails.Net.Demo/Wails.Net.Demo.csproj",
  "examples/Wails.Net.Demo.Vue/Wails.Net.Demo.Vue.csproj",
  "examples/Wails.Net.Demo.React/Wails.Net.Demo.React.csproj",
  "examples/Wails.Net.Demo.Environment/Wails.Net.Demo.Environment.csproj",
  "examples/Wails.Net.Demo.DevRelease/Wails.Net.Demo.DevRelease.csproj",
  "examples/Wails.Net.Demo.Binding/Wails.Net.Demo.Binding.csproj",
];

const good = `  <ItemGroup>
    <!-- M3: OsInfo/AppInfo/Path 插件包 -->
    <ProjectReference Include="..\\..\\src\\Wails.Net.Plugins.OsInfo\\Wails.Net.Plugins.OsInfo.csproj" />
    <ProjectReference Include="..\\..\\src\\Wails.Net.Plugins.AppInfo\\Wails.Net.Plugins.AppInfo.csproj" />
    <ProjectReference Include="..\\..\\src\\Wails.Net.Plugins.Path\\Wails.Net.Plugins.Path.csproj" />
  </ItemGroup>
`;

const badPattern = /  <ItemGroup>\s*\n\s*<!-- M3: OsInfo\/AppInfo\/Path 插件包 -->[\s\S]*?  <\/ItemGroup>\n/g;

for (const f of files) {
  let c = fs.readFileSync(f, "utf8");
  const before = c;
  c = c.replace(badPattern, good);
  if (c !== before) {
    fs.writeFileSync(f, c);
    console.log("fixed:", f);
  } else {
    console.log("NO-MATCH:", f);
  }
}
