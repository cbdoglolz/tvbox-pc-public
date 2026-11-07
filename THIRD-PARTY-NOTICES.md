# Third-Party Notices

TVBox PC is derived from TVBox for Windows (GPL-3.0) and includes or uses third-party software. Each component
remains subject to its own license.

TVBox for Windows itself is distributed under the GNU General Public
License version 3. See `LICENSE` at the repository root.

| Component | Version | License | Source |
| --- | --- | --- | --- |
| FFmpeg shared libraries | 8.1.2 | LGPL-3.0-or-later | <https://ffmpeg.org/> |
| Node.js | 24.15.0 | MIT and bundled third-party notices | <https://nodejs.org/> |
| FlyleafLib | 3.10.4 | LGPL-3.0-or-later | <https://github.com/SuRGeoNix/Flyleaf> |
| Flyleaf.FFmpeg.Bindings | 8.1.0 | LGPL-3.0-or-later | <https://www.nuget.org/packages/Flyleaf.FFmpeg.Bindings> |
| FlyleafLib.Controls.WinUI | 1.3.4 | LGPL-3.0-or-later | <https://github.com/SuRGeoNix/Flyleaf> |
| Jint | 4.14.0 | BSD-2-Clause | <https://github.com/sebastienros/jint> |
| AngleSharp | 1.5.2 | MIT | <https://github.com/AngleSharp/AngleSharp> |
| Microsoft.Data.Sqlite / Core | 9.0.22 | MIT | <https://github.com/dotnet/efcore> |
| SQLitePCLRaw (core, provider, bundle, native package) | 2.1.13 | Apache-2.0; SQLite engine public domain | <https://github.com/ericsink/SQLitePCL.raw> |
| Microsoft Windows App SDK | 2.6.5 | Microsoft Software License Terms | <https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.6.5> |

The exact FFmpeg and Node.js provenance, checksums, replacement instructions,
license texts, and source links are shipped beside those components under
`ffmpeg` and `node`.

NuGet packages and runtime components can include additional transitive
dependencies. Their notices are available from the package and upstream
project metadata.

TMDB metadata and images, when optionally enabled, come from The Movie Database.
This product uses the TMDB API but is not endorsed or certified by TMDB.
The unmodified TMDB logo `assets/icons/tmdb.svg` comes from its official developer
documentation (`https://files.readme.io/29c6fee-blue_short.svg`). Attribution and
branding requirements: https://developer.themoviedb.org/docs/faq.
The logo remains a TMDB trademark and is not relicensed under this project's GPL.
Microsoft.Data.Sqlite and SQLitePCLRaw license texts are in `docs/licenses`.
SQLitePCLRaw copyright 2014-2024 SourceGear, LLC.
IMDb scores are not distributed by this version; a validated external ID only
enables a link to the corresponding IMDb title page.

The Douban logo `assets/icons/douban.png` is the unchanged image embedded in
Douban's official home stylesheet (`https://asset.doubanio.com/cuphead/sns-static/anony-home/css/index.bd71e.css`).
The Netflix N symbol `assets/icons/netflix.svg` preserves the paths and colors
served by `https://www.netflix.com/`; brand guidance: https://brand.netflix.com/en/assets/logos/.
These marks identify the corresponding discovery views and remain their owners'
trademarks; they are not relicensed under GPL and do not imply endorsement.
The Netflix view uses Douban's Netflix-tagged catalog, not Netflix's official charts.

### Netflix horizontal wordmark (0.6.0)

0.8.1 adds `assets/icons/netflix-wordmark.svg`, a scalable rendering of the Netflix wordmark from https://commons.wikimedia.org/wiki/File:Netflix_2015_logo.svg (asset URL: https://upload.wikimedia.org/wikipedia/commons/0/08/Netflix_2015_logo.svg). It replaces the cropped JPEG in the navigation UI; the unused 0.6.0 JPEG is no longer bundled from 0.8.1. Netflix retains its trademark rights. The UI animation is locally implemented with composition transforms; it does not embed an official opening video.

`assets/icons/netflix-wordmark.jpg` is the unmodified Wordmark preview from the official Netflix Brand Site, https://brand.netflix.com/en/assets/logos/ (public page data: https://brand.netflix.com/api/en/page/logos). Original asset: https://images.ctfassets.net/y2ske730sjqp/1aONibCke6niZhgPxuiilC/2c401b05a07288746ddf3bd3943fbc76/BrandAssets_Logos_01-Wordmark.jpg . Netflix trademarks remain owned by Netflix; identification of a catalogue tag does not imply endorsement or access to Netflix playback.


## IMDb 品牌标志（0.7.0）

`assets/icons/imdb.png` 为 IMDb 官方品牌工具包中的 `IMDb_PrimaryLogo_Gold.png`，保持原始文件字节，用于标识作品资料的外部提供方，不表示 IMDb 背书或提供了独立评分 API。
来源：https://brand.imdb.com/ ，官方工具包：https://m.media-amazon.com/images/G/01/IMDb/brand/guidelines/imdb/IMDb_BrandToolkit_061925.zip 。IMDb 标志及商标权归其权利人，商标不因本项目的 GPL 许可证而重新授权。


## 弹幕数据及公共代理（0.8.2）

弹幕数据来自弹弹play开放弹幕网络：https://www.dandanplay.com/ ，官方API接入说明：https://doc.dandanplay.com/open/ 。本程序没有内置弹弹play官方应用凭据，也不冒用其他应用的AppId/AppSecret。

自动弹幕可通过 uosc_danmaku 作者公开提供的代理 `https://danmaku-api.152468.xyz` 查询；代理来源和缓存/限流说明：https://github.com/Tony15246/uosc_danmaku 。本项目自行实现兼容HTTP客户端，没有分发该项目的Lua脚本。请求标识为TVBoxPC；启用后发送作品名称和集数，不上传视频、账号或历史文件。公共代理由第三方维护，不保证持续可用；设置可关闭或替换。B站正片弹幕作为回退来源，不代表官方合作。


## 作品背景与透明标题图（0.8.3）

在用户可选启用TMDB资料时，作品海报、横向背景及透明标题图片通过TMDB图片API获取并缓存在用户资料目录；这些是作品图片资源，不是本项目制作或重新许可的字体。图片语言优先级参考 https://developer.themoviedb.org/docs/image-languages ，图片接口见 https://developer.themoviedb.org/reference/movie-images 。保留原TMDB归属声明；并非每部作品都提供中文海报或标题图片。本版没有把查询所得影视图片或用户缓存打进程序包。
