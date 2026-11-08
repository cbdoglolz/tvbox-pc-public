# TVBox PC

Windows 11 x64 影视点播客户端。[下载便携版](https://github.com/cbdoglolz/tvbox-pc-public/releases/latest)。完整解压后运行根目录 **TVBox.exe**，不要只复制主程序。

## 使用

- 首页按豆瓣、Netflix标签、TMDB目录发现作品；先查看详情，再选择播放。TMDB资料与图片需要在设置中填写个人API Key或Read Token，不填仍可使用豆瓣和普通点播。
- 自动检测启用的来源，支持速度/高清偏好、手动切换和同集续播。刷新选源保留当前播放，检测结束后自行选择新来源。
- 收藏、逐集进度、搜索历史与站点开关保存在本机。站点可分组、添加、关闭，也可添加私人TVBox订阅；Android/JVM JAR不支持在Windows直接运行。
- 播放中可切去首页或设置，点击左侧正在播放返回。单击画面显示/隐藏控制；空格暂停，左右键快进退，F全屏，M静音。
- 底部弹幕按钮控制开关，弹幕源菜单调整大小、速度、颜色和屏幕占比。覆盖范围取决于所选服务，未找到本片本集时显示黄色提示。
- 国际来源为可选实验入口，默认关闭；接口可返回不代表实际视频可播放。

## 更新与资料

设置内可检查软件更新并查看简短功能说明。0.8.5及更早需手动安装一次带更新器的版本。更新保留 `%LOCALAPPDATA%\TVBox PC` 中的收藏、进度和设置；发行包不包含个人缓存、账号、密码、私人订阅或API Key。凭据使用当前Windows用户加密保存，迁移到其他Windows账号后需要重新填写。

本软件仅使用你有权访问的内容。第三方目录、视频和弹幕服务的可用性与授权由各自服务决定。

## 从源码构建

需要Windows x64、Git LFS、.NET 9 SDK与Python 3。执行 `./scripts/Build.ps1`，或用 `./scripts/Build.ps1 -Portable` 生成便携包。请保留随包.NET、WinUI、FFmpeg和Node运行库。

## 许可与来源

项目采用GPL-3.0；基于[TVBox-for-Windows](https://github.com/blueberry2568/TVBox-for-Windows)。原作者与组件许可见 [上游说明](docs/UPSTREAM.md)、[第三方声明](THIRD-PARTY-NOTICES.md) 和 `docs/licenses`。

This product uses the TMDB API but is not endorsed or certified by TMDB.
