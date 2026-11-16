# CATIA 对数螺旋线拟合与标准方程曲线生成工具（操作说明与数学原理）

本工具为独立运行的 Windows 图形界面程序（`LogSpiralFitter.exe`），用于在 CATIA V5 中交互选择一条空间/平面对数螺旋线，按曲线弧长自适应布置采样点，自动拟合并生成 **CATIA 原生 `GSMCurveEquations`（来自方程式的曲线）** 与 **基圆（Base Circle）**，全程无需手动选择 X/Y/Z 法则曲线。

---

## 一、运行环境与文件组成

### 独立运行程序包（`LogSpiralFitter_Program.zip`）
解压后请保持以下文件在**同一文件夹**下（直接双击 `LogSpiralFitter.exe` 启动）：
- `LogSpiralFitter.exe`：主程序（内置对数螺旋线图标，仅依赖 Windows 自带 `.NET Framework 4.x`）
- `LogSpiral_PowerCopy.CATPart`：原生方程曲线模板文件（**必须与 `LogSpiralFitter.exe` 同目录**，用于全自动免弹窗生成 `GSMCurveEquations`；缺失时程序将直接报错提示，不再弹出人工对话框）
- `README_对数螺旋线拟合.md`：本操作说明文档

---

## 二、操作步骤

1. **打开源零件**：在 CATIA V5 中打开并保存需要处理的 `.CATPart`（例如附带的测试样例 `Part9.CATPart`）。
2. **启动程序**：双击运行 `LogSpiralFitter.exe`。
3. **交互选线**：
   - 点击界面上的 **[从 CATIA 选择曲线…]** 按钮，窗口会自动隐藏并切换到 CATIA。
   - 在 CATIA 3D 几何区或左侧结构树中点选目标曲线（如 `Part9.CATPart > 曲线.2`）。
   - 选定后窗口自动恢复，显示曲线名称、实测总长度（mm），以及按当前采样间距自动换算出的采样点数。
4. **设置采样间距**：
   - 默认采样间距为 **`1.00 mm`**（可在 `0.05 ~ 50.00 mm` 间调整）。
   - 程序根据 `Round(曲线长度 / 采样间距) + 1` 自适应计算采样点数（安全范围 `20 ~ 5000` 点）。
5. **开始拟合与自动建模**：
   - 点击 **[开始拟合并生成标准方程曲线]**。
   - 进度条按 `0% ~ 100%` 分 6 个步骤实时推进，状态框保留各步骤执行记录。
   - 程序不会修改或覆盖源 `.CATPart`，而是自动复制生成 `原文件名_LogSpiralEquation.CATPart`（若已存在则自动附加时间戳后缀）。
   - 完成后进度条达 `100%`、状态变为绿色并播放提示音，可点击 **[打开结果文件夹]** 直接定位生成的 `.CATPart` 文件。

---

## 三、输出结果说明

生成的 `*_LogSpiralEquation.CATPart` 中包含：

1. **几何图形集 `LogSpiral_Equation`（默认显示）**：
   - **最终对数螺旋线**（CATIA 原生 `GSMCurveEquations` 特征，非样条近似）：
     名称自动标记为完整参数说明，例如：
     `对数螺旋线_r=84.987*exp(0.04440*θ)_a=84.987mm_b=0.044400_θ=0.0deg~294.9deg`
   - **基圆（Base Circle）**：
     名称为 `基圆_R={a}mm_D={2a}mm`（例如 `基圆_R=84.987mm_D=169.973mm`），位于拟合平面上、以 `Fitted_Center` 为圆心、半径通过公式绑定到 `LogSpiral_a`（与螺旋线内端起点半径 $r_0 = a$ 一致，内端落在基圆上；注：当 $b \neq 0$ 时螺旋线内端切向含径向分量 $ab$，与基圆相交而不共切）。
   - **参数化联动**：
     `Fitted_Center` 坐标、`Fitted_Plane` 方程、`Base_Circle_Radius/Diameter` 与基圆半径均通过 CATIA 公式（Formula）关联到根参数 `Center_X/Y/Z`、`LogSpiral_a` 等；三条 Law 绑定到目标零件的根参数，修改 `LogSpiral_a`、`Center_X/Y/Z` 等参数并更新零件后，方程曲线与基圆同步更新。程序在输出前会自动执行一次 `+10 mm` 参数联动自检与内端到基圆距离测量。
   - **辅助参考几何（保留在结构树中，默认隐藏 NoShow）**：
     - `Fitted_Center`：拟合螺旋中心点
     - `Reference_Axis_U`（$\theta = 0^\circ$ 轴，从圆心指向螺旋线内圈最里端起点）、`Reference_Axis_V`（$\theta = 90^\circ$ 正交轴）及端点 `Reference_U_End`、`Reference_V_End`
     - `Fitted_Plane`：PCA 拟合平面
