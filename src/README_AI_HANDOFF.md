# LogSpiralFitter — 原理说明与后续开发交接文档（AI / 开发者 Handoff）

本文档面向后续维护者与 AI 协作代理，完整记录项目的数学原理、技术架构、CATIA V5 COM Automation 关键兼容性陷阱及解决方案、构建与打包规范。

---

## 一、项目目标与核心约束

在 CATIA V5 中由用户交互选择一条带制造/测量误差的对数螺旋线，自动拟合其所在平面、螺旋中心、基圆及标准方程参数，并在新建的 `*_LogSpiralEquation.CATPart` 中全自动生成 **CATIA 原生 `GSMCurveEquations`（来自方程式的曲线）** 与 **基圆（Base Circle）**。

### 硬性工程约束
1. **原生方程曲线**：最终螺旋线必须是 CATIA 原生 `GSMCurveEquations` 特征（由 X/Y/Z 三条 Knowledgeware Law 驱动），严禁使用离散点 + Spline 样条替代。
2. **全程零弹窗**：不得在 CATIA 中弹出“来自方程式的曲线定义”要求人工点选 X/Y/Z 法则，也不得弹出文件只读权限确认框。
3. **外部独立程序**：采用 VB.NET（`.NET Framework 4.x`）+ WinForms 编译为独立 `LogSpiralFitter.exe`（带自定义对数螺旋线图标 `LogSpiral.ico`），通过 COM Late Binding 连接运行中的 CATIA V5，无需安装第三方依赖库或 CAA 运行时。
4. **源文件保护与过程对象隐藏**：
   - 严禁覆盖源 `.CATPart`，必须复制为 `*_LogSpiralEquation.CATPart` 后操作。
   - 采样点集 `__LogSpiral_FitSamples` 与参考构造几何（`Fitted_Center`、`Reference_Axis_U/V`、`Fitted_Plane`）保留在结构树中但设为隐藏（`NoShow`），默认仅显示最终对数螺旋线与基圆。
   - 磁盘过程报告 `*_report.json` 与 `*_samples.csv` 保留并设为 Windows `Hidden` 属性。

---

## 二、数学与几何算法原理（`SpiralMath`）

1. **按长度自适应采样（`CatiaRunner.ComputeSampleCount` & `SampleCurve`）**：
   - 先通过 `SPAWorkbench.GetMeasurable(curveRef).Length` 获取源曲线总长 $L$（mm）。
   - 根据用户设定的点间距 $\Delta s$（默认 `1.00 mm`），计算采样点数：
     $$N = \operatorname{clamp}\bigl(\operatorname{Round}(L / \Delta s) + 1,\; 20,\; 5000\bigr)$$
   - 在临时图形集 `__LogSpiral_FitSamples` 中按归一化比例 $u_i = i / (N - 1)$ 调用 `AddNewPointOnCurveFromPercent` 创建点并读取三维坐标 $\mathbf{P}_i \in \mathbb{R}^3$。

2. **主成分分析求拟合平面（PCA，`JacobiSmallest`）**：
   - 计算点云质心 $\bar{\mathbf{P}} = \frac{1}{N}\sum \mathbf{P}_i$ 及 $3 \times 3$ 协方差矩阵 $C = \sum (\mathbf{P}_i - \bar{\mathbf{P}})(\mathbf{P}_i - \bar{\mathbf{P}})^T$。
   - 用 $3 \times 3$ 实对称矩阵 Jacobi 旋转法求最小特征值对应的单位特征向量，即为拟合平面法向 $\mathbf{n}$；将点云投影到平面正交基 $(\mathbf{e}_{U0}, \mathbf{e}_{V0})$ 得二维点列 $(x_i, y_i)$。

3. **二维中心稳健非线性优化（`EvaluateCenter` + `NelderMead`）**：
   - 给定平面候选中心 $(c_x, c_y)$，计算极径 $r_i = \hypot(x_i - c_x, y_i - c_y)$ 与解缠绕连续极角 $\theta_i = \operatorname{Unwrap}(\operatorname{atan2}(y_i - c_y, x_i - c_x))$。
   - 对 $\ln r_i = \alpha + b\theta_i$ 做闭式最小二乘线性回归，得到解析最优 $(\alpha, b)$，再计算径向残差 $e_i = \hat{r}(\theta_i) - r_i$。
   - 目标函数采用伪 Huber 稳健损失 + 轻微中心偏移正则项，从质心、包围盒中心、三点代数圆心（`CircleCenter`）及 $3 \times 3$ 网格共 12 个初值启动两阶段 Nelder-Mead 单纯形搜索，精化得到多起点搜索找到的最优候选中心 $(c_x^*, c_y^*)$。

