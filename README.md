# DOTween 序列动画系统

一个基于 DOTween 的数据驱动 Transform 动画序列播放与编辑系统，支持：
- 序列（顺序 / 并行）
- 动画（可包含多段 Transform 关键段）
- 逻辑桥接（逻辑名 → 序列或组合播放）
- JSON 持久化（剥离 UnityEvent）
- 自定义 Inspector + 独立编辑器窗口可视化管理
- 运行时播放控制（单序列 / 多序列 / 单动画 / 串行 / 并行）
- 起止状态自动重置、事件回调

> 当前为轻量级 Transform 动画管线，适合 UI / 过场 / 简单展示逻辑，不是完整 Timeline 替代。

---

## 目录
1. 功能特性
2. 快速上手
3. 组件与数据结构
4. 编辑器工作流
5. 运行时播放（Controller & Bridge）
6. 进度获取
7. 数据持久化 (Save / Load / JSON)
8. 常见问题 (FAQ)
9. 扩展指引
10. Roadmap（后续可选增强）
11. 变更记录（本次批次改动）
12. 命名与约定
13. 质量保障建议

---

## 1. 功能特性

| 模块 | 能力 |
|------|------|
| TransformData | 单关键段：位置 / 旋转 / 缩放 / 时长 / Ease / 激活控制 |
| AnimationData | 一段动画：起始状态 + 多个目标 TransformData 阶段 |
| AnimationSequence | 多个动画组成：顺序或并行，支持 delay |
| DOTweenAnimationController | 播放 / 暂停 / 停止 / 单动画 / 多序列 / 重置 / 保存加载 |
| DOTweenAnimationBridge | 逻辑名→单序列或序列组（串行/并行）调用 + 进度查询 |
| 编辑器窗口 | 序列、动画、关键段增删 / 排序 / 捕获 / 应用 / 复制 / 校验 / 扫描修复引用 |
| 数据服务 | JSON 导出 / 导入（UnityEvent 不序列化） / 路径恢复 |
| 安全措施 | 时长归零保护 / 重复引用检测与克隆 / 序列名唯一化 / 目标缺失跳过 |

---

## 2. 快速上手

1. 导入 DOTween（已安装并初始化）。
2. 将 `DOTweenAnimationController` 挂到一个空 GameObject（建议命名：`AnimationController`）。
3. 打开菜单：`Window / DOTween Animation Editor`。
4. 创建序列 → 添加动画 → 绑定目标对象 → 捕获起始 / 目标姿态。
5. 播放测试（运行态可直接在 EditorWindow 或 Inspector 中点“播放”）。
6. 可选添加 `DOTweenAnimationBridge`，配置逻辑名并用代码调用：
   ```csharp
   bridge.PlayAni("PanelOpen");
   ```
7. 导出 JSON 保存配置或版本化。

---

## 3. 组件与数据结构

### TransformData
字段：position / rotation / scale / duration / easeType / enableActiveControl / activeState  
说明：表示一个关键状态以及到达它所需的补间时长（相对于上一个阶段）。

### AnimationData
- startTransform：起始姿态（进入播放前会应用，顺序模式逐段执行）
- targetTransforms：按顺序播放的关键段集合
- useLocalSpace：决定位置/旋转使用 local 还是 world
- UnityEvent：onAnimationStart / Update / Complete（仅运行内存保存）

### AnimationSequence
- animations：多个 AnimationData
- isParallel：true=并行（每个 AnimationData 自己的所有段 Join），false=顺序
- delay：整体延迟
- GetTotalDuration：并行取 max，总和取 sum

### DOTweenAnimationController
职责：序列注册、播放、跨序列状态重置、单动画独立播放、数据导出导入。

### DOTweenAnimationBridge
职责：提供逻辑层 API：
- 配置 BridgeAniData（aniName → singleSequence 或 groupSequences）
- 支持串行 / 并行组播放
- 单序列模式使用真实 DOTween Sequence 计算精确进度

---

