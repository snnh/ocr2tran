# ocr2tran

`ocr2tran` 是一个 Windows 本地 OCR + 原位叠加翻译工具。它常驻托盘，通过快捷键截取屏幕文字，使用内置 ONNX Runtime 或 PaddleOCR CLI 识别文本，再把原文或译文按原屏幕位置透明覆盖显示。

## 亮点

- 托盘后台待命和前台控制面板
- 图形化配置面板，可修改热键、OCR、翻译、覆盖层和性能参数
- 全局快捷键：单次 OCR、单次 OCR+翻译、区域框选 OCR、框选自动翻译、自动 OCR、自动翻译循环、退出
- 置顶、点击穿透覆盖层，背景色和不透明度支持连续调节
- OCR 后端：
  - 内置 ONNX Runtime，导入 `det.onnx`、`rec.onnx` 即可使用；支持 `rec.onnx` 内置 `character` 字典，也兼容外置字典
  - PaddleOCR CLI 兼容模式，支持导入 PPOCR Paddle 推理模型目录
- 翻译后端：`noop`、百度翻译、Google Cloud Translation、通用 HTTP/AI
- HTTP/AI 翻译支持普通 JSON、SSE、NDJSON 流式响应
- OCR 图像预处理、文本后处理、翻译缓存、截图未变化跳过重复 OCR/翻译
- 插件扩展：自定义 OCR 服务、翻译服务、图片处理和文本处理，支持网络访问清单和用户批准
- CPU 线程数、CPU 亲和性和内存软上限配置

## 快速开始

运行环境：

- Windows
- .NET SDK 10

```powershell
dotnet restore .\src\Ocr2Tran\Ocr2Tran.csproj
dotnet run --project .\src\Ocr2Tran\Ocr2Tran.csproj
```

首次运行会从程序目录读取或生成 `appsettings.json`。开发默认配置在 [src/Ocr2Tran/appsettings.json](src/Ocr2Tran/appsettings.json)。

发布包解压后可直接运行 `Ocr2Tran.exe`。如果要替换旧版本，请先退出托盘里的旧进程，避免 DLL 被占用。

## 基本使用

1. 启动 `Ocr2Tran.exe`，托盘会出现程序图标。
2. 右键托盘图标打开“控制面板”或“配置”。
3. 先导入 ONNX 模型，再点击“测试 OCR”确认识别正常。
4. 按 `Ctrl+Alt+T` 做一次全屏 OCR + 翻译，或按 `Ctrl+Alt+Y` 框选区域后翻译。
5. 需要持续识别固定区域时，按 `Ctrl+Alt+U`，框选一次后进入自动翻译循环；再次按快捷键暂停。

## 推荐 OCR 使用方式

优先使用 ONNX 后端。准备模型目录：

```text
ONNX-OCR/
  det.onnx
  rec.onnx              # 推荐把字典写入 character metadata
  ppocr_keys_v1.txt     # 可选；字典已写入 rec.onnx 的 character metadata 时不需要
```

然后在控制面板中点击“导入 ONNX 模型”，选择 `ONNX-OCR` 目录，再点击“测试 OCR”或按 `Ctrl+Alt+O`。

如果已有 PaddleOCR/PPOCR Paddle 推理模型，也可以使用“导入 PPOCR 模型”。该模式仍需要本机可执行 `paddleocr.exe`。

ONNX 识别结果如果出现“文字框位置正常但文本全是乱码”，通常是识别模型和字典不匹配，或内置字典没有按 `character` metadata 写入。详见 [PPOCR/ONNX 文件准备指南](docs/ppocr-files.md)。

## 翻译服务

`ocr2tran` 可接入 `noop`、百度翻译、Google Cloud Translation，以及任何兼容 OpenAI Chat Completions 的 HTTP/AI 翻译服务。你也可以使用本地模型服务或自建代理。

如果你需要开箱即用的在线大模型翻译服务，可以参考以下服务商：

