# Codex Dock 图标重设计

本目录保留三组早期候选的完整矢量与 Windows 图标素材，以及后来选定的组合版定义。

后续用户选定将 01 的图形与 03 的配色结合，最终定义见 `selected.json`。当前应用素材已经由 `scripts/apply-brand.ps1` 生成到 `assets/brand/`，候选目录继续保留早期方案，不代表当前选定外观。

| 方向 | 形状与配色 | 用途 |
| --- | --- | --- |
| 01 接入（推荐） | 开口 D 与进入端口的箭头；石墨黑、暖白、浅芽绿 | 与 Dock 的连接和统一入口含义最接近，小尺寸轮廓直接 |
| 02 折叠 | 折面构成 D；深墨色与浅紫 | 更明显的几何字母符号 |
| 03 层栈 | 两层结构；浅底、低饱和绿 | 更轻的浅色风格 |

`concepts.json` 是三版形状与颜色的源定义。每版目录 `rendered/<方向>/` 包含：

- `app.svg` 和 `app-16.png` 至 `app-1024.png`：带底色的应用图标。
- `symbol.svg`：透明背景的双色图形。
- `mono-dark.svg`、`mono-light.svg`：透明背景的深浅单色图形；同样提供 16–1024 像素 PNG。
- `app.ico`：16、20、24、32、40、48、64、128、256 像素 Windows 图标。
- `tray-on-light.ico`、`tray-on-dark.ico`：16、20、24、32、40、48 像素单色托盘图标。

图形只使用两块基本形状，没有渐变、阴影或外部图像。小尺寸 PNG 是从同一矢量直接渲染，保留透明边缘；托盘采用透明单色图形。

在仓库根目录重新生成：

```powershell
pwsh -NoProfile -File scripts/render-brand-refresh.ps1
python scripts/compose-brand-refresh.py
```

渲染脚本需要 Windows 与 PowerShell 7；排版脚本需要 Python、Pillow，以及 Windows 自带的 Segoe UI、微软雅黑字体。排版脚本检查所有 PNG 尺寸、透明通道、SVG XML 和 ICO 的每个分辨率，生成比较图与验证记录。

方案图位于 `artifacts/brand-refresh-20261117/logo-options.png`，当前图标对比位于同目录 `logo-before-after.png`。
