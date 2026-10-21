内置伴奏分离引擎版

- 已内置 vocal-separate 的运行环境、FFmpeg 和 2stems 模型。
- 完整解压后运行 MusicLyricApp.exe，菜单打开“伴奏提取”即可使用。
- 无需另装 Python/CUDA，无需单独启动 start.exe，无需配置服务地址。
- 支持导入 MP3/WAV/FLAC，或下载当前搜索歌曲后提取 WAV 伴奏。
- 默认完全在本机分离，不启动浏览器或 HTTP 服务，不在首次使用时联网下载模型。
- 取消任务、关闭窗口或退出程序时自动停止内置引擎及子进程。

下载 MusicLyricsAndBGM-win-x64.zip 并完整解压，保留 engine 文件夹。
运行环境和模型已随包提供，下载体积较大；相比上一版无需另行安装分离工具。
歌曲下载依然受平台权限及直链有效期限制，失败时可导入本地歌曲。

发布前已使用打包后的 Windows 引擎实际分离合成 MP3，验证中文路径、有效 WAV 输出和取消后的进程清理；该测试不代表所有歌曲的分离音质。

引擎基于 jianchang512/vocal-separate v0.0.4（GPL-3.0）。上游源码见附件 vocal-separate-v0.0.4-source.zip；修改后的入口、打包脚本和许可位于包内 engine 目录及本仓库 tools/vocal-engine 目录。第三方组件保留各自许可证。