4. **内端零度基准轴对齐与基圆定义（Scheme 1）**：
   - 初始正交基 $(\mathbf{e}_{U0}, \mathbf{e}_{V0})$ 仅用于平面投影与优化，其零角方向任意。
   - 求得最优中心后，比较首尾两端拟合极径 $r_{\text{first}} = e^{\alpha + b\theta_0}$ 与 $r_{\text{last}} = e^{\alpha + b\theta_{N-1}}$，取较小一端的极角作为内端起点角 $\theta_{\text{inner}}$。
   - 将平面基向量绕法向旋转 $\theta_{\text{inner}}$：
     $$\mathbf{e}_U = \cos\theta_{\text{inner}}\,\mathbf{e}_{U0} + \sin\theta_{\text{inner}}\,\mathbf{e}_{V0}, \quad \mathbf{e}_V = -\sin\theta_{\text{inner}}\,\mathbf{e}_{U0} + \cos\theta_{\text{inner}}\,\mathbf{e}_{V0}$$
   - 令新极角 $\theta' = \theta - \theta_{\text{inner}}$，则内端起点处 $\theta'_{\text{start}} = 0$，外端为 $\theta'_{\text{end}} = \theta_{\text{outer}} - \theta_{\text{inner}}$，重参数化后的对数截距为：
     $$\alpha' = \alpha + b\theta_{\text{inner}}, \quad a = e^{\alpha'} = r_{\min}$$
   - **几何意义**：三维螺旋线空间轨迹完全不变（对任意参数 $t \in [0, 1]$，空间点 $\mathbf{C} + r(\theta'(t))(\cos\theta'(t)\,\mathbf{e}_U + \sin\theta'(t)\,\mathbf{e}_V)$ 恒等），而参数 $a = r(0)$ 直接等于螺旋线最内端起点半径（基圆半径），使螺旋线内端落在基圆上（当 $b \neq 0$ 时内端切向含径向分量 $ab$，与基圆相交而不共切）。
   - **参数化联动与自检**：工作文件直接以 `LogSpiral_PowerCopy.CATPart` 为底板复制创建，复用其原生 `LogSpiral_Equation` 下的 `LogSpiral_Standard_Equation`（`GSMCurveEquations`）与三条 `LogSpiral_X/Y/Z_StandardEquation` 法则及其根参数；`Fitted_Center`、`Fitted_Plane`、`baseCircle.Radius` 及 `Base_Circle_Radius/Diameter` 均通过 `CreateOrUpdateFormula` 绑定到根参数；输出前通过 `VerifyEquationAndBaseCircleBinding` 临时把 `LogSpiral_a` 增加 `10 mm`，同时测量曲线拓扑两端点（`0.0` 与 `1.0`）取较近端验证曲线内端与基圆同步移动，并记录内端到基圆实测间隙。
   - **安全与原子性**：选线与运行前均校验 `doc.Saved`，并在源件临时副本中按 `BodyName` + `CurveName` + 实测弧长校验曲线一致性后导入（`PasteSpecial("CATPrtResultWithOutLink")`）；缺失 `LogSpiral_PowerCopy.CATPart` 直接报错；工作在临时 `.CATPart` 中完成，取消或失败时自动关闭并清理全部临时产物与报告，仅在全部成功后再重命名为正式 `*_LogSpiralEquation.CATPart` 并恢复原 `DisplayFileAlerts` 设置。

---

## 三、CATIA V5 COM 关键技术细节与排错记录（必读）

### 1. 为什么不用 CAA 工厂接口直接创建 `GSMCurveEquations`？
已核查本机 CATIA V5R26（`C:\Program Files\Dassault Systemes\B26`）全部公开与受保护头文件（`CATIGSMFactory.h`、`CATIGSMUseFactory.h`、`CATIGSMUIFactory.h`）：达索仅公开了平面方程 `CATIGSMPlaneEquation` 与阿基米德螺线 `CATIGSMSpiral`，**从未公开 `GSMCurveEquations` 的 C++ 或 Automation 创建工厂**。因此采用包含预建 `GSMCurveEquations` 特征的模板文件 `LogSpiral_PowerCopy.CATPart` 进行自动复制与法则重绑，是无需人工点选且兼容性最好的方案。

### 2. 免弹窗模板工作流：为什么必须以模板为底板导入源曲线（而非反向 Copy/Paste `GSMCurveEquations`）
- **根因 1（会话只读弹窗）**：若调用 `InstanceFactory.BeginInstanceFactory("LogSpiral_PowerCopy", templatePath)`，CATIA 内部会将 `templatePath` 以**会话只读**状态加载并驻留内存，后续打开同路径时会弹出权限确认框。
- **根因 2（跨文档复制粘贴导致参数断链）**：二进制检查 `Part9_LogSpiralEquation.CATPart` 与 `LogSpiral_PowerCopy.CATPart` 发现：通过 `Selection.Copy()`/`Paste()` 跨文档复制 `GSMCurveEquations` 与 3 条 Law 时，CATIA 会隐式把模板中的 13 个参数复制成带本地化默认名的孤立参数（`长度.2`、`长度.3`、`实数.1` 等），导致粘贴后的 `GSMCurveEquations` 仍绑定在冻结的模板值上；而即使修改 Law 文本，如果正文中先出现 `实数.1`（`LogSpiral_b`）而非 `FormalReal.1`，也会把形式参数识别错，造成修改 `LogSpiral_a` 后方程曲线完全不动（增量 `0.0000 mm`）。
- **最终解法（Template-First + `ImportSourceCurveIntoTemplatePart`）**：
  1. 不再跨文档 Copy/Paste `GSMCurveEquations` 和 3 条 Law，也不调用 `BeginInstanceFactory`；
  2. 直接把 `LogSpiral_PowerCopy.CATPart` 复制到 `tempWorkPath`（去除只读属性）并打开作为目标工作文档——该模板内部原生创建的 `LogSpiral_Standard_Equation`、3 条 `LogSpiral_X/Y/Z_StandardEquation` 与 13 个根参数（`#1_..#8_`）之间的 C++ Spec 链接完好无损；
  3. 清理模板中原有的多余几何体与 `LogSpiral_PowerCopy` 定义后，将源 `.CATPart` 复制到临时副本并保存/恢复 `DisplayFileAlerts`，校验并选中源曲线通过 `PasteSpecial("CATPrtResultWithOutLink")` 导入目标工作文档的源几何集进行采样；
  4. `BuildEquationDefinition` 直接复用并更新 `LogSpiral_Equation` 下的参考几何与根参数，并通过 `CreateOrUpdateFormula` 绑定 `Fitted_Center`、`Fitted_Plane` 与基圆半径，最后通过 `VerifyEquationAndBaseCircleBinding` 实测 `+10 mm` 联动。

### 3. VB.NET 后期绑定 `0x80020003 (DISP_E_MEMBERNOTFOUND)` 两大陷阱
- **陷阱 A**：不要在 VB.NET Late Binding 中调用 `Parameters.GetNameToUseInRelation`，部分 CATIA V5 版本返回 `DISP_E_MEMBERNOTFOUND`。在 Knowledgeware Law 公式中直接写根参数名（如 `Center_X`、`LogSpiral_a`）即可。
- **陷阱 B**：在 `MecModTypeLib.tlb` 中，`Part::InWorkObject`（`memid=0x60050002`）被声明为 `INVOKE_PROPERTYPUT`（`DISPATCH_PROPERTYPUT = 4`），而非 `INVOKE_PROPERTYPUTREF`（`8`）。在 VB.NET 中直接写 `part.InWorkObject = resultBody` 时，因右值为 `__ComObject`，`NewLateBinding.LateSet` 只发送 `DISPATCH_PROPERTYPUTREF`，触发 `0x80020003`。必须通过 `SetComPropertyPut` 使用 `InvokeMember("InWorkObject", BindingFlags.PutDispProperty, ...)` 显式发送 `DISPATCH_PROPERTYPUT`。

### 4. SPA `Measurable.Length` 对原生方程曲线返回 `E_FAIL`
部分 CATIA V5 版本的 `SPAWorkbench.GetMeasurable(reference).Length` 不支持 `GSMCurveEquations` 并抛出 `E_FAIL`。`MeasureCurveLength` 已捕获该异常并返回 `Double.NaN`，报告中同时给出精确解析积分弧长：
$$L_{\text{解析}} = a\sqrt{1 + b^2}\,\left|\frac{e^{b\theta_{\text{end}}} - e^{b\theta_{\text{start}}}}{b}\right|$$

---

## 四、源码文件清单与编译打包说明

### 源码包（`LogSpiralFitter_Source.zip`）文件结构
- `LogSpiralFitter.vb`：完整单文件源码（数学库 `VectorMath`/`SpiralMath`、CATIA 自动化引擎 `CatiaRunner`、WinForms 界面 `MainForm`）
- `LogSpiral.ico`：多尺寸（`256/64/48/32/16`）RGBA PNG 压缩格式的 Windows 图标文件（对数螺旋线 + 基圆 + 坐标十字）
- `LogSpiral_PowerCopy.CATPart`：包含原生 `GSMCurveEquations` 与三条坐标法则的模板零件
- `Part9.CATPart`：标准测试样例零件（含待拟合曲线 `曲线.2`）
- `build_plugin.cmd`：一键编译脚本（调用系统自带 `.NET Framework 4.0` 64 位 `vbc.exe`，无需 Visual Studio）
- `Run_LogSpiral_Fitter.CATScript`：CATIA 宏启动脚本
- `README_对数螺旋线拟合.md`：用户操作说明与数学原理文档
- `README_AI_HANDOFF.md`：本开发交接文档

### 编译命令
直接双击或在命令行运行 `build_plugin.cmd`：
```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\vbc.exe /nologo /target:winexe /win32icon:"LogSpiral.ico" /out:"LogSpiralFitter.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "LogSpiralFitter.vb"
```
编译产物 `LogSpiralFitter.exe` 必须与 `LogSpiral_PowerCopy.CATPart` 放在同一目录下运行。