## 4. 编辑器工作流

打开窗口：`Window / DOTween Animation Editor`

基础操作：
- 序列列表：添加 / 删除 / 复制 / 排序
- 动画列表：添加 / 删除 / 排序 / 复制
- 关键段：
  - 捕获：读取当前对象 Transform
  - 应用：把段数据写回对象
  - 前插 / 后插：以当前段 Clone 新段
  - 复：复制段并插入其后
  - ▲▼：调整段顺序
  - 删：删除段
- “扫描并修复动画内部重复 Transform 引用”：
  - 解决旧版本插入逻辑可能造成的多条目共享引用问题
- 排序：可按持续时间排序 targetTransforms
- 批量捕获：把当前对象 Transform 写入所有目标段

注意：
- 修改后建议保存场景或导出 JSON
- UnityEvent 仅在项目资源序列化（场景 / Prefab）中存在，不随 JSON 导出

---

## 5. 运行时播放

### 基本播放
```csharp
var controller = FindObjectOfType<DOTweenAnimationController>();
controller.PlaySequence("PanelIntro");
```

### 播放单个动画（跳过同序列其它动画）
```csharp
controller.PlaySingleAnimation("PanelIntro", "ScaleIn");
```

### 播放所有序列（顺序串行）
```csharp
controller.PlayAllSequences();
```

### 并行播放多个指定序列
```csharp
controller.PlayMultipleSequences(new List<string>{ "PanelIntro", "PanelGlow" }, parallel:true);
```

### 停止 / 暂停 / 恢复
```csharp
controller.StopSequence("PanelIntro");
controller.PauseSequence("PanelIntro");
controller.ResumeSequence("PanelIntro");
controller.StopAllSequences();
```

### 使用 Bridge
Bridge 配置：
- aniName: "OpenPanel"
- singleSequence 或 groupSequences（>0 时优先组合）

调用：
```csharp
bridge.PlayAni("OpenPanel", 
    onStart: ()=>Debug.Log("Start"),
    onComplete: ()=>Debug.Log("Done"));
```

---

## 6. 进度获取

```csharp
float p = bridge.GetCurrentProgress();
```

说明：
- 单序列模式：真实 Sequence Elapsed/Duration
- 序列组模式（串行/并行）：当前为估算（基于总时长）；后续可扩展精确聚合

---

## 7. 数据持久化

### 导出
```csharp
string json = controller.ExportToJSON();
// 可写入磁盘 / 版本管理
```

### 导入
```csharp
controller.ImportFromJSON(jsonContent);
```

### 保存到默认文件（persistentDataPath）
```csharp
controller.SaveAnimationData();
controller.LoadAnimationData();
```

注意：
- UnityEvent 不参与 JSON
- 目标对象绑定依赖层级路径（重命名 / 场景结构改变可能失效）
- 失败后需手动重新指定 targetObject

---

## 8. 常见问题 FAQ

| 问题 | 说明 / 解决 |
|------|-------------|
| 修改 Bridge 配置后 PlayAni 找不到 | 确保已触发 OnValidate（编辑器修改自动）或重新运行；运行时动态改 configs 后可手动调用 EnsureMap（内部 PlayAni 已做一次兜底重建） |
| 动画不播放 / 跳过 | 检查 targetObject 是否为空；duration 是否全部为 0；IsValid |
| 并行序列对同一对象效果冲突 | 当前仅警告，不自动合并；请拆分或避免并行冲突 |
| 进度不精确 | 组合模式仍为估算，需后续升级（见 Roadmap） |
| JSON 导入后对象丢失 | 路径失效，手工重新绑定对象；再执行“扫描修复”确保数据有效 |

---

## 9. 扩展指引

