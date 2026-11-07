# TVBox for Windows 发布清单

本文用于生成不包含开发者配置、历史、收藏、日志或私人订阅的无状态 GitHub Release。正式发布应从干净的 Git 提交构建，不要直接压缩日常运行目录或 `%LOCALAPPDATA%\TVBox PC`。升级和卸载默认保留该目录中的用户设置、源、历史与收藏，发布产物不得读取或覆盖这些数据。

## 1. 发布前确认

- 确认工作树中的改动均属于本次版本，没有临时截图、日志、测试配置或下载文件。
- 确认 `windows/TVBox.Windows/wexfnwconfig.json`、`prefs.json`、`configs.json`、`history.json`、`keep.json`、`app.log` 等本地文件未被 Git 跟踪。
- 搜索仓库中的私人订阅、带用户名/密码的 URL、Cookie、Token、私钥和 `C:\Users\...` 本机路径。
- 更新版本号、README、变更说明和已知问题。
- 确认项目级 `LICENSE`、第三方版权声明及所分发 FFmpeg/Node 二进制的许可要求。
- 从可信来源准备签名证书；证书和密码不得放入仓库。

建议检查命令：

```powershell
git status --short
git ls-files | Select-String -Pattern '(prefs|configs|history|keep)\.json|app\.log|wexfnwconfig'
rg -n --hidden -g '!**/bin/**' -g '!**/obj/**' -g '!artifacts/**' `
  'https?://[^/@\s:]+:[^/@\s]+@|C:\\Users\\|github_pat_|ghp_|BEGIN .*PRIVATE KEY'
```

任何匹配都必须人工判断。公共文档和依赖源码可能含普通示例 URL，但私人源地址、凭据和本机路径不得发布。

## 2. 恢复与严格构建

x64：

```powershell
dotnet restore .\windows\TVBox.Windows\TVBox.Windows.csproj -r win-x64
dotnet build .\windows\TVBox.Windows\TVBox.Windows.csproj `
  -c Release -p:Platform=x64 -r win-x64 --no-restore -warnaserror
```

正式发布要求零错误、零警告。不要把 `--no-restore` 用于首次 `publish`，否则可能得到文件看似齐全但缺失 .NET runtime pack 解析信息的无效包。当前内置 FFmpeg 与 Node.js 二进制均为 x64，不得把同一输出标记或发布为 ARM64。

## 3. 生成便携包

```powershell
.\scripts\Publish-Portable.ps1 -Version 1.0.4
```

默认输出：