| 服务商 | 适合场景 | 接入方式 |
| --- | --- | --- |
| DeepSeek(官方) | 游戏文本、实时翻译、通用 OCR 翻译 | [获取 API Key](https://platform.deepseek.com/api_keys)  (此链接不含任何商业化推广和邀请有礼)|
| 智谱ai(官方) | 游戏文本、实时翻译、通用 OCR 翻译 | [codingplan](https://www.bigmodel.cn/glm-coding?ic=WZCG1QBXSE) |

以上推荐可能包含赞助或推广链接。`ocr2tran` 不会默认向任何第三方服务发送屏幕内容；只有在你主动配置对应翻译服务后，OCR 文本才会发送到该服务。

## 默认快捷键

- `Ctrl+Alt+O`：单次 OCR
- `Ctrl+Alt+T`：单次 OCR 并翻译
- `Ctrl+Alt+R`：框选区域 OCR
- `Ctrl+Alt+Y`：框选区域 OCR 并翻译
- `Ctrl+Alt+A`：启动/暂停自动 OCR
- `Ctrl+Alt+S`：启动/暂停自动翻译循环
- `Ctrl+Alt+U`：启动/暂停框选区域自动翻译
- `Ctrl+Alt+Q`：彻底关闭程序

## 常见调节

- 识别小字差：在配置面板中调高 `ocr.imagePreprocessing.scale`，或适当提高 `contrast`。
- 复杂界面误识别太多：提高 `ocr.postProcessing.minConfidence`、`minRegionArea`，或降低 `maxRegions`。
- 正文被过滤：降低上述阈值，或关闭 `dropShortIsolatedText`。
- 覆盖层太挡内容：调低 `overlay.opacity`，或修改 `overlay.background` 为更浅/更深的 HTML 颜色。
- 热键无效：可能被其他程序占用，程序会跳过占用的热键，可在“配置”里换一个组合。

## 安全提醒

只安装可信来源的主程序和插件。不要随意安装第三方改版主程序，也不要把未知 DLL 放入 `plugins` 目录。插件和主程序运行在同一进程内，恶意插件可以影响识别、翻译结果和本机数据安全。

插件默认需要清单声明网络访问域名，并由用户在配置中批准后才会加载。该机制用于插件准入控制，不会限制内置 HTTP/AI 翻译配置。

## 文档

- [配置说明](docs/configuration.md)
- [OCR 和翻译接入](docs/integrations.md)
- [PPOCR 文件准备指南](docs/ppocr-files.md)
- [插件开发](docs/plugins.md)
- [架构说明](docs/architecture.md)

## 致谢

`ocr2tran` 的 OCR 能力受益于以下开源项目和社区：

- [PaddleOCR / PPOCR](https://github.com/PaddlePaddle/PaddleOCR)：提供优秀的文本检测、文本识别模型和 OCR 工具生态。
- [RapidOCR](https://github.com/RapidAI/RapidOCR)：提供易用的 OCR 推理、ONNX 部署经验和模型集成参考。

本项目开发过程中也使用了 DeepSeek V4 和 GitHub Copilot 辅助代码编写、调试和文档整理。

感谢这些项目、工具及其贡献者，让轻量、本地、可集成的 OCR 工作流变得更容易实现。

## 构建

```powershell
dotnet restore .\src\Ocr2Tran\Ocr2Tran.csproj
dotnet build .\src\Ocr2Tran\Ocr2Tran.csproj --configuration Release --no-restore
dotnet publish .\src\Ocr2Tran\Ocr2Tran.csproj --configuration Release --runtime win-x64 --self-contained false --output .\artifacts\ocr2tran
```

发布包应包含 `LICENSE` 和 `NOTICE`：

```powershell
Copy-Item .\LICENSE .\artifacts\ocr2tran\LICENSE -Force
Copy-Item .\NOTICE .\artifacts\ocr2tran\NOTICE -Force
Compress-Archive -Path .\artifacts\ocr2tran\* -DestinationPath .\artifacts\ocr2tran-release.zip -Force
```

## 开源协议

本项目采用 [GPL-3.0-or-later](LICENSE)。

- 允许商用、收费分发和商业内部使用。
- 分发修改版时，必须以 GPL-3.0-or-later 或兼容 GPL 后续版本开源对应源码。
- 分发原版或修改版时，必须保留 `LICENSE`、`NOTICE` 和来源标注，说明基于 ocr2tran。
