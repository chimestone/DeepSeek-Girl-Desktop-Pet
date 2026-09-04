# DeepSeek 娘桌宠

一个纯本地的 Windows 桌宠程序。角色会待机呼吸、偶尔散步，并在单击时给出 Q 弹回应。

内置 DeepSeek 娘、咕咕嘎嘎、菲比、啥子龙和睦子米五个角色，所有角色采用统一的简约 Q 版画风，并保留各自的原版配色与特征。

## 操作

- 左键拖动：移动桌宠
- 左键单击：Q 弹互动
- 左键双击：显示或隐藏与桌宠同比缩放的半透明助手气泡
- 右键：打开设置、切换自动散步或退出
- 设置顶部：通过角色预览卡片切换桌宠，切换时使用短暂交叉淡入淡出
- 设置中还可调整自动散步、速度、大小、置顶、边缘吸附和开机启动
- 简要助手：以逐字动画显示提示，可查询城市当前天气、使用可调时长的番茄钟、管理本地待办事项

程序不读取 DeepSeek 账号，不显示余额。设置与待办数据仅保存在本机；只有主动查询天气时会访问 Open-Meteo 的地理编码和天气接口。

## 开发构建

需要 Windows 和 .NET 9 SDK：

```powershell
dotnet build .\DeepSeekPet.sln -c Release
dotnet publish .\src\DeepSeekPet\DeepSeekPet.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

发布文件位于 `src\DeepSeekPet\bin\Release\net9.0-windows\win-x64\publish\`。