```text
artifacts/
|-- TVBox-x64-1.0.4/
|-- TVBox-x64-1.0.4.zip
`-- SHA256SUMS.txt
```

发布目录与 MSI 安装目录必须使用以下结构：

```text
TVBox/
|-- TVBox.exe
|-- assets/
|   |-- icons/
|   `-- js/
|-- ffmpeg/
|-- libs/
|   |-- TVBox.dll
|   |-- TVBox.deps.json
|   |-- TVBox.runtimeconfig.json
|   |-- Microsoft.ui.xaml.dll / Microsoft.UI.Xaml.Phone.dll
|   |-- Microsoft.UI.pri / Microsoft.UI.Xaml.Controls.pri
|   `-- en-us/ ja-JP/ ko-KR/ zh-CN/ zh-TW/ (WinUI MUI)
|-- locales/ (reserved)
|-- node/
|-- runtime/
|   |-- host/fxr/<version>/hostfxr.dll
|   |-- shared/Microsoft.NETCore.App/<version>/
|   `-- shared/Microsoft.WindowsDesktop.App/<version>/
|-- LICENSE
|-- README.md
`-- THIRD-PARTY-NOTICES.md
```

`TVBox.exe` 是指向 `libs\TVBox.dll` 的根 apphost；它的 app-relative dotnet 路径必须从托管入口目录解析为 `..\runtime`。`runtime` 必须是标准私有 dotnet-root，而不是平铺的 self-contained publish 输出。整个程序目录必须恰好只有一个 `TVBox.exe`，`libs` 中不得再出现同名主程序。自0.5.0升级Windows App SDK 2.6.5后，WinUI模块、PRI和MUI必须在`libs`同目录解析，免注册清单也指向`libs`，不得复制到第二个目录。旧版`locales\winui`布局在新版会启动失败（0xC000027B），必须实测便携入口启动；只验证普通bin目录不够。

脚本执行以下检查：

- 使用 Release、框架依赖应用、私有 .NET Desktop Runtime、自包含 Windows App SDK、明确的 RID 和结构化多文件目标发布；禁止单文件打包。
- 禁止包中出现常见用户数据文件和运行时缓存目录。
- 校验根 apphost、WinUI 激活清单、唯一的 `TVBox.exe`、版本元数据、`libs` 中的托管入口，以及 `runtime\host\fxr` 和两个 `runtime\shared` 框架；同时校验 Node、JS 运行库、语言资源和关键 FFmpeg DLL。
- 扫描文本文件中的本机用户路径、带凭据 URL、私钥和常见 GitHub Token。
- 复制公开 README，创建 ZIP 并写入 SHA-256。

使用 `-ZipOnly` 可在成功压缩后删除未压缩目录。使用自定义 `-OutputRoot` 时，脚本仍会验证所有删除目标位于该输出目录下。

## 4. 安装包

当前TVBox PC 0.7.0仅交付便携包；上游安装脚本的产品名及WinUI布局校验尚待适配，以下为后续MSI交付要求，不表示本版已经生成或验收安装器。

安装器应只从同一版本的干净便携目录取文件，不得从 `bin/`、`obj/`、旧 `windows/publish/` 或用户运行目录取文件。

```powershell
.\installer\build.ps1 -Version 1.0.4
```

安装包输出为 `artifacts\TVBox-Setup-x64-1.0.4.msi`，并追加写入 `artifacts\SHA256SUMS.txt`。

至少确认：

- 安装向导可以修改安装路径，开始菜单名称使用 `TVBox`，并允许用户选择是否创建桌面快捷方式。
- 安装后的文件结构必须与便携包一致：根目录只有一个 `TVBox.exe`，应用依赖、私有 .NET、Node、FFmpeg、Assets 和语言资源按上述目录分类。
- 升级前能关闭正在运行的 `TVBox.exe`，或明确提示用户退出。
- 卸载程序默认不要静默删除 `%LOCALAPPDATA%\TVBox PC`，避免误删用户收藏和历史；如提供清理选项，必须显式说明且由用户选择。
- 安装器架构必须标记为 x64，不得误标为 ARM64。
- 签名发生在最终文件生成后；签名后的安装包重新计算 SHA-256。

## 5. 人工验收

自动构建通过不等于播放行为已验证。发布前由人工至少检查：

- 全新数据目录首次启动，没有预置点播源、直播源、收藏或历史。
- 在未安装系统 .NET Desktop Runtime 的干净 x64 环境中，根 `TVBox.exe` 仍能使用随包 `runtime` 启动；不得出现安装 .NET 的提示。
- 点播配置、CatPawOpen 配置和独立直播配置可以添加、切换和重载。
- 点播详情、搜索、播放、暂停、拖动进度、上下集、线路、倍率、比例和字幕。
- 直播频道、上下频道、暂停、线路切换及可回看内容的进度行为。
- 普通窗口、最大化、视频/直播全屏、画中画及退出后的窗口和侧栏状态。
- 100%、125%、150%、200% 缩放，以及有条件时的多显示器。
- 退出后没有遗留 Node 子进程或占用发布目录文件。

音画同步、爆音、异常时间戳、特定编码和长时间播放必须通过真实内容人工试听，不能仅凭编译或 UI 截图判断。

## 6. Release 内容

### 用户要求的独立 Markdown 更新报告

每次更新改版后必须交付 `docs/reports/TVBox-PC-v<版本号>-Update-Report.md`，并更新报告索引。第一份0.3.0报告为0.1.0至0.3.0累计汇总，以后按版本提供增量报告。

报告记录真实改动、原因、验证、限制、遗留事项、下载/启动方式及用户数据保留情况；区分行为断言、实际播放与未验证内容。仅补写文档不提升程序版本，不重新生成已发布的二进制包。

若本次已有发布授权，把MD作为同版本Release的独立附件上传，并完整下载校验；最终回复必须提供报告文件链接。不得只提供聊天摘要或只把报告放在ZIP内部。详细约定见仓库根目录 `AGENTS.md`。

每个 GitHub Release 建议包含：

- x64 安装包
- x64 便携 ZIP
- `SHA256SUMS.txt`
- 版本变更、修复项、已知问题、最低系统版本和升级说明

Release 说明中不要粘贴私人订阅或带鉴权的复现地址。若没有代码签名，应明确说明 SmartScreen 可能提示未知发布者。

本地构建完成不代表获得上传授权。未收到项目维护者明确的指定上传指令时，禁止执行 Git 提交、打标签、推送或创建/更新 GitHub Release；构建脚本也不得包含这些操作。

收到明确上传指令并配置好 GitHub CLI 后，才可使用类似命令发布；实际仓库、标签和文件名以当前版本为准：

```powershell
git tag -a v1.0.4 -m 'TVBox for Windows v1.0.4'
git push origin v1.0.4
gh release create v1.0.4 .\artifacts\TVBox-x64-1.0.4.zip `
  .\artifacts\TVBox-Setup-x64-1.0.4.msi `
  .\artifacts\SHA256SUMS.txt --verify-tag --title 'TVBox for Windows v1.0.4'
