# Wallpaper Engine 「当前壁纸视频 → PotPlayer」一键播放

零修改 Wallpaper Engine 本体的小工具。托盘常驻 + 全局热键，一键把**当前正在用的壁纸视频**丢给 PotPlayer 播放，
省掉「右键 → 在资源管理器中打开 → 双击视频」这一串操作。

**交付形态：一个 exe。** 桌面上双击 `Wallpaper视频播放器.exe` 就能用 ——
不需要安装、不需要命令行、不写注册表、不设开机自启。

---

## 一、可行性评估结论（先说重点）

对 `D:\program files\steam\steamapps\common\wallpaper_engine`（v2.8.42）的勘察结果：

| 项 | 结论 |
|---|---|
| `plugins\` 目录 | 只有 `led`（iCUE / Chroma 灯效联动），**没有通用插件/扩展接口** |
| 主程序 | `wallpaper64.exe` + `bin\wallpaperui.exe`，基于 **CEF(Chromium)**，UI 是单文件压缩过的 AngularJS + 原生 C++ IPC |
| 界面代码 | 未打包进 pak，明文放在 `ui\dist\scripts\scripts.js`（1.16MB，单行压缩） |
| 右键菜单项来源 | JS 里 `contextMenu.builder().button(标签, 图标, 回调)`；「在资源管理器中打开」= `callDeferred("browseWallpaperObject","openInExplorer",路径)` |
| 原生侧可用命令 | 反查 `wallpaperui.exe` 的方法名表，有 `openInExplorer` / `openDirectory` / `playInWindow` / `openInEditor` … **没有任何「用外部程序打开」的命令** |
| 开发者文档 | 官方只支持 **Wallpaper Engine 壁纸（网页/场景/视频）** 的开发，**没有宿主程序插件 SDK** |

所以：

1. **「写一个官方插件」这条路不存在。** 能做的只有两件事：改它的 UI 脚本，或者在它外面挂一个辅助程序。
2. **「在右键菜单里加一项」技术上可行**，但必须动安装目录里的 `ui\dist\scripts\scripts.js`：
   在「在资源管理器中打开」按钮旁边插入 132 个字符，把路径交给一个自定义协议（`wpepot:`）或外部程序。
   - 需要管理员权限（`D:\program files\...` 写入要 UAC）；
   - Steam 更新/校验可能覆盖，需要重打；
   - 属于修改游戏文件，有风险。
   离线验证过插入点唯一、可行（见 `probe\` 里的记录）。
3. **不改任何游戏文件也能达到同样目的**：当前壁纸的文件路径就写在
   `wallpaper_engine\config.json` → `general.wallpaperconfig.selectedwallpapers.Monitor0.file`，
   直接读它 + 调用 PotPlayer，就是本项目采用的方案 B。

> 结论：**要「原生右键菜单项」就选方案 A（改 UI，需提权、会被更新覆盖）；
> 要「干净、稳定、不怕更新」就选方案 B（本工具）。**

---

## 二、本工具做什么

- 托盘常驻，图标右键菜单里一键播放；双击托盘图标也能播。
- 全局热键（默认 **Ctrl+Alt+P**）直接播放，不用先切回 Wallpaper Engine。
- 自动读取 Wallpaper Engine 配置，得到**当前正在使用**的壁纸文件，支持 `.mp4 .webm .mkv .avi .mov .m4v .wmv .flv .mpg .mpeg .ts`。
- 支持多显示器（`monitor` 指定 `Monitor0` / `Monitor1` …）。
- 「最近使用过的壁纸」子菜单：直接播历史里的视频壁纸；还列出当前壁纸同目录的其它视频。
- 当前壁纸是场景/网页（`scene.pkg`、`index.html`）时，自动改为在资源管理器中定位它，而不是报错。
- 播放器可配置：默认用 PotPlayer，也能填 VLC / MPC-HC 等任意播放器 exe。
- 自带 `--diagnose` 自检和 `launcher.log` 日志，出问题好排查。

---

## 三、怎么用（就是一个 exe，双击即可）

**桌面上已经放好一个**：`Wallpaper视频播放器.exe`（图标是深蓝圆底 + 黄色播放三角）。

- **双击它** → 托盘出现同一个图标，程序开始工作（**不需要任何安装、不需要命令行、不写注册表、不设开机自启**）。
- 之后两种用法：
  - 按 **Ctrl+Alt+P**（推荐，全局生效，不用切窗口）；
  - 或**双击托盘图标** / 右键托盘图标 →「播放当前壁纸视频」。
- **再双击一次 exe** 不会开出第二个进程，只会弹个气泡提醒「程序已经在运行」。
- 不想常驻了：右键托盘图标 → 退出。下次要用再双击一次即可。

> **exe 是自包含的**：默认值编译在程序内部，旁边没有 `config.json` 也照样能跑（会自动探测 Wallpaper Engine 配置和 PotPlayer）。
> 只有想改播放器路径/热键时，才需要右键托盘 →「打开配置文件」生成一份 `config.json`。

想让它跟着开机自动跑（可选，默认关闭）：把桌面这个 exe 的快捷方式放进
`Win+R` → `shell:startup` 打开的启动文件夹即可。

### 从源码重新编译（一般不需要）

```powershell
powershell -ExecutionPolicy Bypass -File release\build.ps1
```

编译产物就是 `release\WpeVideoLauncher.exe`（与桌面那个功能完全相同），可自由复制到任何地方双击运行。

---

## 四、配置说明（可选）

不需要配置就能用。只有要改东西时，右键托盘图标 →「打开配置文件」，
会在 **exe 所在目录**生成 `config.json`（该目录不可写时退到 `%LOCALAPPDATA%\WpeVideoLauncher\config.json`），
编辑后回到托盘 →「重新加载配置」。

```json
{
  "configFile": "D:/program files/steam/steamapps/common/wallpaper_engine/config.json",
  "player":     "D:/program files/PotPlayer/PotPlayerMini64.exe",
  "hotkey":     "Ctrl+Alt+P",
  "monitor":    "Monitor0",
  "extraArgs":  "",
  "showBalloon": true,
  "videoExtensions": ".mp4,.webm,.mkv,.avi,.mov,.m4v,.wmv,.flv,.mpg,.mpeg,.ts"
}
```

| 字段 | 说明 |
|---|---|
| `configFile` | Wallpaper Engine 的 `config.json` 路径。留空 = 自动探测（进程路径 → Steam 注册表 → 常见路径） |
| `player` | 播放器 exe 路径。**建议用 `/` 或 `\\`**，用单个 `\` 会被当转义符 |
| `hotkey` | 形如 `Ctrl+Alt+P`、`Ctrl+Shift+F9`、`Alt+1`；填 `none` 关闭热键。**若该组合已被别的软件占用，日志里会出现 `RegisterHotKey 失败, err=1409`，换个键即可** |
| `monitor` | 取哪个显示器的壁纸，`Monitor0` 起 |
| `extraArgs` | 追加给播放器的命令行参数，例如 `/new` 让 PotPlayer 开新实例 |
| `showBalloon` | 是否显示气泡提示 |
| `videoExtensions` | 判定为「视频壁纸」的扩展名列表 |


改完配置后，托盘右键 →「重新加载配置」即可生效（热键会重新注册）。也可以直接点「打开配置文件」。

---

## 五、命令行用法

```powershell
WpeVideoLauncher.exe              # 托盘模式（正常用法）
WpeVideoLauncher.exe --once       # 播放一次就退出（桌面快捷方式用的就是这个，适合绑别的启动器/热键工具）
WpeVideoLauncher.exe --diagnose   # 自检，把探测结果写进 launcher.log
```

退出码：`0` 成功，`2` 取不到当前壁纸，`3` 当前壁纸不是视频，`4` 找不到播放器，`5` 启动播放器失败。

---

## 六、常见问题

**Q: 双击 exe 之后没看到任何窗口？**
正常。它是托盘程序，图标在右下角托盘区（深蓝圆底 + 黄色播放三角），可能需要点托盘的小箭头展开隐藏图标。

**Q: 按了热键没反应？**
看 `launcher.log` 有没有 `热键已注册`。若显示 `RegisterHotKey 失败, err=1409`，说明 `Ctrl+Alt+P` 被
别的软件（输入法、QQ、截图工具等）占了，右键托盘 →「打开配置文件」把 `hotkey` 换成
`Ctrl+Alt+F9` 之类，再「重新加载配置」。
若日志里连注册记录都没有，说明没找到 Wallpaper Engine 的 `config.json`（用 `--diagnose` 看）。

**Q: 日志文件在哪？**
优先在 exe 旁边（`launcher.log`）；若 exe 所在目录不可写，则自动写到
`%LOCALAPPDATA%\WpeVideoLauncher\launcher.log`。托盘右键 →「打开日志」可直接打开。

**Q: 播放的是旧壁纸？**
Wallpaper Engine 在切换壁纸时会写 `config.json`，本工具每次触发都重新读一遍，正常不会旧。
若确实旧，多半是你换了壁纸但 WE 还没落盘，稍等一秒再按。多屏时确认 `monitor` 填对了。

**Q: 提示「当前壁纸不是视频」？**
该壁纸是场景(`scene.pkg`)或网页(`index.html`) 类型，没有独立视频文件可播，工具会改为在资源管理器里定位它。

**Q: 中文/emoji 文件名的视频能播吗？**
能。工具按 UTF-8 读取配置，实测 `[TY98]FUTA⚠星×知更鸟.mp4`、`Rinhee 26.08 2B 中文字幕.mp4` 这类路径都能正确传给 PotPlayer。

**Q: 会不会动到 Wallpaper Engine 的文件？**
不会。本方案只**读**它的 `config.json`，不写、不改、不注入；程序不写注册表、不设自启。

---

## 七、目录结构

```
wallpaper plugin\
├─ README.md                      本文件（含可行性评估）
├─ src\
│  ├─ WpeVideoLauncher.cs         全部逻辑（单文件 C#，WinForms 托盘 + 热键 + 内置默认配置）
│  ├─ app.manifest                asInvoker + PerMonitorV2 DPI
│  └─ app.ico                     图标（10 尺寸，DIB+PNG，已嵌入 exe）
├─ tools\
│  └─ make-icon.ps1               重新生成 app.ico
├─ release\
│  ├─ build.ps1                   用系统自带 csc.exe 编译（自动嵌入图标）
│  ├─ install.ps1                 可选的"复制到指定目录+建快捷方式"，不用也能直接跑 exe
│  ├─ uninstall.ps1               撤销上面那个脚本做的事
│  ├─ config.json                 配置模板（可选，exe 已内置默认值）
│  └─ WpeVideoLauncher.exe        编译产物，可直接双击
└─ probe\                         勘察期脚本（只读，供参考）
   ├─ enum-windows.ps1            Wallpaper Engine 窗口/CEF 结构枚举
   ├─ acc-probe2.ps1              CEF 无障碍接口探测
   └─ read-current.ps1            从 config.json 提取当前壁纸视频路径
