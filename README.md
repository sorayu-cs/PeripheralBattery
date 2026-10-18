# 外设电量监控（PeripheralBattery）

Windows 10/11 上的 2.4G 无线外设电量监控小工具。开机自启、常驻托盘、启动时自动隐藏到通知区域（Windows 11 默认进入“隐藏的图标”区）。

本项目参考了 [yszyszysz233/2.4G-battery-viewer](https://github.com/yszyszysz233/2.4G-battery-viewer) 的思路（纯系统 API、无第三方依赖、托盘常驻、低 CPU），并用 C# / .NET 重写，增加了设备配置界面与多协议扩展。

## 已支持的设备

| 设备 | VID:PID | 协议 | 说明 |
|---|---|---|---|
| 罗技 G Pro Wireless（Lightspeed 接收器） | `046D:C547` | 罗技 HID++ 2.0 | 长报文接口 `usage FF00/0002`，读取特性 `0x1004` 电池电压，按 GPW 曲线（3.500–3.944 V 线性）换算，实测 3855 mV → 80% |
| 迈从 K99 V3（2.4G 接收器） | `3837:3033` | 迈从 QHW 无线 | 接口 `usage 0001/0000`，发送 `55 05 00 38 38`，取 `AA 05` 状态帧第 31 字节（随电量变化，实测 85/86），按官方 10% 分档显示（85–94 → 90%） |
| 雷蛇鼠标 | 任意 | 雷蛇 HID（实验性） | 默认 `02 07 00 00`，偏移 8；未验证型号请用“自定义 HID 报文”适配 |
| 其他设备 | 任意 | 自定义 HID 报文 | 可配置发送十六进制、报告 ID、回复前缀、电量偏移与倍率 |

## 运行要求

- Windows 10 / 11（x64）
- .NET 10 Desktop Runtime（本机已随 .NET SDK 安装）
- 单文件 exe 约 220 KB，不写日志、不联网

## 使用

- 双击桌面「外设电量监控」→ 打开深色卡片式主面板，托盘同时常驻。
- 主面板每张卡片显示：设备图标、名称、数据来源（协议/电压/原始值）、大号电量、渐变进度条。
- 主面板右下角：「设置」管理设备与刷新间隔，「退出」结束程序。
- 托盘图标是电量环 + 数字（绿 ≥60 / 黄 30–59 / 红 <30）。
- 左键单击图标：立即刷新并弹出气泡；双击：打开主面板；右键：设备电量、打开面板、刷新、设置、开机自启、退出。
- 图标默认在任务栏右下角的隐藏图标区（`^`）。如果看不到，在「任务栏设置 → 其他系统托盘图标」中打开本程序。

## 配置文件

`%APPDATA%\PeripheralBattery\config.json`

```json
{
  "refreshSeconds": 60,
  "autostart": true,
  "startMinimized": true,
  "devices": [
    {
      "name": "罗技 G Pro Wireless",
      "vid": 1737,
      "pid": 50503,
      "protocol": "logitech_hidpp",
      "options": { "deviceIndex": "1", "usagePage": "FF00", "usage": "0002" }
    },
    {
      "name": "迈从 K99 V3",
      "vid": 14391,
      "pid": 12339,
      "protocol": "mchose_qhw",
      "options": { "usagePage": "0001", "usage": "0000", "batteryOffset": "1" }
    }
  ]
}
```

## 迈从电量校准

迈从状态帧 `AA 05` 的返回格式为：

```
AA 05 00 F4 00 00 00 <len=0x50> <80 字节数据>
data[31] = 0x56（86）等，随电量变化
```

软件默认取 `data[31]`，并按 M HUB 的 10% 分档显示（85–94 → 90%，75–84 → 80%）。如果与官方驱动不一致：

1. 打开设置 → 双击「迈从 K99 V3」；
2. 调整参数：
   - `batteryOffset=31`：电量在 `data` 中的字节下标；
   - `batteryScale=1.0`：倍率（一般不用改）；
3. 点「测试读取」查看结果，保存。

> 说明：M HUB 网页把电量按 10% 分档显示，因此软件显示的是与官方一致的档位值。

## 构建

```powershell
cd src\PeripheralBattery
dotnet publish -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -o ..\..\dist
```

## 卸载

1. 托盘右键 → 退出；
2. 删除 `%LOCALAPPDATA%\Programs\PeripheralBattery`；
3. 删除「启动」文件夹里的 `外设电量监控.lnk` 和桌面快捷方式；
4. 可选：删除 `%APPDATA%\PeripheralBattery`。

## 已知限制

- 2.4G 键盘休眠时 dongle 不应答，按一下按键再刷新即可。
- 罗技设备被 G HUB 同时占用时可能超时，移动鼠标唤醒后重试。
- 雷蛇协议为实验性实现，不同型号偏移可能不同，建议用自定义报文校准。