2. **采样点过程几何集 `__LogSpiral_FitSamples`（保留在结构树中，默认隐藏 NoShow）**：
   - 包含按间距生成的曲线采样点 `FitSample_0000` 等，仅隐藏不删除，便于后续复核。
3. **三条标准方程法则（`Relations` 下）**：
   - `LogSpiral_X_StandardEquation`、`LogSpiral_Y_StandardEquation`、`LogSpiral_Z_StandardEquation`
4. **命名参数（`Parameters` 下）**：
   - `Center_X`、`Center_Y`、`Center_Z`（mm）
   - `LogSpiral_a`（基圆半径 $a$，mm）、`LogSpiral_b`（指数增长率 $b$）
   - `Base_Circle_Radius`、`Base_Circle_Diameter`（基圆半径与直径，mm）
   - `Theta_Start`、`Theta_End`（起止角，起始角恒对齐为 `0 rad`）
   - `Axis_U_X/Y/Z`、`Axis_V_X/Y/Z`、`Plane_Normal_X/Y/Z`
   - `Fit_RMS_Error`、`Fit_Max_Error`、`Fit_R2`
5. **隐藏的过程数据文件（同目录下，Windows 隐藏属性）**：
   - `*_report.json`：完整拟合参数与误差报告（含平面内法向残差、平面内径向残差、离平面 RMS/最大偏差、内端到基圆实测间隙）
   - `*_samples.csv`：全部采样点三维坐标、极角、`in_plane_normal_residual_mm`（平面内法向残差）与 `in_plane_radial_residual_mm`（平面内径向残差）明细

---

## 四、数学原理简介

1. **自适应弧长采样**：
   在源曲线上按归一化弧长比例 $u_i = i / (N - 1)$（$N = \text{clamp}(\text{Round}(L / \Delta s) + 1, 20, 5000)$）布置采样点，通过 `SPAWorkbench` 提取精确三维坐标 $\mathbf{P}_i$。
2. **平面拟合（PCA）**：
   对采样点云求质心与 $3 \times 3$ 协方差矩阵，用 Jacobi 特征值分解求最小特征值对应的单位法向量 $\mathbf{n}$，建立局部平面正交基。
3. **螺旋中心与参数稳健优化**：
   在平面内以多起点（质心、包围盒中心、三点圆心及网格偏移点）运行两阶段 Nelder-Mead 单纯形优化，最小化带 Huber 稳健权重的径向残差目标函数。对任意候选中心 $(c_u, c_v)$，计算连续解缠绕极角 $\theta_i$ 与对数半径 $\ln r_i$，由解析线性回归求得 $\ln r = \alpha + b\theta$。
4. **内端零角基准对齐（$\theta = 0^\circ$ 对齐基圆起点）**：
   求出最优圆心后，比较曲线首尾两端的拟合半径，取最小半径端角度 $\theta_0$（螺旋线最内端起点），将局部平面正交基 $(\mathbf{e}_U, \mathbf{e}_V)$ 绕法向旋转 $\theta_0$：
   $$\mathbf{e}_U' = \cos\theta_0\,\mathbf{e}_U + \sin\theta_0\,\mathbf{e}_V, \quad \mathbf{e}_V' = -\sin\theta_0\,\mathbf{e}_U + \cos\theta_0\,\mathbf{e}_V$$
   在新基准下，极角 $\theta' = \theta - \theta_0$ 从内端起点 $\theta'_{\text{start}} = 0^\circ$ 开始，标准方程为：
   $$r(\theta') = a \cdot e^{b\theta'}, \quad a = r(0) = r_{\min}$$
   三维曲线轨迹完全不变，而 $a$ 直接等于内圈基圆半径，使螺旋线内端落在基圆上（仅支持任意朝向的平面对数螺旋线；若离平面偏差、展开角或 $b$ 值异常，报告会给出退化警告）。

---

## 五、`Part9.CATPart` 标准测试结果参考

- **源曲线**：`Part9.CATPart > 曲线.2`（实测弧长 `492.079 mm`）
- **采样设置**：间距 `1.00 mm` $\rightarrow$ 自适应采样 `493` 点
- **拟合中心**：`(3383.676658, 586.500000, 781.145127) mm`
- **基圆半径 $a$（`LogSpiral_a`）**：`84.986659505 mm`（基圆直径 `169.973319010 mm`）
- **螺旋系数 $b$（`LogSpiral_b`）**：`0.044399720883`（扩展角 $\alpha = \arctan(b) = 2.542246955^\circ$）
- **起止角**：`0.000000000° ~ 294.927231127°`（展开角 `294.927231127°`，约 `0.819242309` 圈）
- **拟合精度**：`RMS = 0.415838 mm`，最大误差 `1.393977 mm`，$R^2 = 0.994994180$
