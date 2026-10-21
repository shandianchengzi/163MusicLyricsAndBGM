# 歌词与伴奏提取

跨平台版本新增菜单 **伴奏提取**：

1. 从 [vocal-separate Releases](https://github.com/jianchang512/vocal-separate/releases) 下载并解压，运行 `start.exe`；源码部署见该项目说明，需安装 ffmpeg 和 `2stems` 模型。
2. 在本程序菜单打开“伴奏提取”，默认服务地址为 `http://127.0.0.1:9999`。
3. 选择“导入音频并提取伴奏”（MP3/WAV/FLAC），或先搜索歌曲，再选择“下载当前歌曲并提取伴奏”。
4. 选择保存位置，等待生成 `歌曲名_伴奏.wav`。

歌曲下载复用搜索结果中的平台直链，受歌曲权限、链接有效期和平台限制影响；没有直链或下载失败时请导入本地音频。直链若仅提供试听片段，提取结果也仅包含该片段。不会绕过付费或登录限制。音频发送到填写的分离服务，默认在本机处理；模型和 Python 环境由 vocal-separate 提供，本程序不重复打包。取消会停止客户端请求，服务端已开始的计算可能继续运行。请避免同时在分离服务中运行其他任务。

GitHub Actions 的 **Build and test** 构建提供 Windows x64 程序压缩包（Artifacts）。旧版 `archive-winform` 不包含此功能。

---

[![Release](https://img.shields.io/github/v/release/jitwxs/163MusicLyrics.svg)](https://github.com/jitwxs/163MusicLyrics/releases)
![Star](https://badgen.net/github/stars/jitwxs/163MusicLyrics)
![GitHub all releases](https://img.shields.io/github/downloads/jitwxs/163MusicLyrics/total)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![Email](https://img.shields.io/badge/Email-jitwxs%40foxmail.com-brightgreen)](#)

### How To Use

[👉163MusicLyrics User Guide](https://github.com/jitwxs/163MusicLyrics/wiki)

- [x] 支持网易云音乐、QQ音乐两家音乐提供商 Two providers: NetEase Cloud Music and QQ Music are supported
- [x] 支持单曲、专辑、歌单三种查询类别，ID 与链接精确查询 Support 3 search types: Song, Album, Playlist; search by ID or full URL for precise queries
- [x] 支持关键字模糊查询并提供结果选择窗口 Keyword-based fuzzy search with selectable result list
- [x] 支持批量查询与目录扫描批量导入 Support batch search and drive/folder scan input
- [x] 支持歌词结果保存与批量保存管理 Save lyrics results and manage batch saving
- [x] 支持自动获取并下载歌曲试听直链 Support retrieving and downloading preview song links
- [x] 支持歌曲直链在线播放与进度控制 In-app playback of preview links with progress control
- [x] 支持下载歌曲封面图片 Support downloading song cover images
- [x] 支持歌词格式转换（LRC <-> SRT）Built-in LRC <-> SRT format conversion
- [x] 支持多种歌词组织与渲染（原文/译文组合）Support multiple original/translated lyrics compositions
- [x] 支持百度翻译、彩云小译自动翻译歌词 Support Baidu Translate and CaiYun Translate APIs
- [x] 支持本地缓存（歌词与直链）Support local cache for lyrics and song links
- [x] 支持应用设置与主题切换 App settings with theme switching

### Downloads

进入 [GitHub Release](https://github.com/jitwxs/163MusicLyrics/releases)
页下载最新版本即可，您可点击 [ChangeLog](https://github.com/jitwxs/163MusicLyrics/wiki/ChangeLog) 查看不同版本的变更。

Enter the [Github Release](https://github.com/jitwxs/163musiclyrics/releses) page to download the latest version, you can
click on [Changelog](https://github.com/jitwxs/163musiclyrics/wiki/changelog) to view the changes in different versions.

<p align="center">
  <img src="./images/v7_3_home.png" width="32%" />
  <img src="./images/v7_3_search.png" width="32%" />
  <img src="./images/v7_3_download.png" width="32%" />
</p>

### Contribution

您可访问 [163MusicLyrics Projects](https://github.com/users/jitwxs/projects/1) 了解项目当前阶段的工作计划，如您愿意在其中贡献力量，您可以：

- 将您的想法或发现的 bug 填写在 [issuses](https://github.com/jitwxs/163MusicLyrics/issues) 中，我将不定期的进行处理
- Fork 项目，并提交您的 pull requests

You can access [163MusiclyRics Projects](https://github.com/Users/jitwxs/projects/1) to learn about the current state of
the project, if you are willing to contribute, you can:

- Write feature suggestions or bug reports to [Issuses](https://github.com/jitwxs/163musiclyrics/issues), I will deal with it sometimes
- Fork the project, and submit your pull requests

### Stargazers over time

[![Stargazers over time](https://starchart.cc/jitwxs/163MusicLyrics.svg)](https://starchart.cc/jitwxs/163MusicLyrics)

### Reference

本项目部分功能借鉴以下项目 Some features of this project took reference from other projects：

- https://github.com/Binaryify/NeteaseCloudMusicApi
- https://github.com/Rain120/qq-music-api
- https://github.com/jsososo/QQMusicApi
- https://github.com/ElliottSilence/LyricCapture
- https://github.com/xmcp/QRCD
- https://github.com/ivanakcheurov/ntextcat

第三方使用介绍视频 Third party use guide videos

- https://www.bilibili.com/video/BV19R4y197on

### Donate

如果本项目为您带来方便，欢迎 Star 来让更多人发现和使用它。本项目为个人维护项目，不收取任何费用，如果您愿意请作者喝杯咖啡话，欢迎打赏。

If this project brings you convenience, you're welcome to star it to let more people discover and use it. This project is
maintained personally, all features are free. If you would like to treat the creator with a bottle of coke, you're welcome to donate.

如您选择打赏，记得备注您的昵称，我将不定期为您登记到  [DONATE.md](./DONATE.md) 页面中。

If you choose to donate, remember to note your nickname, I will register it to  [DONATE.md](./DONATE.md) at free time.

<div align="center">
    <img src="https://cdn.jsdelivr.net/gh/jitwxs/cdn/blog/configuration/alipay_donate_full.jpg" height="200" style="float:left;margin-right:20px;margin-left: 310px">
    <img src="https://cdn.jsdelivr.net/gh/jitwxs/cdn/blog/configuration/wechat_donate_full.jpg" height="200" style="float:left">
</div>

最后，感谢 JetBrains 为开源项目提供免费的 IDE 支持。

Lastly, thanks to JetBrains for sponsoring the IDE for open-sources project.

![Rider logo](https://resources.jetbrains.com/storage/products/company/brand/logos/Rider_icon.svg)
