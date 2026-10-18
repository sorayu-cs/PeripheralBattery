# PeripheralBattery

Windows 10/11 上的 2.4G 无线外设电量监控小工具。常驻托盘、开机自启、深色卡片式面板，支持多设备与自定义协议扩展。

## 功能

- 托盘常驻：启动无窗口，图标默认进入任务栏隐藏图标区（`^`）
- 双层电量环图标：外环第一台设备、内环第二台设备，按电量在绿 / 橙 / 红之间切换
- 深色主面板：设备卡片、大号百分比、渐变进度条、整分自动刷新
- 设备断开时保留最后一次电量，并标注断开时间
- 开机自启：启动文件夹快捷方式，任务管理器「启动」里可管理
- 设置界面：添加 / 编辑 / 删除设备、分钟或秒级刷新间隔、开机自启开关

## 已支持的设备

| 设备 | VID:PID | 协议 |
|---|---|---|
| 罗技 Lightspeed 接收器（GPW 等） | `046D:C547` | HID++ 2.0，特性 `0x1004` 电池电压 |
| 迈从 K99 V3 2.4G | `3837:3033` | QHW 无线，`AA 05` 状态帧 |
| 雷蛇鼠标 | 任意 | 实验性，可在设置中校准 |
| 其他设备 | 任意 | 自定义 HID 报文（发送十六进制 + 偏移 + 倍率） |

新增设备只需在设置里添加 VID/PID、选择协议并填写参数，不需要改代码。

## 运行要求

- Windows 10 / 11 x64
- .NET 10 Desktop Runtime（自行构建时）
- 发布的自包含单文件版本无需安装运行时

## 构建

```powershell
cd src\PeripheralBattery
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ..\..\dist
```

## 使用

- 双击 exe：托盘常驻
- `--dashboard`：打开主面板
- `--settings`：打开设置（已运行时会把窗口唤到前台）
- `--once`：读取一次电量并输出到控制台

配置保存在 `%APPDATA%\PeripheralBattery\config.json`。

## 隐私

- 不联网：没有任何 HTTP / socket / 遥测调用，电量读取全部在本地 HID 层完成
- 不写日志：正常运行不产生日志文件
- 不上传：设备名称、VID/PID 只在本机配置里使用
- 仓库内不包含任何用户桌面截图、用户名或本机路径

## 目录结构

```
src/PeripheralBattery/
├── Program.cs              入口、单实例、--once / --dashboard / --settings
├── Core/                   配置、设备模型、读取调度、开机自启
├── Hid/                    setupapi / hid.dll 的 ctypes 封装与枚举
├── Protocols/              罗技 HID++、迈从 QHW、雷蛇、自定义 HID
├── Tray/                   托盘图标与菜单、单实例消息
└── UI/                     主面板、设备卡片、设置、图标绘制
tools/IconMaker.cs          多尺寸 ICO 生成工具
```

## License

MIT
