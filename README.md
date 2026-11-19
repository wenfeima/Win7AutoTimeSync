# Win7 自动校时（AutoTimeSync）

Windows 7 用的轻量校时小工具。单个 exe，双击就能用，不需要装任何运行环境（Win7 自带 .NET Framework 3.5）。

## 为什么做这个

Win7 自带的"Internet 时间同步"经常报错（默认的时间服务器在国内基本连不上，误差窗口也太小），结果是系统时间慢慢跑偏，
影响 HTTPS 证书校验、代码签名、邮件时间戳这些。这个工具直接跑 SNTP，自己算偏差，不依赖 Windows 时间服务。

## 功能

- **手动同步**：点「立即同步」立刻校准
- **开机自动同步**：勾选后建一个 SYSTEM 身份的计划任务，开机 1 分钟后跑一次
- **定时自动同步**：可设每 N 小时跑一次，同样走计划任务
- **服务器下拉选择**：内置 6 个源（阿里云 / 腾讯云 / 国家授时中心 / cn.pool / 微软 / 阿里云 IP 直连）
- **手动添加服务器**：内网 NTP 的 IP 或域名直接输入添加，配置存同目录 servers.ini
- **只用所选服务器**：勾上就不自动换源，连不上直接报错（内网环境用）
- **自动换源有提示**：选中的服务器不通会明确写"选中的 X 连不上，已自动改用 Y"，不会闷着换

## 原理

标准 SNTP（RFC 5905）四时间戳算法：发请求记 t1、服务器回包时间 t2、服务器发包时间 t3、本机收到 t4，
偏差 = ((t2-t1) + (t3-t4)) / 2，同时用往返时延做合理性判断。改时间需要 SeSystemtimePrivilege，
exe 内嵌 equireAdministrator 清单，双击自动提权。

## 文件

| 文件 | 说明 |
|---|---|
| 自动校时.exe | 主程序，单文件，32 位，36864 字节 |
| 使用说明.txt | 给使用者看的傻瓜说明 |
| 自动校时_Win7.zip | exe + 说明的发布包 |
| 编译.bat | 一键重新编译（调系统自带的 csc.exe） |
| 源码/TimeSyncSource.cs | 全部源码（C#，.NET 2.0 语法，兼容 Win7） |
| 源码/app.manifest | 提权清单，声明 Win7~Win11 兼容 |

## 重新编译

双击 编译.bat，或者手动：

`at
C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe /nologo /target:winexe /platform:x86 /optimize+ /codepage:65001 ^
  /win32manifest:"源码\app.manifest" /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /out:"自动校时.exe" "源码\TimeSyncSource.cs"
`

## 命令行

`
自动校时.exe /silent            静默同步（给计划任务用，不弹窗）
自动校时.exe /dryrun            只检测偏差，不改时间
自动校时.exe /install           装定时同步计划任务
自动校时.exe /uninstall         卸载计划任务
自动校时.exe /boot:1            开机同步开/关
自动校时.exe /timer:1 /every:6  定时同步开/关，间隔小时
自动校时.exe /servers:a,b,c     指定服务器列表
`

计划任务名：Win7-AutoTimeSync（定时）、Win7-AutoTimeSync-Boot（开机），都是 SYSTEM 身份、最高权限。

## 配置与日志

同目录下自动生成，都可手工编辑：

- servers.ini — server= 当前服务器、hours= 定时间隔、custom= 自定义服务器（可多行）
- TimeSync.log — 每次同步记录用的哪台服务器、偏差多少、往返多少毫秒，最多留 200 行

## 兼容性

Win7 SP1 及以上（XP 未测试）。需要 .NET Framework 3.5，Win7 原版自带；Win10/11 是系统可选组件，可能需要在
「启用或关闭 Windows 功能」里勾上。

## 已知限制

- 只支持 IPv4 和域名解析，不处理 IPv6-only 环境
- 时间服务器不通时默认会自动往下试，最长可能等 3 秒 × 服务器数量；勾「只用所选服务器」可以避免
- 没有做时间平滑过渡，偏差大时直接跳变（跟 w32tm 的 slew 不同）

## License

MIT