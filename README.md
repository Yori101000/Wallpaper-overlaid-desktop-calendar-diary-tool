# 透明日历

一个 Windows 透明桌面日历应用，设计为覆盖在已有 Wallpaper Engine 壁纸上方使用。

## 运行

在工程目录执行：

```powershell
dotnet run --project .\透明日历.csproj
```

也可以双击 `run.ps1`，或在 PowerShell 中运行：

```powershell
.\run.ps1
```

如果 Windows 禁止运行 PowerShell 脚本，双击 `启动透明日历.bat`。

## 数据位置

日历和设置数据保存在：

```text
%AppData%\透明日历\
```

应用设置窗口里有“打开本地存储位置”按钮，可以直接打开这个文件夹。