```

先创建草稿 Release 并人工核对附件、哈希和说明，再公开发布。

## 7. 发布后

- 从 GitHub Release 重新下载附件并核对 SHA-256，而不是只检查本地原文件。
- 在干净 Windows 用户环境中安装或解压一次，确认 `TVBox.exe` 可启动。
- 确认 Release 附件中没有 `prefs.json`、`configs.json`、`history.json`、`keep.json`、日志、PDB、证书或源地址文件。
- 若发现凭据或私人源泄露，立即删除 Release、撤销相应凭据、清理 Git 历史并重新发版；仅删除当前文件不足以消除 Git 历史中的泄露。


0.7.0：在支持 SmallestSize 的 .NET/PowerShell 环境使用更高压缩率的标准 ZIP；旧环境退回 Optimal。文件内容和布局不因压缩等级改变，仍需 CRC、当前程序集/PRI、运行库和独立 MD 回读校验。


## 公开发布与旧版更新（0.8.6起）

公开仓库为`cbdoglolz/tvbox-pc-public`。工作目录的origin可能仍是上游，禁止直接推送私人开发历史或改变原私有仓库可见性。首次公开使用通过审计的跟踪文件创建新历史；后续只在独立公开检出中更新同一份干净文件。所有新增凭据文件（包括`services.credential`）、用户资料和临时QA目录必须排除。

继续使用`./scripts/Build.ps1 -Portable`，包含离线断言、独立安装回滚场景和严格构建；随后运行`python scripts/verify_package.py`。每个稳定Release都附便携包、完整对应源码、独立MD和SHA-256。以匿名方式完整下载比对后才算交付。原始下载ZIP必须保持项目标准的单层`TVBox-x64-<版本>`根目录，不能重新压缩日常使用目录。

0.8.5及以前需要手动安装首个带更新器的0.8.6；以后设置里的更新入口只读取本公开仓库稳定Release。不得用强制结束交互实例完成升级。更新暂存/备份位于用户资料updates目录，与程序和缓存分开，失败保留恢复依据。更换自托管渠道需另外实现一致的版本和摘要协议，不在旧二进制里凭空生效。
