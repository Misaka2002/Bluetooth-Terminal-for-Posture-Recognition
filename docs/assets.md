# 3D 姿态素材

`assets/posture-atlas.png` 使用 Codex 内置 image_gen 生成，作为人物姿态示意。图片为带透明通道的 PNG，尺寸 1536 × 1024，按 4 列、2 行排列，共八个图格。

编译时，图集以 `PostureStatistics.PostureAtlas.png` 的资源名称嵌入 EXE。程序根据设备发送的分类名称选择对应图格，不需要联网加载图片。

左右方向按人物自身视角判断。分类顺序和图格编号的对应关系如下，图格从 0 开始按行编号：

| 分类名称 | 图格编号 |
| --- | ---: |
| `upright` | 0 |
| `forward_lean` | 1 |
| `backward_lean` | 2 |
| `left_lean` | 4 |
| `right_lean` | 3 |
| `forward_hunch` | 5 |
| `left_hunch` | 6 |
| `right_hunch` | 7 |

`docs/images/overview.png` 展示的是演示模式界面，画面中的时长来自模拟数据。
