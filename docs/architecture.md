# 架构说明

`ocr2tran` 是一个 Windows WinForms 托盘应用，主流程是“截图 -> OCR -> 翻译 -> 覆盖层绘制”。

## 模块

- `Windows`：托盘菜单、全局快捷键、控制面板、透明覆盖层。
- `App`：配置加载、热键动作、OCR/翻译编排。
- `Core`：屏幕截图、文本区域模型、翻译缓存。
- `Ocr`：OCR 引擎接口、模型导入扫描器、ONNX Runtime 后端、PaddleOCR CLI 适配器。
- `Translation`：翻译接口、百度/Google/HTTP/noop 后端、HTTP 模板和流式解析。
- `Runtime`：线程、CPU 亲和性和内存软上限控制。

## 启动流程

1. `Program.Main` 加载 `appsettings.json`。
2. `TrayAppContext` 创建托盘图标、控制面板、覆盖层和热键管理器。
3. `OcrTranslationCoordinator` 根据配置创建 OCR 引擎和翻译器。
4. 用户通过快捷键、托盘菜单或控制面板触发 OCR，也可以框选固定区域后进入自动翻译循环。

## OCR 流程

1. 截取虚拟屏幕或用户框选的固定区域，支持多显示器。截图前会临时隐藏覆盖层，避免识别到自己绘制的文本。
2. 计算截图 hash；截图未变化时复用上一轮 OCR 结果。
3. 对截图执行可选图像预处理：放大、灰度、对比度和亮度调整。预处理图像保留源尺寸映射，OCR 输出框会映射回真实屏幕坐标。
4. 调用 OCR 后端：
   - `onnx`：内置 ONNX Runtime，加载检测模型、识别模型和可选方向分类模型。
   - `paddleCli`：启动 `paddleocr.exe` 或兼容包装程序。
   - 其他值：进入 `noop` 调试 OCR。
5. 对 OCR 结果执行文本后处理：清洗控制字符、过滤低置信度/小框/噪声块、去除重复框、合并同一行邻近文本，并可把相邻且对齐的多行合并为同一个文本块。
6. 得到 `TextRegion` 列表，每项包含屏幕坐标、原文、译文和置信度。
7. 覆盖层按 `TextRegion.Bounds` 在原屏幕位置绘制文本。

## 翻译流程

翻译开启时，协调器按 `TextRegion` 调用翻译后端。OCR 后处理可能已把同一块的多行文本合并到一个 `TextRegion`，此时该块完整文本会作为一次普通翻译请求发送；翻译层不使用带标记的批量拆分协议。缓存 key 包含 provider、源语言、目标语言和原文。相同文本再次出现时直接复用译文。

HTTP/AI 翻译支持：

- 普通 JSON 响应字段路径
- SSE 流式响应
- NDJSON 流式响应

## 覆盖层

`OverlayForm` 是无边框、置顶、点击穿透、不抢焦点的透明窗口。覆盖层覆盖 Windows 虚拟屏幕，并把全局屏幕坐标转换为窗口本地坐标绘制。

覆盖层使用 Windows layered window 和 32 位 ARGB 位图提交绘制结果，因此背景色和 `overlay.opacity` 可以做真实中间态透明合成。旧的 `TransparencyKey` 方案只能实现“挖空或不挖空”，不适合半透明背景。

## 性能控制

- 默认 2 个 CPU 线程。
- ONNX Runtime 可配置 `intraOpNumThreads`。
- Paddle CLI 模式会继承 `OMP_NUM_THREADS`、`MKL_NUM_THREADS`、`OPENBLAS_NUM_THREADS` 等环境变量。
- 可配置 CPU 亲和性。
- 内存软上限通过主动 GC 尝试缓解，不是硬限制。

## 当前边界

- ONNX 后端实现 DB 风格检测 + 可选方向分类 + CTC 识别，当前文本框为轴对齐矩形。
- Paddle CLI 模式仍需要本机存在 `paddleocr.exe` 或兼容程序。
- 配置文件当前位于程序目录；安装到只读目录时建议后续迁移到用户配置目录。
- 图形化配置应用和 OCR 引擎重载使用异步等待，避免正在 OCR 时保存配置导致 UI 线程卡死。
