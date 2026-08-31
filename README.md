# DeepSeek 娘桌宠

一个纯本地的 Windows 桌宠程序。角色会待机呼吸、偶尔散步，并在单击时给出 Q 弹回应。

## 操作

- 左键拖动：移动桌宠
- 左键单击：Q 弹互动
- 右键：打开设置、切换自动散步或退出
- 设置中可调整自动散步、速度、大小、置顶、边缘吸附和开机启动

程序不读取 DeepSeek 账号，不显示余额，也不会发起网络请求。

## 开发构建

需要 Windows 和 .NET 9 SDK：

```powershell
dotnet build .\DeepSeekPet.sln -c Release
dotnet publish .\src\DeepSeekPet\DeepSeekPet.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

发布文件位于 `src\DeepSeekPet\bin\Release\net9.0-windows\win-x64\publish\`。