方向与建议：
1. 属性扩展：在 TransformData 增加可选字段（如 Color/Alpha），在 CreateAnimationSequence 中条件 Join Tween。
2. Tween 通道裁剪：若位置/旋转/缩放与上一段相同则跳过，减少无效 Tween。
3. 0 时长合并：播放前把连续 0 duration 片段合并为最后一个（减少 AppendCallback）。
4. Track 概念：将 Position/Rotation/Scale 分离，解决冲突并允许叠加。
5. 相对动画：新增 bool relative → target.DOLocalMove(relativeTarget, d).SetRelative()。
6. 多序列组合真实进度：为 group 构造虚拟聚合 Sequence（空 Tweens + Append/Join），基于 Elapsed 获取。
7. GUID 绑定：给关键对象挂载组件保存唯一 ID，提高跨场景/重命名恢复成功率。
8. 编辑器性能：大量数据时列表虚拟化、延迟 SetDirty 合并。

---

## 10. Roadmap（候选增强）

| 优先 | 项目 | 说明 |
|------|------|------|
| 高 | 组合进度精确化 | 串行/并行聚合真实 DOTween Timeline |
| 高 | 0 时长节点合并 | 降低回调和 Sequence 深度 |
| 中 | Tween 通道裁剪 | 减少无意义 Move/Rotate/Scale |
| 中 | GUID + 多策略恢复 | 提升跨层级变动的还原率 |
| 中 | 相对模式 | 提高复用与动态性 |
| 低 | Track 系统 | 冲突自动管理 |
| 低 | 曲线编辑 | 自定义 AnimationCurve |
| 低 | 事件关键帧 | TransformData 级事件 |

---

## 11. 变更记录（本次“第一批”修复摘要）

| 类别 | 内容 |
|------|------|
| 数据安全 | AnimationData.Sanitize 中新增 EnsureDistinctTransformDataReferences |
| 编辑器 | 前插/后插/复制关键段改为 Clone；新增“扫描并修复重复引用”按钮 |
| Bridge | 动态映射延迟重建 EnsureMap；单序列进度使用真实 DOTween Sequence |
| 控制器 | SanitizeAll：序列名唯一化；列表为空安全初始化 |
| 稳定性 | 防止 targetTransforms 为空；空引用自动补一个默认节点 |
| 其它 | 保留原行为基础上最小侵入式修改，未做性能大改（为后续批次留接口） |

---

## 12. 命名与约定

| 元素 | 规则 |
|------|------|
| 序列名 | 必须唯一（系统自动冲突重命名: name_1 / name_2） |
| 动画名 | 建议在同一序列中唯一，便于 PlaySingleAnimation |
| 逻辑 aniName | Bridge 内唯一 |
| JSON 文件 | 默认名称 `AnimationData.json` 或自定义 |
| 复制 | 自动追加 `_Copy` / `_Copy_n` |

---

## 13. 质量保障建议

1. 启动自检（开发期）：枚举所有序列 → 检查 targetObject 为空 / 重复引用 / 0 总时长。
2. 单元测试（可使用 EditMode Tests）：
   - Clone 深拷贝 vs 引用测试
   - 顺序/并行时长计算验证
   - JSON Export→Import 往返一致性（除 UnityEvent）
3. 性能基准：
   - 100 序列 × 每序列 10 动画 × 每动画 8 段，检测 GC Alloc / 帧耗时
4. 编辑器安全：
   - 大批量修改后手动触发一次“扫描重复引用”确保历史数据清洁
5. 持续集成：
   - 检查脚本编译 + 测试通过 + JSON 示例文件合法性

---

## 附：简单示例代码

```csharp
public class UIAnimator : MonoBehaviour
{
    public DOTweenAnimationBridge bridge;

    public void Open()
    {
        bridge.PlayAni("OpenPanel",
            onStart: ()=>Debug.Log("Opening..."),
            onComplete: ()=>Debug.Log("Opened"));
    }

    public void Close()
    {
        bridge.PlayAni("ClosePanel");
    }

    void Update()
    {
        if (bridge != null && bridge.PlayState == BridgePlayState.Playing)
        {
            float p = bridge.GetCurrentProgress();
            // 可驱动进度条或调试输出
        }
    }
}
```
