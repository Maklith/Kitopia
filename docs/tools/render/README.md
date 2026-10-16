# Kitopia 文档截图工具

这个项目使用 Avalonia Headless 和 Skia 渲染 Kitopia 界面，不调用系统桌面截图。截图成品写入 `docs/assets/README`，输入素材放在当前目录的 `media` 下。

先构建测试程序集：

```powershell
dotnet build tests/KitopiaTest/KitopiaTest.csproj -c Debug
```

按需运行：

```powershell
dotnet run --project docs/tools/render/Render.csproj -- search
dotnet run --project docs/tools/render/Render.csproj -- search-en
dotnet run --project docs/tools/render/Render.csproj -- en
dotnet run --project docs/tools/render/Render.csproj -- rename
dotnet run --project docs/tools/render/Render.csproj -- rename-en
dotnet run --project docs/tools/render/Render.csproj -- pdf
dotnet run --project docs/tools/render/Render.csproj -- pdf-en
```

不带参数会生成完整的一组中文文档界面截图，`en` 生成完整英文截图。`search-en` 仅生成英文搜索界面；`rename` 和 `rename-en` 仅生成中英文批量重命名预览，不执行重命名；`pdf` 和 `pdf-en` 分别生成中文、英文 PDF Quick Look 截图。

项目会从测试构建输出复制 FFmpeg 工具。完整截图会在系统临时目录创建演示文件，并实际执行图片和视频压缩。可通过 `KITOPIA_SCREENSHOT_OUTPUT` 环境变量指定截图输出目录。英文图片预览输出为 `image-preview-en-20261014.png`，不会覆盖 PDF Quick Look 截图。

PDF 预览使用位于屏幕外的 WebView2，通过 `CapturePreviewAsync` 获取 PDF 内容并与 Avalonia 界面合成，不截取系统桌面。

修改仓库中的中英文 PPT 后，可从仓库根目录执行 `./docs/tools/export-readme.ps1`，直接导出 README 功能页图片。此步骤需要安装 PowerPoint，文件名默认使用当天日期；README 图片链接需同步更新。