```

---

## 八、实测记录（2026-09-20，本机）

| 项目 | 结果 |
|---|---|
| 双击 exe | 托盘启动正常，常驻稳定（连续观察 15 秒无退出） |
| 单实例 | 再双击一次不会开第二个进程，会弹气泡提醒 |
| 热键 Ctrl+Alt+P | **实际按键验证通过**：日志出现 `收到热键，开始播放` 并成功调起 PotPlayer 播放 4 个不同壁纸视频 |
| 无配置运行 | 干净目录里只有一个 exe，仍能自动找到 WE 配置与 PotPlayer |
| 路径解析 | 中文/emoji/空格/方括号文件名全部正确传递 |
| 内存占用 | 空闲工作集约 33–35 MB，专用内存约 25 MB，30 秒累计 CPU 0.16 秒 |
| 只读目录 | 日志自动回退到 `%LOCALAPPDATA%`，不报错 |
| 对 WE 的影响 | 零改动（只读它的 config.json） |

---

## 九、附录：如果以后想做「原生右键菜单项」（方案 A 要点）

勘察已确认的插入点（`ui\dist\scripts\scripts.js`，唯一匹配）：

```js
// 原文
(A.button("ui_editor_filemenu_open_in_explorer","fas fa-folder-open",
  function(){j.callDeferred("browseWallpaperObject","openInExplorer",o.project||o.file)}),

// 插入后
(A.button("在 PotPlayer 中播放","fas fa-play",
  function(){window.open("wpepot:"+(o.project||o.file))})),
```

配套需要：注册 `HKCU\Software\Classes\wpepot` 协议指向一个能接收路径并启动 PotPlayer 的小程序。
代价：需要管理员写入安装目录、Steam 更新后可能被覆盖、属性校验/完整性风险，故本项目未采用。

原版文件 SHA256 可用 `Get-FileHash` 备份记录，改前务必备份。

