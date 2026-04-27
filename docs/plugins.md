# 插件开发

## 给用户的安全说明

只安装可信来源的主程序和插件。不要随意安装第三方改版主程序，也不要把未知 DLL 放入 `plugins` 目录。

插件与主程序运行在同一进程内，拥有和主程序相近的本机权限。插件清单用于加载前审核：插件必须声明需要访问的网站，且域名经用户配置批准后才会加载。它不是操作系统级沙箱，不能保证已加载的恶意插件不会做其他事情。

插件用于扩展四类能力：

- `IOcrServicePlugin`：替换 OCR 服务。
- `ITranslationServicePlugin`：替换翻译服务。
- `IImageProcessingPlugin`：在 OCR 前处理截图。
- `ITextProcessingPlugin`：在 OCR 后处理文本区域。

插件 DLL 放入程序目录下的 `plugins` 文件夹。程序启动或保存配置后会加载插件。插件类需要有无参构造函数；同一个类可以实现多个接口。`Order` 越小越早执行。

## 权限清单

每个插件 DLL 默认必须配套同名清单文件。例如：

```text
plugins/
  MyPlugin.dll
  MyPlugin.plugin.json
```

清单声明插件需要访问的网站：

```json
{
  "id": "my-plugin",
  "name": "My Plugin",
  "network": {
    "hosts": [
      "api.example.com"
    ]
  }
}
```

用户需要在配置中批准域名后，插件才会加载：

```json
{
  "plugins": {
    "enabled": true,
    "directory": "plugins",
    "requireManifest": true,
    "approvedNetworkHosts": "api.example.com; *.trusted.example"
  }
}
```

如果插件不需要联网，`hosts` 留空即可。启用 `requireManifest` 后，程序只把带同名 `.plugin.json` 的 DLL 视为插件入口；同目录下的依赖 DLL 可被插件引用，但不会被当作插件主动加载。清单无效、或声明域名未被批准时，程序会跳过该插件 DLL。该约束只作用于插件加载；内置 HTTP/AI 翻译的访问地址仍由用户自己的翻译配置决定。

注意：当前插件运行在主程序进程内。清单机制可以阻止未声明或未批准的插件被加载，但不能替代操作系统级沙箱；请只安装来源可信的插件。

## 启用服务插件

服务插件按名称选择。可以把配置写成插件的 `Name`、类名或完整类型名：

```json
{
  "ocr": {
    "provider": "MyOcr"
  },
  "translation": {
    "provider": "MyTranslator"
  }
}
```

## 最小示例

```csharp
using Ocr2Tran.Core;
using Ocr2Tran.Plugins;

public sealed class MyTranslator : ITranslationServicePlugin
{
    public string Name => "MyTranslator";
    public Task<string> TranslateAsync(string text, CancellationToken cancellationToken)
    {
        return Task.FromResult("[translated] " + text);
    }
}

public sealed class MyTextFilter : ITextProcessingPlugin
{
    public string Name => "MyTextFilter";
    public ValueTask<IReadOnlyList<TextRegion>> ProcessAsync(
        IReadOnlyList<TextRegion> regions,
        PluginContext context,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<IReadOnlyList<TextRegion>>(
            regions.Select(region => region with { Text = region.Text.Trim() }).ToArray());
    }
}
```

图片处理插件返回原 `CapturedScreen` 表示不替换图片；如果返回新的 `CapturedScreen`，程序会负责释放中间图片。插件依赖 DLL 可一并放在 `plugins` 目录中。

## GPL 影响

本项目使用 GPL-3.0-or-later。当前插件是进程内 DLL，并直接实现主程序接口、调用主程序类型、共享 `TextRegion` / `CapturedScreen` 等数据结构。按 GNU GPL FAQ 对动态链接插件的解释，这类插件和主程序通常会被视为一个组合程序；如果对外分发插件，插件一般应使用 GPL-3.0-or-later 或 GPL 兼容许可证，并按 GPL 要求提供对应源码。

只在本机或组织内部自用而不分发，通常不会触发 GPL 的源码分发义务。若插件改为独立进程，通过命令行、标准输入输出或网络协议与主程序进行松耦合通信，许可判断可能不同。

这不是法律意见；准备分发闭源或商业插件时，应咨询熟悉开源许可证的律师。
