Option Strict Off

Imports System
Imports System.IO
Imports System.Text
Imports System.Globalization
Imports System.Runtime.InteropServices
Imports System.Collections.Generic
Imports System.Windows.Forms

Public Structure Point3
    Public X As Double
    Public Y As Double
    Public Z As Double
    Public Sub New(ByVal px As Double, ByVal py As Double, ByVal pz As Double)
        X = px : Y = py : Z = pz
    End Sub
End Structure

Public Structure Point2
    Public X As Double
    Public Y As Double
    Public Sub New(ByVal px As Double, ByVal py As Double)
        X = px : Y = py
    End Sub
End Structure

Public Class CenterEvaluation
    Public Objective As Double
    Public Alpha As Double
    Public B As Double
    Public Theta() As Double
    Public Residual() As Double
End Class

Public Class FitResult
    Public Center As Point3
    Public PlaneOrigin As Point3
    Public AxisU As Point3
    Public AxisV As Point3
    Public Normal As Point3
    Public Center2 As Point2
    Public Alpha As Double
    Public B As Double
    Public ThetaStart As Double
    Public ThetaEnd As Double
    Public RmsError As Double
    Public MaxError As Double
    Public PlanarityMax As Double
    Public R2 As Double
    Public SampleSpacingMm As Double
    Public SampleCount As Integer
    Public SourcePoints() As Point3
    Public Theta() As Double
    Public Residual() As Double
End Class

Public Module VectorMath
    Public Function Add(ByVal a As Point3, ByVal b As Point3) As Point3
        Return New Point3(a.X + b.X, a.Y + b.Y, a.Z + b.Z)
    End Function
    Public Function Subtract(ByVal a As Point3, ByVal b As Point3) As Point3
        Return New Point3(a.X - b.X, a.Y - b.Y, a.Z - b.Z)
    End Function
    Public Function Scale(ByVal a As Point3, ByVal s As Double) As Point3
        Return New Point3(a.X * s, a.Y * s, a.Z * s)
    End Function
    Public Function Dot(ByVal a As Point3, ByVal b As Point3) As Double
        Return a.X * b.X + a.Y * b.Y + a.Z * b.Z
    End Function
    Public Function Cross(ByVal a As Point3, ByVal b As Point3) As Point3
        Return New Point3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X)
    End Function
    Public Function Norm(ByVal a As Point3) As Double
        Return Math.Sqrt(Dot(a, a))
    End Function
    Public Function Normalize(ByVal a As Point3) As Point3
        Dim n As Double = Norm(a)
        If n < 1.0E-14 Then Throw New InvalidOperationException("Cannot normalize a zero vector.")
        Return Scale(a, 1.0 / n)
    End Function
End Module

Public Class SpiralMath
    Private Shared Function Unwrap(ByVal raw() As Double) As Double()
        Dim result(raw.Length - 1) As Double
        result(0) = raw(0)
        For i As Integer = 1 To raw.Length - 1
            Dim d As Double = raw(i) - raw(i - 1)
            While d > Math.PI
                d -= 2.0 * Math.PI
            End While
            While d < -Math.PI
                d += 2.0 * Math.PI
            End While
            result(i) = result(i - 1) + d
        Next
        Return result
    End Function

    Public Shared Function EvaluateCenter(ByVal points() As Point2, ByVal cx As Double, ByVal cy As Double, ByVal robustScale As Double) As CenterEvaluation
        Dim n As Integer = points.Length
        Dim raw(n - 1) As Double
        Dim radii(n - 1) As Double
        Dim logs(n - 1) As Double
        For i As Integer = 0 To n - 1
            Dim dx As Double = points(i).X - cx
            Dim dy As Double = points(i).Y - cy
            Dim r As Double = Math.Sqrt(dx * dx + dy * dy)
            If r < 1.0E-9 Then
                Return New CenterEvaluation With {.Objective = Double.PositiveInfinity}
            End If
            raw(i) = Math.Atan2(dy, dx)
            radii(i) = r
            logs(i) = Math.Log(r)
        Next
        Dim theta() As Double = Unwrap(raw)
        Dim meanT As Double = 0.0, meanL As Double = 0.0
        For i As Integer = 0 To n - 1
            meanT += theta(i) : meanL += logs(i)
        Next
        meanT /= n : meanL /= n
        Dim sTT As Double = 0.0, sTL As Double = 0.0
        For i As Integer = 0 To n - 1
            Dim dt As Double = theta(i) - meanT
            sTT += dt * dt
            sTL += dt * (logs(i) - meanL)
        Next
        If sTT < 1.0E-10 Then
            Return New CenterEvaluation With {.Objective = Double.PositiveInfinity}
        End If
        Dim b As Double = sTL / sTT
        Dim alpha As Double = meanL - b * meanT
        Dim residual(n - 1) As Double
        Dim objective As Double = 0.0
        Dim delta As Double = Math.Max(robustScale, 1.0E-5)
        Dim denom As Double = Math.Sqrt(1.0 + b * b)
        For i As Integer = 0 To n - 1
            Dim e As Double = (logs(i) - alpha - b * theta(i)) * radii(i) / denom
            residual(i) = e
            Dim ae As Double = Math.Abs(e)
            If ae <= delta Then
                objective += 0.5 * e * e
            Else
                objective += delta * (ae - 0.5 * delta)
            End If
        Next
        objective /= n
        Return New CenterEvaluation With {.Objective = objective, .Alpha = alpha, .B = b, .Theta = theta, .Residual = residual}
    End Function

    Private Shared Function PenalizedObjective(ByVal points() As Point2, ByVal x As Double, ByVal y As Double, ByVal c0 As Point2, ByVal limit As Double, ByVal robustScale As Double) As Double
        Dim dx As Double = x - c0.X, dy As Double = y - c0.Y
        Dim d As Double = Math.Sqrt(dx * dx + dy * dy)
        Dim value As Double = EvaluateCenter(points, x, y, robustScale).Objective
        If d > limit Then value += (d - limit) * (d - limit) * 1000.0
        Return value
    End Function

    Private Shared Function NelderMead(ByVal points() As Point2, ByVal start As Point2, ByVal stepSize As Double, ByVal centroid As Point2, ByVal limit As Double, ByVal robustScale As Double) As Point2
        Dim p(2) As Point2
        p(0) = start
        p(1) = New Point2(start.X + stepSize, start.Y)
        p(2) = New Point2(start.X, start.Y + stepSize)
        Dim f(2) As Double
        For i As Integer = 0 To 2
            f(i) = PenalizedObjective(points, p(i).X, p(i).Y, centroid, limit, robustScale)
        Next
        For iter As Integer = 0 To 499
            For i As Integer = 0 To 1
                For j As Integer = i + 1 To 2
                    If f(j) < f(i) Then
                        Dim tf As Double = f(i) : f(i) = f(j) : f(j) = tf
                        Dim tp As Point2 = p(i) : p(i) = p(j) : p(j) = tp
                    End If
                Next
            Next
            Dim spread As Double = Math.Max(Math.Abs(f(2) - f(0)), Math.Max(Math.Abs(p(2).X - p(0).X), Math.Abs(p(2).Y - p(0).Y)))
            If spread < 1.0E-10 Then Exit For
            Dim mx As Double = 0.5 * (p(0).X + p(1).X)
            Dim my As Double = 0.5 * (p(0).Y + p(1).Y)
            Dim pr As New Point2(mx + (mx - p(2).X), my + (my - p(2).Y))
            Dim fr As Double = PenalizedObjective(points, pr.X, pr.Y, centroid, limit, robustScale)
            If fr < f(0) Then
                Dim pe As New Point2(mx + 2.0 * (pr.X - mx), my + 2.0 * (pr.Y - my))
                Dim fe As Double = PenalizedObjective(points, pe.X, pe.Y, centroid, limit, robustScale)
                If fe < fr Then p(2) = pe : f(2) = fe Else p(2) = pr : f(2) = fr
            ElseIf fr < f(1) Then
                p(2) = pr : f(2) = fr
            Else
                Dim pc As New Point2(mx + 0.5 * (p(2).X - mx), my + 0.5 * (p(2).Y - my))
                Dim fc As Double = PenalizedObjective(points, pc.X, pc.Y, centroid, limit, robustScale)
                If fc < f(2) Then
                    p(2) = pc : f(2) = fc
                Else
                    For i As Integer = 1 To 2
                        p(i) = New Point2(p(0).X + 0.5 * (p(i).X - p(0).X), p(0).Y + 0.5 * (p(i).Y - p(0).Y))
                        f(i) = PenalizedObjective(points, p(i).X, p(i).Y, centroid, limit, robustScale)
                    Next
                End If
            End If
        Next
        Return p(0)
    End Function

    Private Shared Function Solve3(ByVal a(,) As Double, ByVal b() As Double) As Double()
        Dim m(2, 3) As Double
        For i As Integer = 0 To 2
            For j As Integer = 0 To 2 : m(i, j) = a(i, j) : Next
            m(i, 3) = b(i)
        Next
        For k As Integer = 0 To 2
            Dim pivot As Integer = k
            For i As Integer = k + 1 To 2
                If Math.Abs(m(i, k)) > Math.Abs(m(pivot, k)) Then pivot = i
            Next
            If Math.Abs(m(pivot, k)) < 1.0E-12 Then Return Nothing
            If pivot <> k Then
                For j As Integer = k To 3
                    Dim t As Double = m(k, j) : m(k, j) = m(pivot, j) : m(pivot, j) = t
                Next
            End If
            Dim d As Double = m(k, k)
            For j As Integer = k To 3 : m(k, j) /= d : Next
            For i As Integer = 0 To 2
                If i <> k Then
                    Dim q As Double = m(i, k)
                    For j As Integer = k To 3 : m(i, j) -= q * m(k, j) : Next
                End If
            Next
        Next
        Return New Double() {m(0, 3), m(1, 3), m(2, 3)}
    End Function

    Private Shared Function CircleCenter(ByVal points() As Point2) As Point2?
        Dim ata(2, 2) As Double, atb(2) As Double
        For Each p As Point2 In points
            Dim row() As Double = {p.X, p.Y, 1.0}
            Dim rhs As Double = -(p.X * p.X + p.Y * p.Y)
            For i As Integer = 0 To 2
                atb(i) += row(i) * rhs
                For j As Integer = 0 To 2 : ata(i, j) += row(i) * row(j) : Next
            Next
        Next
        Dim sol() As Double = Solve3(ata, atb)
        If sol Is Nothing Then Return Nothing
        Return New Point2(-0.5 * sol(0), -0.5 * sol(1))
    End Function

    Private Shared Sub JacobiSmallest(ByVal input(,) As Double, ByRef eigenValue As Double, ByRef eigenVector As Point3)
        Dim a(2, 2) As Double, v(2, 2) As Double
        For i As Integer = 0 To 2
            For j As Integer = 0 To 2 : a(i, j) = input(i, j) : Next
            v(i, i) = 1.0
        Next
        For iter As Integer = 0 To 60
            Dim p As Integer = 0, q As Integer = 1
            Dim maxOff As Double = Math.Abs(a(0, 1))
            If Math.Abs(a(0, 2)) > maxOff Then p = 0 : q = 2 : maxOff = Math.Abs(a(0, 2))
            If Math.Abs(a(1, 2)) > maxOff Then p = 1 : q = 2 : maxOff = Math.Abs(a(1, 2))
            If maxOff < 1.0E-14 Then Exit For
            Dim phi As Double = 0.5 * Math.Atan2(2.0 * a(p, q), a(q, q) - a(p, p))
            Dim c As Double = Math.Cos(phi), s As Double = Math.Sin(phi)
            For k As Integer = 0 To 2
                Dim apk As Double = a(p, k), aqk As Double = a(q, k)
                a(p, k) = c * apk - s * aqk : a(q, k) = s * apk + c * aqk
            Next
            For k As Integer = 0 To 2
                Dim akp As Double = a(k, p), akq As Double = a(k, q)
                a(k, p) = c * akp - s * akq : a(k, q) = s * akp + c * akq
                Dim vkp As Double = v(k, p), vkq As Double = v(k, q)
                v(k, p) = c * vkp - s * vkq : v(k, q) = s * vkp + c * vkq
            Next
        Next
        Dim idx As Integer = 0
        If a(1, 1) < a(idx, idx) Then idx = 1
        If a(2, 2) < a(idx, idx) Then idx = 2
        eigenValue = a(idx, idx)
        eigenVector = VectorMath.Normalize(New Point3(v(0, idx), v(1, idx), v(2, idx)))
    End Sub

    Public Shared Function Fit(ByVal source() As Point3) As FitResult
        If source.Length < 20 Then Throw New ArgumentException("At least 20 samples are required.")
        Dim origin As New Point3(0, 0, 0)
        For Each p As Point3 In source : origin = VectorMath.Add(origin, p) : Next
        origin = VectorMath.Scale(origin, 1.0 / source.Length)
        Dim cov(2, 2) As Double
        For Each p As Point3 In source
            Dim d As Point3 = VectorMath.Subtract(p, origin)
            Dim z() As Double = {d.X, d.Y, d.Z}
            For i As Integer = 0 To 2
                For j As Integer = 0 To 2 : cov(i, j) += z(i) * z(j) : Next
            Next
        Next
        Dim smallest As Double, normal As Point3
        JacobiSmallest(cov, smallest, normal)
        Dim chord As Point3 = VectorMath.Subtract(source(source.Length - 1), source(0))
        chord = VectorMath.Subtract(chord, VectorMath.Scale(normal, VectorMath.Dot(chord, normal)))
        If VectorMath.Norm(chord) < 1.0E-8 Then
            chord = VectorMath.Subtract(source(source.Length \ 3), source(0))
            chord = VectorMath.Subtract(chord, VectorMath.Scale(normal, VectorMath.Dot(chord, normal)))
        End If
        Dim axisU As Point3 = VectorMath.Normalize(chord)
        Dim axisV As Point3 = VectorMath.Normalize(VectorMath.Cross(normal, axisU))
        Dim planar(source.Length - 1) As Point2
        Dim maxPlane As Double = 0.0
        For i As Integer = 0 To source.Length - 1
            Dim d As Point3 = VectorMath.Subtract(source(i), origin)
            planar(i) = New Point2(VectorMath.Dot(d, axisU), VectorMath.Dot(d, axisV))
            maxPlane = Math.Max(maxPlane, Math.Abs(VectorMath.Dot(d, normal)))
        Next
        Dim minX As Double = planar(0).X, maxX As Double = minX, minY As Double = planar(0).Y, maxY As Double = minY
        Dim meanX As Double = 0.0, meanY As Double = 0.0
        For Each p As Point2 In planar
            minX = Math.Min(minX, p.X) : maxX = Math.Max(maxX, p.X)
            minY = Math.Min(minY, p.Y) : maxY = Math.Max(maxY, p.Y)
            meanX += p.X : meanY += p.Y
        Next
        meanX /= planar.Length : meanY /= planar.Length
        Dim centroid As New Point2(meanX, meanY)
        Dim diag As Double = Math.Sqrt((maxX - minX) ^ 2 + (maxY - minY) ^ 2)
        If diag < 1.0E-6 Then Throw New InvalidOperationException("Curve extent is too small for fitting.")
        Dim robustScale As Double = Math.Max(0.001, diag * 0.002)
        Dim starts As New List(Of Point2)
        starts.Add(centroid)
        starts.Add(New Point2(0.5 * (minX + maxX), 0.5 * (minY + maxY)))
        Dim cc As Point2? = CircleCenter(planar)
        If cc.HasValue Then starts.Add(cc.Value)
        For gx As Integer = -1 To 1
            For gy As Integer = -1 To 1
                starts.Add(New Point2(centroid.X + gx * diag * 0.35, centroid.Y + gy * diag * 0.35))
            Next
        Next
        Dim best As Point2 = starts(0), bestValue As Double = Double.PositiveInfinity
        For Each s As Point2 In starts
            Dim candidate As Point2 = NelderMead(planar, s, diag * 0.12, centroid, diag * 5.0, robustScale)
            Dim value As Double = EvaluateCenter(planar, candidate.X, candidate.Y, robustScale).Objective
            If value < bestValue Then bestValue = value : best = candidate
        Next
        best = NelderMead(planar, best, diag * 0.01, centroid, diag * 5.0, robustScale * 0.25)
        Dim eval As CenterEvaluation = EvaluateCenter(planar, best.X, best.Y, robustScale * 0.25)
        Dim sumSq As Double = 0.0, maxErr As Double = 0.0
        For Each e As Double In eval.Residual
            sumSq += e * e : maxErr = Math.Max(maxErr, Math.Abs(e))
        Next
        Dim logs(planar.Length - 1) As Double, meanLog As Double = 0.0
        For i As Integer = 0 To planar.Length - 1
            Dim dx As Double = planar(i).X - best.X, dy As Double = planar(i).Y - best.Y
            logs(i) = Math.Log(Math.Sqrt(dx * dx + dy * dy)) : meanLog += logs(i)
        Next
        meanLog /= logs.Length
        Dim ssTot As Double = 0.0, ssRes As Double = 0.0
        For i As Integer = 0 To logs.Length - 1
            ssTot += (logs(i) - meanLog) ^ 2
            ssRes += (logs(i) - eval.Alpha - eval.B * eval.Theta(i)) ^ 2
        Next
        Dim center3 As Point3 = VectorMath.Add(origin, VectorMath.Add(VectorMath.Scale(axisU, best.X), VectorMath.Scale(axisV, best.Y)))
        Dim thetaFirst As Double = eval.Theta(0)
        Dim thetaLast As Double = eval.Theta(eval.Theta.Length - 1)
        Dim rFirst As Double = Math.Exp(eval.Alpha + eval.B * thetaFirst)
        Dim rLast As Double = Math.Exp(eval.Alpha + eval.B * thetaLast)
        Dim theta0 As Double = If(rFirst <= rLast, thetaFirst, thetaLast)
        Dim thetaOuter As Double = If(rFirst <= rLast, thetaLast - theta0, thetaFirst - theta0)
        Dim cos0 As Double = Math.Cos(theta0)
        Dim sin0 As Double = Math.Sin(theta0)
        Dim alignedU As Point3 = VectorMath.Normalize(VectorMath.Add(VectorMath.Scale(axisU, cos0), VectorMath.Scale(axisV, sin0)))
        Dim alignedV As Point3 = VectorMath.Normalize(VectorMath.Add(VectorMath.Scale(axisU, -sin0), VectorMath.Scale(axisV, cos0)))
        Dim alignedAlpha As Double = eval.Alpha + eval.B * theta0
        Dim alignedTheta(eval.Theta.Length - 1) As Double
        For i As Integer = 0 To eval.Theta.Length - 1
            alignedTheta(i) = eval.Theta(i) - theta0
        Next
        Return New FitResult With {
            .Center = center3, .PlaneOrigin = origin, .AxisU = alignedU, .AxisV = alignedV, .Normal = normal,
            .Center2 = best, .Alpha = alignedAlpha, .B = eval.B,
            .ThetaStart = 0.0, .ThetaEnd = thetaOuter,
            .RmsError = Math.Sqrt(sumSq / eval.Residual.Length), .MaxError = maxErr,
            .PlanarityMax = maxPlane, .R2 = If(ssTot > 0, 1.0 - ssRes / ssTot, 1.0),
            .SourcePoints = source, .Theta = alignedTheta, .Residual = eval.Residual}
    End Function
End Class

Public Class CurveChoice
    Public DocumentPath As String
    Public DocumentName As String
    Public CurveName As String
    Public BodyIndex As Integer
    Public ShapeIndex As Integer
    Public LengthMm As Double
End Class

Public Class CatiaRunner
    Private Shared ReadOnly Inv As CultureInfo = CultureInfo.InvariantCulture

    Private Shared Function FindSourceCurve(ByVal part As Object, ByVal preferredName As String, ByRef bodyIndex As Integer, ByRef shapeIndex As Integer) As Object
        For i As Integer = 1 To part.HybridBodies.Count
            Dim hb As Object = part.HybridBodies.Item(i)
            For j As Integer = 1 To hb.HybridShapes.Count
                Dim sh As Object = hb.HybridShapes.Item(j)
                If String.IsNullOrEmpty(preferredName) OrElse String.Equals(CStr(sh.Name), preferredName, StringComparison.OrdinalIgnoreCase) Then
                    bodyIndex = i : shapeIndex = j : Return sh
                End If
            Next
        Next
        Throw New InvalidOperationException("No top-level hybrid curve was found in the CATPart.")
    End Function

    Private Shared Function SameComObject(ByVal leftObject As Object, ByVal rightObject As Object) As Boolean
        Dim leftPointer As IntPtr = IntPtr.Zero, rightPointer As IntPtr = IntPtr.Zero
        Try
            leftPointer = Marshal.GetIUnknownForObject(leftObject)
            rightPointer = Marshal.GetIUnknownForObject(rightObject)
            Return leftPointer = rightPointer
        Finally
            If leftPointer <> IntPtr.Zero Then Marshal.Release(leftPointer)
            If rightPointer <> IntPtr.Zero Then Marshal.Release(rightPointer)
        End Try
    End Function

    Private Shared Function FindSelectedCurve(ByVal part As Object, ByVal selectedObject As Object, ByRef bodyIndex As Integer, ByRef shapeIndex As Integer) As Object
        For i As Integer = 1 To part.HybridBodies.Count
            Dim hb As Object = part.HybridBodies.Item(i)
            For j As Integer = 1 To hb.HybridShapes.Count
                Dim shape As Object = hb.HybridShapes.Item(j)
                If SameComObject(shape, selectedObject) Then
                    bodyIndex = i
                    shapeIndex = j
                    Return shape
                End If
            Next
        Next
        Throw New InvalidOperationException("请选择几何图形集中的顶层曲线，不要选择点、平面、曲面或实体边。")
    End Function

    Public Shared Function SelectCurveInteractive() As CurveChoice
        Dim catia As Object = Marshal.GetActiveObject("CATIA.Application")
        Dim doc As Object = catia.ActiveDocument
        If doc Is Nothing OrElse Not CStr(doc.Name).EndsWith(".CATPart", StringComparison.OrdinalIgnoreCase) Then
            Throw New InvalidOperationException("请先在 CATIA 中激活一个已保存的 CATPart。")
        End If
        Dim documentPath As String = CStr(doc.FullName)
        If String.IsNullOrWhiteSpace(documentPath) OrElse Not File.Exists(documentPath) Then
            Throw New InvalidOperationException("请先保存当前 CATPart。")
        End If

        doc.Activate()
        Dim selection As Object = doc.Selection
        selection.Clear()
        Dim filters(0) As Object
        filters(0) = "AnyObject"
        Dim status As String = CStr(selection.SelectElement2(filters, "请在几何区或结构树中选择需要拟合的曲线", False))
        If Not String.Equals(status, "Normal", StringComparison.OrdinalIgnoreCase) Then
            selection.Clear()
            Throw New OperationCanceledException("已取消曲线选择。")
        End If

        Dim selectedObject As Object = selection.Item2(1).Value
        Dim selectedName As String = CStr(selectedObject.Name)
        Dim bodyIndex As Integer = 0, shapeIndex As Integer = 0
        Dim curve As Object = FindSelectedCurve(doc.Part, selectedObject, bodyIndex, shapeIndex)
        Dim reference As Object = doc.Part.CreateReferenceFromObject(curve)
        Dim measurable As Object = doc.GetWorkbench("SPAWorkbench").GetMeasurable(reference)
        Dim lengthMm As Double
        Try
            lengthMm = CDbl(measurable.Length)
        Catch
            selection.Clear()
            Throw New InvalidOperationException("所选对象不是可测量的曲线，请重新选择。")
        End Try
        If lengthMm <= 0.0 Then Throw New InvalidOperationException("所选曲线长度必须大于 0。")
        selection.Clear()
        Return New CurveChoice With {
            .DocumentPath = documentPath,
            .DocumentName = CStr(doc.Name),
            .CurveName = selectedName,
            .BodyIndex = bodyIndex,
            .ShapeIndex = shapeIndex,
            .LengthMm = lengthMm
        }
    End Function

    Public Shared Function ComputeSampleCount(ByVal curveLengthMm As Double, ByVal spacingMm As Double) As Integer
        If Double.IsNaN(spacingMm) OrElse Double.IsInfinity(spacingMm) Then spacingMm = 1.0
        If spacingMm < 0.05 Then spacingMm = 0.05
        If spacingMm > 100.0 Then spacingMm = 100.0
        Dim count As Integer = CInt(Math.Round(curveLengthMm / spacingMm)) + 1
        If count < 20 Then count = 20
        If count > 5000 Then count = 5000
        Return count
    End Function

    Private Shared Function SampleCurve(ByVal doc As Object, ByVal part As Object, ByVal curve As Object, ByVal count As Integer, ByVal spacingMm As Double, ByRef tempBody As Object, ByVal progress As Action(Of Integer, String), ByVal cancellation As System.Threading.CancellationToken) As Point3()
        Dim factory As Object = part.HybridShapeFactory
        Dim curveRef As Object = part.CreateReferenceFromObject(curve)
        tempBody = part.HybridBodies.Add()
        tempBody.Name = "__LogSpiral_FitSamples"
        Dim pointObjects As New List(Of Object)
        Dim reportStep As Integer = Math.Max(1, count \ 10)
        For i As Integer = 0 To count - 1
            cancellation.ThrowIfCancellationRequested()
            Dim ratio As Double = CDbl(i) / CDbl(count - 1)
            Dim hp As Object = factory.AddNewPointOnCurveFromPercent(curveRef, ratio, False)
            hp.Name = "FitSample_" & i.ToString("0000", Inv)
            tempBody.AppendHybridShape(hp)
            pointObjects.Add(hp)
            If progress IsNot Nothing AndAlso (i = 0 OrElse i = count - 1 OrElse ((i + 1) Mod reportStep = 0)) Then
                Dim pct As Integer = 10 + CInt(Math.Round(20.0 * CDbl(i + 1) / CDbl(count)))
                progress(pct, String.Format(Inv, "[步骤 2/6] 正在布置采样点 ({0}/{1}，间距 {2:F2} mm)…", i + 1, count, spacingMm))
            End If
        Next
        If progress IsNot Nothing Then progress(32, "[步骤 2/6] 正在更新曲线采样点几何…")
        part.Update()
        Dim result(count - 1) As Point3
        Dim spa As Object = doc.GetWorkbench("SPAWorkbench")
        For i As Integer = 0 To count - 1
            cancellation.ThrowIfCancellationRequested()
            Dim pr As Object = part.CreateReferenceFromObject(pointObjects(i))
            Dim measurable As Object = spa.GetMeasurable(pr)
            Dim coordinates(2) As Object
            measurable.GetPoint(coordinates)
            result(i) = New Point3(CDbl(coordinates(0)), CDbl(coordinates(1)), CDbl(coordinates(2)))
            If progress IsNot Nothing AndAlso (i = 0 OrElse i = count - 1 OrElse ((i + 1) Mod reportStep = 0)) Then
                Dim pct As Integer = 35 + CInt(Math.Round(15.0 * CDbl(i + 1) / CDbl(count)))
                progress(pct, String.Format(Inv, "[步骤 2/6] 正在读取采样点三维坐标 ({0}/{1})…", i + 1, count))
            End If
        Next
        Return result
    End Function

    Private Shared Sub SetObjectsVisibility(ByVal doc As Object, ByVal objects As IEnumerable(Of Object), ByVal show As Boolean)
        If doc Is Nothing OrElse objects Is Nothing Then Return
        Dim sel As Object = doc.Selection
        sel.Clear()
        For Each obj As Object In objects
            If obj IsNot Nothing Then
                Try : sel.Add(obj) : Catch : End Try
            End If
        Next
        If CInt(sel.Count) > 0 Then
            sel.VisProperties.SetShow(If(show, 0, 1))
        End If
        sel.Clear()
    End Sub

    Private Shared Sub DeleteTemporaryBody(ByVal doc As Object, ByVal body As Object)
        If body Is Nothing Then Return
        Dim selection As Object = doc.Selection
        selection.Clear()
        selection.Add(body)
        selection.Delete()
        selection.Clear()
    End Sub

    Private Shared Sub AddParameter(ByVal parameters As Object, ByVal name As String, ByVal value As String)
        parameters.CreateString(name, value)
    End Sub

    Private Shared Function AddLengthParameter(ByVal parameters As Object, ByVal name As String, ByVal valueMm As Double) As Object
        Dim parameter As Object = parameters.CreateDimension(name, "LENGTH", 0.0)
        parameter.ValuateFromString(valueMm.ToString("R", Inv) & "mm")
        Return parameter
    End Function

    Private Shared Function AddAngleParameter(ByVal parameters As Object, ByVal name As String, ByVal valueRad As Double) As Object
        Dim parameter As Object = parameters.CreateDimension(name, "ANGLE", 0.0)
        parameter.ValuateFromString(valueRad.ToString("R", Inv) & "rad")
        Return parameter
    End Function

    Private Shared Function AddRealParameter(ByVal parameters As Object, ByVal name As String, ByVal value As Double) As Object
        Return parameters.CreateReal(name, value)
    End Function

    Private Shared Function GetCoordinateLawExpressions(ByVal fit As FitResult) As String()
        ' Root parameter names can be referenced directly by Knowledgeware laws.
        ' Avoid Parameters.GetNameToUseInRelation here: some CATIA V5 releases do not expose
        ' that method through late-bound VB.NET COM and return DISP_E_MEMBERNOTFOUND.
        Dim centerXName As String = "Center_X"
        Dim centerYName As String = "Center_Y"
        Dim centerZName As String = "Center_Z"
        Dim spiralAName As String = "LogSpiral_a"
        Dim spiralBName As String = "LogSpiral_b"
        Dim thetaStartName As String = "Theta_Start"
        Dim thetaEndName As String = "Theta_End"
        Dim axisUXName As String = "Axis_U_X"
        Dim axisUYName As String = "Axis_U_Y"
        Dim axisUZName As String = "Axis_U_Z"
        Dim axisVXName As String = "Axis_V_X"
        Dim axisVYName As String = "Axis_V_Y"
        Dim axisVZName As String = "Axis_V_Z"
        Dim thetaExpr As String = "(" & thetaStartName & "+(" & thetaEndName & "-" & thetaStartName & ")*FormalReal.1)"
        Dim radialExpr As String = "(" & spiralAName & "*exp(" & spiralBName & "*(" & thetaExpr & ")/1rad))"
        Dim xExpr As String = centerXName & "+" & radialExpr & "*(" & axisUXName & "*cos(" & thetaExpr & ")+" & axisVXName & "*sin(" & thetaExpr & "))"
        Dim yExpr As String = centerYName & "+" & radialExpr & "*(" & axisUYName & "*cos(" & thetaExpr & ")+" & axisVYName & "*sin(" & thetaExpr & "))"
        Dim zExpr As String = centerZName & "+" & radialExpr & "*(" & axisUZName & "*cos(" & thetaExpr & ")+" & axisVZName & "*sin(" & thetaExpr & "))"
        Return New String() {xExpr, yExpr, zExpr}
    End Function

    Private Shared Function CreateCoordinateLaw(ByVal relations As Object, ByVal name As String, ByVal body As String) As Object
        Dim law As Object = relations.CreateLaw(name, "Logarithmic spiral coordinate law; input t is normalized from 0 to 1.", "")
        law.AddFormalParameter("FormalLength.1", "Length")
        law.AddFormalParameter("FormalReal.1", "Real")
        law.Modify("FormalLength.1 = " & body)
        Return law
    End Function

    Private Shared Function FindRelationByNameOrPrefix(ByVal relations As Object, ByVal baseName As String) As Object
        Dim prefix As String = baseName & "."
        Dim exactMatch As Object = Nothing
        Dim prefixMatch As Object = Nothing
        For i As Integer = CInt(relations.Count) To 1 Step -1
            Dim rel As Object = relations.Item(i)
            Dim relName As String = ""
            Try
                relName = CStr(rel.Name)
            Catch
            End Try
            If String.Equals(relName, baseName, StringComparison.OrdinalIgnoreCase) Then
                exactMatch = rel
                Exit For
            ElseIf prefixMatch Is Nothing AndAlso relName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) Then
                prefixMatch = rel
            End If
        Next
        Return If(exactMatch IsNot Nothing, exactMatch, prefixMatch)
    End Function

    Private Shared Sub EnsureCoordinateLawsBound(ByVal relations As Object, ByVal exprs() As String)
        Dim lawNames() As String = {"LogSpiral_X_StandardEquation", "LogSpiral_Y_StandardEquation", "LogSpiral_Z_StandardEquation"}
        For idx As Integer = 0 To 2
            Dim law As Object = FindRelationByNameOrPrefix(relations, lawNames(idx))
            If law IsNot Nothing Then
                law.Modify("FormalLength.1 = " & exprs(idx))
            Else
                CreateCoordinateLaw(relations, lawNames(idx), exprs(idx))
            End If
        Next
    End Sub

    Private Shared Function FormatSpiralCurveName(ByVal fit As FitResult) As String
        Dim aMm As Double = Math.Exp(fit.Alpha)
        Dim startDeg As Double = fit.ThetaStart * 180.0 / Math.PI
        Dim endDeg As Double = fit.ThetaEnd * 180.0 / Math.PI
        Return String.Format(Inv, "对数螺旋线_r={0:F3}*exp({1:F5}*θ)_a={0:F3}mm_b={1:F5}_θ={2:F1}deg~{3:F1}deg", aMm, fit.B, startDeg, endDeg)
    End Function

    Private Shared Function MeasureCurveLength(ByVal doc As Object, ByVal part As Object, ByVal curveObj As Object) As Double
        Try
            Dim reference As Object = part.CreateReferenceFromObject(curveObj)
            Dim spa As Object = doc.GetWorkbench("SPAWorkbench")
            Return CDbl(spa.GetMeasurable(reference).Length)
        Catch
            Return Double.NaN
        End Try
    End Function

    Private Shared Sub WriteHiddenProcessFile(ByVal filePath As String, ByVal content As String)
        If File.Exists(filePath) Then
            Dim existingAttrs As FileAttributes = File.GetAttributes(filePath)
            If (existingAttrs And FileAttributes.Hidden) = FileAttributes.Hidden Then
                File.SetAttributes(filePath, existingAttrs And Not FileAttributes.Hidden)
            End If
        End If
        File.WriteAllText(filePath, content, New UTF8Encoding(False))
        File.SetAttributes(filePath, File.GetAttributes(filePath) Or FileAttributes.Hidden)
    End Sub

    Private Shared Function BuildEquationDefinition(ByVal doc As Object, ByVal part As Object, ByVal fit As FitResult, ByVal createLaws As Boolean, ByRef paramMap As Dictionary(Of String, Object)) As Object()
        Dim factory As Object = part.HybridShapeFactory
        Dim resultBody As Object = part.HybridBodies.Add()
        resultBody.Name = "LogSpiral_Equation"

        Dim centerPoint As Object = factory.AddNewPointCoord(fit.Center.X, fit.Center.Y, fit.Center.Z)
        centerPoint.Name = "Fitted_Center"
        resultBody.AppendHybridShape(centerPoint)
        Dim axisLength As Double = Math.Max(10.0, Math.Exp(fit.Alpha) * 0.25)
        Dim uEnd3 As Point3 = VectorMath.Add(fit.Center, VectorMath.Scale(fit.AxisU, axisLength))
        Dim vEnd3 As Point3 = VectorMath.Add(fit.Center, VectorMath.Scale(fit.AxisV, axisLength))
        Dim uEnd As Object = factory.AddNewPointCoord(uEnd3.X, uEnd3.Y, uEnd3.Z)
        uEnd.Name = "Reference_U_End" : resultBody.AppendHybridShape(uEnd)
        Dim vEnd As Object = factory.AddNewPointCoord(vEnd3.X, vEnd3.Y, vEnd3.Z)
        vEnd.Name = "Reference_V_End" : resultBody.AppendHybridShape(vEnd)
        part.Update()
        Dim axisULine As Object = factory.AddNewLinePtPt(part.CreateReferenceFromObject(centerPoint), part.CreateReferenceFromObject(uEnd))
        axisULine.Name = "Reference_Axis_U" : resultBody.AppendHybridShape(axisULine)
        Dim axisVLine As Object = factory.AddNewLinePtPt(part.CreateReferenceFromObject(centerPoint), part.CreateReferenceFromObject(vEnd))
        axisVLine.Name = "Reference_Axis_V" : resultBody.AppendHybridShape(axisVLine)
        Dim d As Double = -(fit.Normal.X * fit.Center.X + fit.Normal.Y * fit.Center.Y + fit.Normal.Z * fit.Center.Z)
        Dim plane As Object = factory.AddNewPlaneEquation(fit.Normal.X, fit.Normal.Y, fit.Normal.Z, d)
        plane.Name = "Fitted_Plane"
        resultBody.AppendHybridShape(plane)
        part.Update()

        Dim baseRadiusMm As Double = Math.Exp(fit.Alpha)
        Dim baseCircle As Object = factory.AddNewCircleCtrRad(part.CreateReferenceFromObject(centerPoint), part.CreateReferenceFromObject(plane), False, baseRadiusMm)
        Try : baseCircle.SetLimitation(1) : Catch : End Try
        baseCircle.Name = String.Format(Inv, "基圆_R={0:F3}mm_D={1:F3}mm", baseRadiusMm, baseRadiusMm * 2.0)
        resultBody.AppendHybridShape(baseCircle)
        part.Update()

        Dim parameters As Object = part.Parameters
        Dim relations As Object = part.Relations
        AddParameter(parameters, "LogSpiral_Model", "r = LogSpiral_a * exp(LogSpiral_b * theta)")
        Dim pCenterX As Object = AddLengthParameter(parameters, "Center_X", fit.Center.X)
        Dim pCenterY As Object = AddLengthParameter(parameters, "Center_Y", fit.Center.Y)
        Dim pCenterZ As Object = AddLengthParameter(parameters, "Center_Z", fit.Center.Z)
        Dim pSpiralA As Object = AddLengthParameter(parameters, "LogSpiral_a", baseRadiusMm)
        Dim pSpiralB As Object = AddRealParameter(parameters, "LogSpiral_b", fit.B)
        Dim pThetaStart As Object = AddAngleParameter(parameters, "Theta_Start", fit.ThetaStart)
        Dim pThetaEnd As Object = AddAngleParameter(parameters, "Theta_End", fit.ThetaEnd)
        Dim pAxisUX As Object = AddRealParameter(parameters, "Axis_U_X", fit.AxisU.X)
        Dim pAxisUY As Object = AddRealParameter(parameters, "Axis_U_Y", fit.AxisU.Y)
        Dim pAxisUZ As Object = AddRealParameter(parameters, "Axis_U_Z", fit.AxisU.Z)
        Dim pAxisVX As Object = AddRealParameter(parameters, "Axis_V_X", fit.AxisV.X)
        Dim pAxisVY As Object = AddRealParameter(parameters, "Axis_V_Y", fit.AxisV.Y)
        Dim pAxisVZ As Object = AddRealParameter(parameters, "Axis_V_Z", fit.AxisV.Z)
        AddLengthParameter(parameters, "Base_Circle_Radius", baseRadiusMm)
        AddLengthParameter(parameters, "Base_Circle_Diameter", baseRadiusMm * 2.0)
        AddRealParameter(parameters, "Plane_Normal_X", fit.Normal.X)
        AddRealParameter(parameters, "Plane_Normal_Y", fit.Normal.Y)
        AddRealParameter(parameters, "Plane_Normal_Z", fit.Normal.Z)
        AddLengthParameter(parameters, "Fit_RMS_Error", fit.RmsError)
        AddLengthParameter(parameters, "Fit_Max_Error", fit.MaxError)
        AddRealParameter(parameters, "Fit_R2", fit.R2)
        AddParameter(parameters, "LogSpiral_Rotation", If(fit.ThetaEnd < fit.ThetaStart, "negative_about_normal", "positive_about_normal"))
        AddParameter(parameters, "LogSpiral_Representation", "Native GSMCurveEquations driven by named standard-equation parameters; no spline")

        paramMap = New Dictionary(Of String, Object)(StringComparer.Ordinal) From {
            {"Center_X", pCenterX},
            {"Center_Y", pCenterY},
            {"Center_Z", pCenterZ},
            {"LogSpiral_a", pSpiralA},
            {"LogSpiral_b", pSpiralB},
            {"Theta_Start", pThetaStart},
            {"Theta_End", pThetaEnd},
            {"Axis_U_X", pAxisUX},
            {"Axis_U_Y", pAxisUY},
            {"Axis_U_Z", pAxisUZ},
            {"Axis_V_X", pAxisVX},
            {"Axis_V_Y", pAxisVY},
            {"Axis_V_Z", pAxisVZ}
        }

        Dim lawX As Object = Nothing
        Dim lawY As Object = Nothing
        Dim lawZ As Object = Nothing
        If createLaws Then
            Dim exprs() As String = GetCoordinateLawExpressions(fit)
            lawX = CreateCoordinateLaw(relations, "LogSpiral_X_StandardEquation", exprs(0))
            lawY = CreateCoordinateLaw(relations, "LogSpiral_Y_StandardEquation", exprs(1))
            lawZ = CreateCoordinateLaw(relations, "LogSpiral_Z_StandardEquation", exprs(2))
        End If
        part.Update()
        Dim auxShapes() As Object = {centerPoint, uEnd, vEnd, axisULine, axisVLine, plane}
        Return New Object() {resultBody, lawX, lawY, lawZ, baseCircle, auxShapes}
    End Function

    Private Shared Sub SetComPropertyPut(ByVal target As Object, ByVal propertyName As String, ByVal value As Object)
        target.GetType().InvokeMember(propertyName, System.Reflection.BindingFlags.PutDispProperty, Nothing, target, New Object() {value})
    End Sub

    Private Shared Sub CloseDocumentIfOpen(ByVal catiaApp As Object, ByVal filePath As String)
        Dim docs As Object = catiaApp.Documents
        For i As Integer = CInt(docs.Count) To 1 Step -1
            Dim candidate As Object = docs.Item(i)
            Try
                If String.Equals(CStr(candidate.FullName), filePath, StringComparison.OrdinalIgnoreCase) Then
                    candidate.Close()
                End If
            Catch
            End Try
        Next
    End Sub

    Private Shared Function SnapshotHybridShapeCounts(ByVal part As Object) As Dictionary(Of Integer, Integer)
        Dim counts As New Dictionary(Of Integer, Integer)()
        Dim bodies As Object = part.HybridBodies
        For i As Integer = 1 To CInt(bodies.Count)
            counts(i) = CInt(bodies.Item(i).HybridShapes.Count)
        Next
        Return counts
    End Function

    Private Shared Function FindNewEquationCurve(ByVal part As Object, ByVal resultBody As Object, ByVal initialResultShapeCount As Integer, ByVal initialBodyCount As Integer, ByVal initialCounts As Dictionary(Of Integer, Integer)) As Object
        Dim resultCount As Integer = CInt(resultBody.HybridShapes.Count)
        If resultCount > initialResultShapeCount Then
            Return resultBody.HybridShapes.Item(resultCount)
        End If
        Dim bodies As Object = part.HybridBodies
        Dim bodyCount As Integer = CInt(bodies.Count)
        For i As Integer = initialBodyCount + 1 To bodyCount
            Dim b As Object = bodies.Item(i)
            Dim cnt As Integer = CInt(b.HybridShapes.Count)
            If cnt > 0 Then
                Return b.HybridShapes.Item(cnt)
            End If
        Next
        For i As Integer = 1 To initialBodyCount
            Dim b As Object = bodies.Item(i)
            Dim cnt As Integer = CInt(b.HybridShapes.Count)
            Dim prev As Integer = 0
            If initialCounts.ContainsKey(i) Then prev = initialCounts(i)
            If cnt > prev Then
                Return b.HybridShapes.Item(cnt)
            End If
        Next
        Return Nothing
    End Function

    Private Shared Function FindShapeInPart(ByVal part As Object, ByVal shapeName As String) As Object
        Dim bodies As Object = part.HybridBodies
        For i As Integer = 1 To CInt(bodies.Count)
            Dim shapes As Object = bodies.Item(i).HybridShapes
            For j As Integer = 1 To CInt(shapes.Count)
                Dim s As Object = shapes.Item(j)
                Try
                    If String.Equals(CStr(s.Name), shapeName, StringComparison.OrdinalIgnoreCase) Then
                        Return s
                    End If
                Catch
                End Try
            Next
        Next
        Return Nothing
    End Function

    Private Shared Sub CopyTemplateEquationBySelection(ByVal doc As Object, ByVal resultBody As Object, ByVal templatePath As String)
        Dim catiaApp As Object = doc.Application
        Dim tempTplPath As String = Path.Combine(Path.GetTempPath(), "__LogSpiral_Tpl_" & Guid.NewGuid().ToString("N") & ".CATPart")
        File.Copy(templatePath, tempTplPath, True)
        Try
            Dim attrs As FileAttributes = File.GetAttributes(tempTplPath)
            If (attrs And FileAttributes.ReadOnly) = FileAttributes.ReadOnly Then
                File.SetAttributes(tempTplPath, attrs And Not FileAttributes.ReadOnly)
            End If
        Catch
        End Try

        Try : catiaApp.DisplayFileAlerts = False : Catch : End Try
        Dim tplDoc As Object = Nothing
        Try
            tplDoc = catiaApp.Documents.Open(tempTplPath)
            Dim tplPart As Object = tplDoc.Part
            Dim tplSel As Object = tplDoc.Selection
            tplSel.Clear()
            Dim tplRelations As Object = tplPart.Relations
            Dim lawNames() As String = {"LogSpiral_X_StandardEquation", "LogSpiral_Y_StandardEquation", "LogSpiral_Z_StandardEquation"}
            For Each lawName As String In lawNames
                Dim lawObj As Object = FindRelationByNameOrPrefix(tplRelations, lawName)
                If lawObj IsNot Nothing Then tplSel.Add(lawObj)
            Next
            Dim tplCurve As Object = FindShapeInPart(tplPart, "LogSpiral_Standard_Equation")
            If tplCurve Is Nothing Then
                Dim bodies As Object = tplPart.HybridBodies
                For i As Integer = 1 To CInt(bodies.Count)
                    Dim b As Object = bodies.Item(i)
                    If String.Equals(CStr(b.Name), "LogSpiral_Equation", StringComparison.OrdinalIgnoreCase) AndAlso CInt(b.HybridShapes.Count) > 0 Then
                        tplCurve = b.HybridShapes.Item(CInt(b.HybridShapes.Count))
                        Exit For
                    End If
                Next
            End If
            If tplCurve Is Nothing Then
                Throw New InvalidOperationException("模板文件中未找到 LogSpiral_Standard_Equation 曲线。")
            End If
            tplSel.Add(tplCurve)
            tplSel.Copy()
            tplSel.Clear()

            doc.Activate()
            Dim dstSel As Object = doc.Selection
            dstSel.Clear()
            dstSel.Add(resultBody)
            dstSel.Paste()
            dstSel.Clear()
        Finally
            If tplDoc IsNot Nothing Then
                Try : tplDoc.Close() : Catch : End Try
            End If
            Try : catiaApp.DisplayFileAlerts = True : Catch : End Try
            Try
                If File.Exists(tempTplPath) Then File.Delete(tempTplPath)
            Catch
            End Try
        End Try
    End Sub

    Private Shared Function InstantiateEquationCurveFromPowerCopy(ByVal doc As Object, ByVal part As Object, ByVal resultBody As Object, ByVal paramMap As Dictionary(Of String, Object), ByVal fit As FitResult, ByVal templatePath As String) As Object
        Dim subStep As String = "init"
        Try
            doc.Activate()

            Dim initialResultShapeCount As Integer = CInt(resultBody.HybridShapes.Count)
            Dim initialBodyCount As Integer = CInt(part.HybridBodies.Count)
            Dim initialCounts As Dictionary(Of Integer, Integer) = SnapshotHybridShapeCounts(part)

            subStep = "part.InWorkObject"
            Try
                SetComPropertyPut(part, "InWorkObject", resultBody)
            Catch
            End Try

            subStep = "CopyTemplateEquationBySelection"
            CopyTemplateEquationBySelection(doc, resultBody, templatePath)
            Dim equationCurve As Object = FindNewEquationCurve(part, resultBody, initialResultShapeCount, initialBodyCount, initialCounts)

            If equationCurve Is Nothing Then
                Throw New InvalidOperationException("从模板复制方程曲线后未在目标 Part 中发现新增方程曲线。")
            End If

            subStep = "EnsureCoordinateLawsBound"
            EnsureCoordinateLawsBound(part.Relations, GetCoordinateLawExpressions(fit))

            subStep = "rename and update"
            equationCurve.Name = FormatSpiralCurveName(fit)
            part.Update()
            Return equationCurve
        Catch ex As Exception
            Throw New InvalidOperationException("[" & subStep & "] " & ex.Message, ex)
        End Try
    End Function

    Private Shared Function WaitForEquationCurve(ByVal doc As Object, ByVal part As Object, ByVal resultBody As Object, ByVal initialShapeCount As Integer, ByVal fit As FitResult, ByVal timeoutSeconds As Integer, ByVal cancellation As System.Threading.CancellationToken) As Object
        Dim deadline As DateTime = DateTime.Now.AddSeconds(timeoutSeconds)
        Dim lastError As String = "尚未发现可更新的新增曲线。"
        Do While DateTime.Now < deadline
            cancellation.ThrowIfCancellationRequested()
            Dim equationCurve As Object = Nothing
            Try
                Dim shapeCount As Integer = CInt(resultBody.HybridShapes.Count)
                If shapeCount > initialShapeCount Then
                    Dim candidate As Object = resultBody.HybridShapes.Item(shapeCount)
                    If CBool(part.IsUpToDate(candidate)) Then equationCurve = candidate
                End If
            Catch ex As Exception
                lastError = ex.Message
            End Try
            If equationCurve IsNot Nothing Then
                equationCurve.Name = FormatSpiralCurveName(fit)
                part.Update()
                Return equationCurve
            End If
            System.Threading.Thread.Sleep(500)
        Loop
        Throw New TimeoutException("等待 CATIA 方程曲线超时。最后检测信息：" & lastError)
    End Function

    Private Shared Sub WriteReports(ByVal outputPath As String, ByVal fit As FitResult)
        Dim basePath As String = Path.Combine(Path.GetDirectoryName(outputPath), Path.GetFileNameWithoutExtension(outputPath))
        Dim baseRadiusMm As Double = Math.Exp(fit.Alpha)
        Dim json As New StringBuilder()
        json.AppendLine("{")
        json.AppendLine("  ""model"": ""r = LogSpiral_a * exp(LogSpiral_b * theta)"",")
        json.AppendLine("  ""representation"": ""CATIA native GSMCurveEquations driven by named standard-equation parameters; no spline"",")
        json.AppendLine("  ""parameter_names"": [""Center_X"", ""Center_Y"", ""Center_Z"", ""LogSpiral_a"", ""LogSpiral_b"", ""Theta_Start"", ""Theta_End"", ""Base_Circle_Radius"", ""Base_Circle_Diameter""],")
        json.AppendLine("  ""sample_spacing_mm"": " & fit.SampleSpacingMm.ToString("R", Inv) & ",")
        json.AppendLine("  ""sample_count"": " & fit.SampleCount.ToString(Inv) & ",")
        json.AppendLine("  ""LogSpiral_a_mm"": " & baseRadiusMm.ToString("R", Inv) & ",")
        json.AppendLine("  ""base_circle_radius_mm"": " & baseRadiusMm.ToString("R", Inv) & ",")
        json.AppendLine("  ""base_circle_diameter_mm"": " & (baseRadiusMm * 2.0).ToString("R", Inv) & ",")
        json.AppendLine("  ""LogSpiral_b"": " & fit.B.ToString("R", Inv) & ",")
        json.AppendLine("  ""theta_start_rad"": " & fit.ThetaStart.ToString("R", Inv) & ",")
        json.AppendLine("  ""theta_end_rad"": " & fit.ThetaEnd.ToString("R", Inv) & ",")
        json.AppendLine("  ""theta_start_deg"": " & (fit.ThetaStart * 180.0 / Math.PI).ToString("R", Inv) & ",")
        json.AppendLine("  ""theta_end_deg"": " & (fit.ThetaEnd * 180.0 / Math.PI).ToString("R", Inv) & ",")
        json.AppendLine("  ""angular_span_deg"": " & (Math.Abs(fit.ThetaEnd - fit.ThetaStart) * 180.0 / Math.PI).ToString("R", Inv) & ",")
        json.AppendLine("  ""expansion_angle_deg"": " & (Math.Atan(fit.B) * 180.0 / Math.PI).ToString("R", Inv) & ",")
        json.AppendLine(String.Format(Inv, "  ""center_mm"": [{0:R}, {1:R}, {2:R}],", fit.Center.X, fit.Center.Y, fit.Center.Z))
        json.AppendLine(String.Format(Inv, "  ""plane_normal"": [{0:R}, {1:R}, {2:R}],", fit.Normal.X, fit.Normal.Y, fit.Normal.Z))
        json.AppendLine(String.Format(Inv, "  ""axis_u"": [{0:R}, {1:R}, {2:R}],", fit.AxisU.X, fit.AxisU.Y, fit.AxisU.Z))
        json.AppendLine(String.Format(Inv, "  ""axis_v"": [{0:R}, {1:R}, {2:R}],", fit.AxisV.X, fit.AxisV.Y, fit.AxisV.Z))
        json.AppendLine("  ""rotation"": """ & If(fit.ThetaEnd < fit.ThetaStart, "negative_about_normal", "positive_about_normal") & """,")
        json.AppendLine("  ""angular_span_rad"": " & Math.Abs(fit.ThetaEnd - fit.ThetaStart).ToString("R", Inv) & ",")
        json.AppendLine("  ""turns"": " & (Math.Abs(fit.ThetaEnd - fit.ThetaStart) / (2.0 * Math.PI)).ToString("R", Inv) & ",")
        json.AppendLine("  ""rms_error_mm"": " & fit.RmsError.ToString("R", Inv) & ",")
        json.AppendLine("  ""max_error_mm"": " & fit.MaxError.ToString("R", Inv) & ",")
        json.AppendLine("  ""planarity_max_mm"": " & fit.PlanarityMax.ToString("R", Inv) & ",")
        json.AppendLine("  ""r2_logpolar"": " & fit.R2.ToString("R", Inv))
        json.AppendLine("}")
        WriteHiddenProcessFile(basePath & "_report.json", json.ToString())

        Dim csv As New StringBuilder("index,source_x_mm,source_y_mm,source_z_mm,theta_rad,normal_residual_mm" & Environment.NewLine)
        For i As Integer = 0 To fit.SourcePoints.Length - 1
            Dim p As Point3 = fit.SourcePoints(i)
            csv.AppendLine(String.Format(Inv, "{0},{1:R},{2:R},{3:R},{4:R},{5:R}", i, p.X, p.Y, p.Z, fit.Theta(i), fit.Residual(i)))
        Next
        WriteHiddenProcessFile(basePath & "_samples.csv", csv.ToString())
    End Sub

    Public Shared Function Run(ByVal spacingMm As Double, ByVal choice As CurveChoice, Optional ByVal progress As Action(Of Integer, String) = Nothing, Optional ByVal cancellation As System.Threading.CancellationToken = Nothing, Optional ByVal reportReady As Action(Of String) = Nothing) As String
        If choice Is Nothing Then Throw New InvalidOperationException("请先选择需要拟合的曲线。")
        Dim sampleCount As Integer = ComputeSampleCount(choice.LengthMm, spacingMm)
        If progress IsNot Nothing Then progress(5, "[步骤 1/6] 正在复制源 CATPart 并打开工作文档…")
        Dim catia As Object = Marshal.GetActiveObject("CATIA.Application")
        Dim sourceDoc As Object = Nothing
        For i As Integer = 1 To catia.Documents.Count
            Dim candidate As Object = catia.Documents.Item(i)
            Try
                If String.Equals(CStr(candidate.FullName), choice.DocumentPath, StringComparison.OrdinalIgnoreCase) Then
                    sourceDoc = candidate
                    Exit For
                End If
            Catch
            End Try
        Next
        If sourceDoc Is Nothing Then Throw New InvalidOperationException("选择曲线所在的 CATPart 已关闭，请重新选择。")
        sourceDoc.Activate()
        Dim sourcePath As String = choice.DocumentPath
        If String.IsNullOrWhiteSpace(sourcePath) OrElse Not File.Exists(sourcePath) Then
            Throw New InvalidOperationException("选择的 CATPart 尚未保存。")
        End If
        Dim bodyIndex As Integer = choice.BodyIndex
        Dim shapeIndex As Integer = choice.ShapeIndex

        Dim directory As String = Path.GetDirectoryName(sourcePath)
        Dim stem As String = Path.GetFileNameWithoutExtension(sourcePath)
        Dim outputPath As String = Path.Combine(directory, stem & "_LogSpiralEquation.CATPart")
        If File.Exists(outputPath) Then
            outputPath = Path.Combine(directory, stem & "_LogSpiralEquation_" & DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv) & ".CATPart")
        End If
        File.Copy(sourcePath, outputPath, False)
        Dim outputDoc As Object = catia.Documents.Open(outputPath)
        Dim outputPart As Object = outputDoc.Part
        Dim curve As Object = outputPart.HybridBodies.Item(bodyIndex).HybridShapes.Item(shapeIndex)
        Dim tempBody As Object = Nothing
        Dim stage As String = "sampling"
        Try
            cancellation.ThrowIfCancellationRequested()
            If progress IsNot Nothing Then
                progress(10, String.Format(Inv, "[步骤 2/6] 曲线长 {0:F2} mm，按 {1:F2} mm 间距自适应采样 {2} 点…", choice.LengthMm, spacingMm, sampleCount))
            End If
            Dim samples() As Point3 = SampleCurve(outputDoc, outputPart, curve, sampleCount, spacingMm, tempBody, progress, cancellation)
            cancellation.ThrowIfCancellationRequested()
            stage = "numerical fitting"
            If progress IsNot Nothing Then progress(55, "[步骤 3/6] 正在拟合平面、中心与对数螺旋线参数 (共 " & sampleCount.ToString(Inv) & " 点)…")
            Dim fit As FitResult = SpiralMath.Fit(samples)
            fit.SampleSpacingMm = spacingMm
            fit.SampleCount = sampleCount
            cancellation.ThrowIfCancellationRequested()
            If reportReady IsNot Nothing Then reportReady(FormatParameterReport(fit, Double.NaN))
            stage = "building equation laws"
            Dim templatePath As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LogSpiral_PowerCopy.CATPart")
            Dim usePowerCopy As Boolean = File.Exists(templatePath)
            If progress IsNot Nothing Then
                progress(70, "[步骤 4/6] 正在创建参考几何、基圆与标准方程命名参数…")
            End If
            Dim paramMap As Dictionary(Of String, Object) = Nothing
            Dim equationData() As Object = BuildEquationDefinition(outputDoc, outputPart, fit, Not usePowerCopy, paramMap)
            Dim resultBody As Object = equationData(0)
            Dim baseCircle As Object = equationData(4)
            Dim auxShapes() As Object = DirectCast(equationData(5), Object())
            Dim equationCurve As Object
            If usePowerCopy Then
                stage = "instantiating PowerCopy equation curve"
                If progress IsNot Nothing Then progress(82, "[步骤 5/6] 正在生成原生 GSMCurveEquations 对数螺旋线…")
                equationCurve = InstantiateEquationCurveFromPowerCopy(outputDoc, outputPart, resultBody, paramMap, fit, templatePath)
            Else
                stage = "opening CATIA Curve from Equations command"
                catia.ActiveWindow.ActiveViewer.Reframe()
                Dim selection As Object = outputDoc.Selection
                selection.Clear()
                selection.Add(equationData(1))
                Dim initialShapeCount As Integer = CInt(resultBody.HybridShapes.Count)
                catia.StartCommand("CATShapeDesignCurveEquations")
                stage = "waiting for the native equation curve"
                If progress IsNot Nothing Then progress(82, "[步骤 5/6] 请在 CATIA 方程曲线窗口中选择 X/Y/Z 三条法则，然后点[确定]…")
                equationCurve = WaitForEquationCurve(outputDoc, outputPart, resultBody, initialShapeCount, fit, 900, cancellation)
            End If
            stage = "finalizing visibility and saving"
            If progress IsNot Nothing Then progress(94, "[步骤 6/6] 正在设置螺旋线参数化名称、隐藏过程几何与过程文件并保存…")
            equationCurve.Name = FormatSpiralCurveName(fit)
            Dim hideList As New List(Of Object)()
            If tempBody IsNot Nothing Then hideList.Add(tempBody)
            If auxShapes IsNot Nothing Then hideList.AddRange(auxShapes)
            SetObjectsVisibility(outputDoc, hideList, False)
            SetObjectsVisibility(outputDoc, New Object() {resultBody, baseCircle, equationCurve}, True)
            outputPart.Update()
            outputDoc.Save()
            stage = "writing reports"
            WriteReports(outputPath, fit)
            Dim nativeCurveLength As Double = MeasureCurveLength(outputDoc, outputPart, equationCurve)
            If progress IsNot Nothing Then progress(100, "[完成] 对数螺旋线与基圆已生成并保存。")
            Return outputPath & Environment.NewLine &
                FormatParameterReport(fit, nativeCurveLength)
        Catch ex As OperationCanceledException
            Try : DeleteTemporaryBody(outputDoc, tempBody) : Catch : End Try
            Throw
        Catch ex As Exception
            Try : DeleteTemporaryBody(outputDoc, tempBody) : Catch : End Try
            Throw New InvalidOperationException("Stage '" & stage & "' failed: " & ex.Message, ex)
        End Try
    End Function

    Public Shared Function FormatParameterReport(ByVal fit As FitResult, ByVal nativeCurveLength As Double) As String
        Dim report As New StringBuilder()
        Dim aMm As Double = Math.Exp(fit.Alpha)
        report.AppendLine("标准对数螺旋线：r = a · e^(bθ)    [θ 用弧度，r 用 mm]")
        report.AppendLine(String.Format(Inv, "r = {0:F9} · e^({1:F12} · θ) mm", aMm, fit.B))
        report.AppendLine(String.Format(Inv, "a = {0:F9} mm    b = {1:F12}", aMm, fit.B))
        report.AppendLine(String.Format(Inv, "基圆半径 r0 = a = {0:F9} mm    基圆直径 D0 = {1:F9} mm", aMm, aMm * 2.0))
        If fit.SampleCount > 0 Then
            report.AppendLine(String.Format(Inv, "采样策略：间距 {0:F2} mm    自适应点数 = {1} 点", fit.SampleSpacingMm, fit.SampleCount))
        End If
        report.AppendLine(String.Format(Inv, "中心 (X, Y, Z) = ({0:F6}, {1:F6}, {2:F6}) mm", fit.Center.X, fit.Center.Y, fit.Center.Z))
        report.AppendLine(String.Format(Inv, "扩展角 α = atan(b) = {0:F9}°（切线与圆周方向的有符号夹角）", Math.Atan(fit.B) * 180.0 / Math.PI))
        report.AppendLine(String.Format(Inv, "起始角 = {0:F9}°    终止角 = {1:F9}°", fit.ThetaStart * 180.0 / Math.PI, fit.ThetaEnd * 180.0 / Math.PI))
        report.AppendLine(String.Format(Inv, "展开角（覆盖角）= {0:F9}°    圈数 = {1:F9}", Math.Abs(fit.ThetaEnd - fit.ThetaStart) * 180.0 / Math.PI, Math.Abs(fit.ThetaEnd - fit.ThetaStart) / (2.0 * Math.PI)))
        report.AppendLine(String.Format(Inv, "RMS = {0:F6} mm    最大误差 = {1:F6} mm    R² = {2:F9}", fit.RmsError, fit.MaxError, fit.R2))
        Dim theoreticalLength As Double
        If Math.Abs(fit.B) < 0.000000000001 Then
            theoreticalLength = aMm * Math.Abs(fit.ThetaEnd - fit.ThetaStart)
        Else
            theoreticalLength = aMm * Math.Sqrt(1.0 + fit.B * fit.B) * Math.Abs((Math.Exp(fit.B * fit.ThetaEnd) - Math.Exp(fit.B * fit.ThetaStart)) / fit.B)
        End If
        report.AppendLine(String.Format(Inv, "理论解析弧长 = {0:F9} mm", theoreticalLength))
        report.AppendLine(If(Double.IsNaN(nativeCurveLength), "CATIA 实测弧长：待测或接口不支持；不影响参数报告。", String.Format(Inv, "CATIA 实测弧长 = {0:F9} mm", nativeCurveLength)))
        report.AppendLine("a 对应参考 U 轴零角方向；b 和角度符号随 U/V 方向约定。")
        Return report.ToString()
    End Function
End Class

Public Class MainForm
    Inherits Form

    Private selectedCurve As CurveChoice
    Private ReadOnly selectButton As New Button()
    Private ReadOnly fitButton As New Button()
    Private ReadOnly selectedText As New TextBox()
    Private ReadOnly samplesInput As New NumericUpDown()
    Private ReadOnly samplesHint As New Label()
    Private ReadOnly statusText As New TextBox()
    Private ReadOnly progressBar As New ProgressBar()
    Private ReadOnly reportText As New TextBox()
    Private ReadOnly stateLabel As New Label()
    Private ReadOnly elapsedLabel As New Label()
    Private ReadOnly closeButton As New Button()
    Private ReadOnly folderButton As New Button()
    Private ReadOnly clockTimer As New Timer()
    Private ReadOnly progressStepLines As New List(Of String)()
    Private runCancellation As System.Threading.CancellationTokenSource
    Private busy As Boolean
    Private closeWhenFinished As Boolean
    Private startedAt As DateTime
    Private outputPath As String

    Public Sub New()
        Me.Text = "CATIA 对数螺旋线拟合"
        Me.StartPosition = FormStartPosition.CenterScreen
        Me.ClientSize = New Drawing.Size(720, 825)
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.Font = New Drawing.Font("微软雅黑", 9.0F)
        Try
            Me.Icon = Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath)
        Catch
        End Try

        Dim title As New Label() With {
            .Text = "CATIA 对数螺旋线拟合与标准方程曲线生成",
            .Font = New Drawing.Font("微软雅黑", 15.0F, Drawing.FontStyle.Bold),
            .AutoSize = True, .Location = New Drawing.Point(24, 20)
        }
        Dim hint As New Label() With {
            .Text = "1. 在 CATIA 中打开并保存 CATPart   2. 点击下方按钮后亲自选择曲线   3. 开始拟合",
            .AutoSize = True, .ForeColor = Drawing.Color.DimGray, .Location = New Drawing.Point(27, 58)
        }

        Dim selectionGroup As New GroupBox() With {
            .Text = "源曲线", .Location = New Drawing.Point(24, 88), .Size = New Drawing.Size(672, 112)
        }
        selectButton.Text = "从 CATIA 选择曲线…"
        selectButton.Location = New Drawing.Point(18, 28)
        selectButton.Size = New Drawing.Size(185, 36)
        AddHandler selectButton.Click, AddressOf SelectCurveClicked
        selectedText.Location = New Drawing.Point(217, 31)
        selectedText.Size = New Drawing.Size(435, 28)
        selectedText.ReadOnly = True
        selectedText.Text = "尚未选择"
        selectedText.BackColor = Drawing.Color.White
        Dim selectedHint As New Label() With {
            .Text = "可以在 CATIA 几何区或结构树中点选。请选择单条可测量曲线。",
            .AutoSize = True, .ForeColor = Drawing.Color.DimGray, .Location = New Drawing.Point(18, 76)
        }
        selectionGroup.Controls.AddRange(New Control() {selectButton, selectedText, selectedHint})

        Dim settingsGroup As New GroupBox() With {
            .Text = "拟合设置", .Location = New Drawing.Point(24, 213), .Size = New Drawing.Size(672, 75)
        }
        Dim samplesLabel As New Label() With {.Text = "采样间距 (mm)：", .AutoSize = True, .Location = New Drawing.Point(18, 33)}
        samplesInput.Location = New Drawing.Point(124, 29)
        samplesInput.Size = New Drawing.Size(78, 25)
        samplesInput.DecimalPlaces = 2
        samplesInput.Minimum = 0.05D : samplesInput.Maximum = 50.0D : samplesInput.Increment = 0.5D : samplesInput.Value = 1.0D
        AddHandler samplesInput.ValueChanged, Sub(s, ev) UpdateSamplesHint()
        samplesHint.Location = New Drawing.Point(212, 24)
        samplesHint.Size = New Drawing.Size(446, 36)
        samplesHint.TextAlign = Drawing.ContentAlignment.MiddleLeft
        samplesHint.ForeColor = Drawing.Color.DimGray
        UpdateSamplesHint()
        settingsGroup.Controls.AddRange(New Control() {samplesLabel, samplesInput, samplesHint})

        fitButton.Text = "开始拟合并生成标准方程曲线"
        fitButton.Location = New Drawing.Point(24, 305)
        fitButton.Size = New Drawing.Size(330, 44)
        fitButton.BackColor = Drawing.Color.FromArgb(37, 125, 74)
        fitButton.ForeColor = Drawing.Color.White
        fitButton.FlatStyle = FlatStyle.Flat
        AddHandler fitButton.Click, AddressOf FitClicked

        progressBar.Location = New Drawing.Point(370, 316)
        progressBar.Size = New Drawing.Size(326, 22)
        progressBar.Style = ProgressBarStyle.Blocks
        progressBar.Minimum = 0
        progressBar.Maximum = 100
        progressBar.Value = 0

        statusText.Location = New Drawing.Point(24, 365)
        statusText.Size = New Drawing.Size(672, 82)
        statusText.Multiline = True
        statusText.ReadOnly = True
        statusText.ScrollBars = ScrollBars.Vertical
        statusText.BackColor = Drawing.Color.White
        statusText.Text = "就绪。请先选择曲线。"

        stateLabel.SetBounds(24, 456, 672, 32)
        stateLabel.Font = New Drawing.Font("微软雅黑", 12.0F, Drawing.FontStyle.Bold)
        stateLabel.Text = "就绪 · 请先选择曲线"
        elapsedLabel.SetBounds(24, 492, 660, 25)
        elapsedLabel.Text = "完成后会显示绿色状态和结果保存位置。"
        reportText.SetBounds(24, 521, 672, 235)
        reportText.Multiline = True
        reportText.ReadOnly = True
        reportText.ScrollBars = ScrollBars.Vertical
        reportText.BackColor = Drawing.Color.White
        reportText.Text = "参数报告：完成拟合后显示标准方程、a、b、基圆半径/直径、中心坐标和角度制参数。"
        folderButton.SetBounds(24, 772, 180, 38)
        folderButton.Text = "打开结果文件夹"
        folderButton.Enabled = False
        AddHandler folderButton.Click, Sub()
                                           If Not String.IsNullOrEmpty(outputPath) Then
                                               Diagnostics.Process.Start("explorer.exe", "/select,""" & outputPath & """")
                                           End If
                                       End Sub
        closeButton.SetBounds(516, 772, 180, 38)
        closeButton.Text = "关闭程序"
        AddHandler closeButton.Click, Sub() Me.Close()
        AddHandler Me.FormClosing, AddressOf HandleFormClosing
        AddHandler Me.FormClosed, Sub() clockTimer.Dispose()
        clockTimer.Interval = 500
        AddHandler clockTimer.Tick, Sub()
                                       If busy Then elapsedLabel.Text = "已用时 " & CInt((DateTime.Now - startedAt).TotalSeconds).ToString() & " 秒 · " & If(closeWhenFinished, "正在结束当前步骤…", "界面可操作；可取消并关闭。")
                                   End Sub
        Me.Controls.AddRange(New Control() {stateLabel, elapsedLabel, reportText, folderButton, closeButton})

        Me.Controls.AddRange(New Control() {title, hint, selectionGroup, settingsGroup, fitButton, progressBar, statusText})
        Me.AcceptButton = fitButton
    End Sub

    Private Sub UpdateSamplesHint()
        Dim spacingMm As Double = CDbl(samplesInput.Value)
        If selectedCurve Is Nothing Then
            samplesHint.Text = String.Format(CultureInfo.InvariantCulture, "默认按 {0:F2} mm 间距根据曲线长度自适应布置采样点。源文件不会被覆盖。", spacingMm)
        Else
            Dim adaptiveCount As Integer = CatiaRunner.ComputeSampleCount(selectedCurve.LengthMm, spacingMm)
            samplesHint.Text = String.Format(CultureInfo.InvariantCulture, "曲线长 {0:F2} mm，按 {1:F2} mm 间距自适应布置 {2} 个点。源文件不会被覆盖。", selectedCurve.LengthMm, spacingMm, adaptiveCount)
        End If
    End Sub

    Private Shared Function ExtractStepPrefix(ByVal message As String) As String
        If String.IsNullOrEmpty(message) OrElse Not message.StartsWith("[", StringComparison.Ordinal) Then Return ""
        Dim closeIdx As Integer = message.IndexOf("]"c)
        If closeIdx <= 0 Then Return ""
        Return message.Substring(0, closeIdx + 1)
    End Function

    Private Sub SetStatus(ByVal message As String)
        If Me.InvokeRequired Then
            Me.BeginInvoke(New Action(Of String)(AddressOf SetStatus), message)
            Return
        End If
        statusText.Text = message
        statusText.SelectionStart = statusText.TextLength
        statusText.ScrollToCaret()
    End Sub

    Private Sub SetProgress(ByVal percent As Integer, ByVal message As String)
        If Me.InvokeRequired Then
            Me.BeginInvoke(New Action(Of Integer, String)(AddressOf SetProgress), percent, message)
            Return
        End If
        Dim clamped As Integer = Math.Max(0, Math.Min(100, percent))
        progressBar.Style = ProgressBarStyle.Blocks
        progressBar.Value = clamped
        Dim prefix As String = ExtractStepPrefix(message)
        If prefix.Length > 0 AndAlso progressStepLines.Count > 0 AndAlso progressStepLines(progressStepLines.Count - 1).StartsWith(prefix, StringComparison.Ordinal) Then
            progressStepLines(progressStepLines.Count - 1) = message
        Else
            progressStepLines.Add(message)
        End If
        statusText.Text = String.Join(Environment.NewLine, progressStepLines.ToArray())
        statusText.SelectionStart = statusText.TextLength
        statusText.ScrollToCaret()
        If busy AndAlso Not closeWhenFinished Then
            If message.Contains("请在 CATIA") Then
                stateLabel.Text = String.Format(CultureInfo.InvariantCulture, "等待操作 ({0}%) · 请到 CATIA 点击确定", clamped)
                stateLabel.ForeColor = Drawing.Color.DarkOrange
            Else
                stateLabel.Text = String.Format(CultureInfo.InvariantCulture, "正在处理 ({0}%) · {1}", clamped, message)
                stateLabel.ForeColor = Drawing.Color.RoyalBlue
            End If
        End If
    End Sub

    Private Sub ShowParameterReport(ByVal report As String)
        If Me.InvokeRequired Then
            Me.BeginInvoke(New Action(Of String)(AddressOf ShowParameterReport), report)
            Return
        End If
        reportText.Text = report
    End Sub

    Private Sub SelectCurveClicked(ByVal sender As Object, ByVal e As EventArgs)
        Try
            SetStatus("请切换到 CATIA 并点选目标曲线…")
            Me.Hide()
            Application.DoEvents()
            selectedCurve = CatiaRunner.SelectCurveInteractive()
            selectedText.Text = selectedCurve.DocumentName & "  >  " & selectedCurve.CurveName &
                String.Format(CultureInfo.InvariantCulture, "   (长度 {0:F3} mm)", selectedCurve.LengthMm)
            UpdateSamplesHint()
            Dim adaptiveCount As Integer = CatiaRunner.ComputeSampleCount(selectedCurve.LengthMm, CDbl(samplesInput.Value))
            SetStatus(String.Format(CultureInfo.InvariantCulture, "已选择曲线：{0}（长 {1:F2} mm，按 {2:F2} mm 间距将布置 {3} 个点）{4}{5}", selectedCurve.CurveName, selectedCurve.LengthMm, CDbl(samplesInput.Value), adaptiveCount, Environment.NewLine, selectedCurve.DocumentPath))
        Catch ex As OperationCanceledException
            SetStatus(ex.Message)
        Catch ex As Exception
            SetStatus("选择失败：" & ex.Message)
            MessageBox.Show(ex.Message, "选择曲线失败", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Finally
            Me.Show()
            Me.Activate()
        End Try
    End Sub

    Private Sub FitClicked(ByVal sender As Object, ByVal e As EventArgs)
        If busy Then Return
        If selectedCurve Is Nothing Then
            MessageBox.Show("请先点击[从 CATIA 选择曲线]。", "尚未选择曲线", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        selectButton.Enabled = False
        fitButton.Enabled = False
        samplesInput.Enabled = False
        progressBar.Style = ProgressBarStyle.Blocks
        progressBar.Value = 0
        progressStepLines.Clear()
        busy = True
        closeWhenFinished = False
        startedAt = DateTime.Now
        folderButton.Enabled = False
        reportText.Text = "正在计算本次参数报告…"
        closeButton.Text = "取消并关闭"
        fitButton.Text = "正在拟合…"
        runCancellation = New System.Threading.CancellationTokenSource()
        clockTimer.Start()
        SetProgress(2, "[步骤 0/6] 正在连接 CATIA 并准备输出文件…")
        Dim spacingMm As Double = CDbl(samplesInput.Value)
        Dim choice As CurveChoice = selectedCurve
        Dim worker As New System.Threading.Thread(Sub()
            Dim result As String = Nothing
            Dim failure As Exception = Nothing
            Try
                result = CatiaRunner.Run(spacingMm, choice, AddressOf SetProgress, runCancellation.Token, AddressOf ShowParameterReport)
            Catch ex As Exception
                failure = ex
            End Try
            Me.BeginInvoke(New Action(Sub() FinishRun(result, failure)))
        End Sub)
        worker.SetApartmentState(System.Threading.ApartmentState.STA)
        worker.IsBackground = True
        worker.Start()
    End Sub

    Private Sub HandleFormClosing(ByVal sender As Object, ByVal e As FormClosingEventArgs)
        If busy Then
            e.Cancel = True
            closeWhenFinished = True
            runCancellation.Cancel()
            closeButton.Enabled = False
            stateLabel.Text = "正在取消 · 当前 CATIA 调用返回后关闭"
            stateLabel.ForeColor = Drawing.Color.DarkOrange
            statusText.Text = "正在停止任务。若 CATIA 正在执行命令，请等待该命令返回。已创建的文件会保留；CATIA 中尚未确认的命令请按 Esc 取消。"
        End If
    End Sub

    Private Sub FinishRun(ByVal result As String, ByVal failure As Exception)
        busy = False
        clockTimer.Stop()
        progressBar.Style = ProgressBarStyle.Blocks
        selectButton.Enabled = True
        fitButton.Enabled = True
        samplesInput.Enabled = True
        closeButton.Enabled = True
        closeButton.Text = "关闭程序"
        elapsedLabel.Text = "本次用时 " & CInt((DateTime.Now - startedAt).TotalSeconds).ToString() & " 秒"
        If failure Is Nothing Then
            progressBar.Value = 100
            stateLabel.Text = "✓ 已完成 · 曲线与基圆已生成并保存"
            stateLabel.ForeColor = Drawing.Color.ForestGreen
            outputPath = result.Split(New String() {Environment.NewLine}, StringSplitOptions.None)(0)
            reportText.Text = result.Substring(outputPath.Length).TrimStart(ChrW(13), ChrW(10))
            folderButton.Enabled = File.Exists(outputPath)
            fitButton.Text = "再次拟合所选源曲线"
            progressStepLines.Add("输出文件：" & outputPath)
            SetStatus(String.Join(Environment.NewLine, progressStepLines.ToArray()))
            Me.WindowState = FormWindowState.Normal
            Me.Activate()
            System.Media.SystemSounds.Asterisk.Play()
        Else
            If reportText.Text = "正在计算本次参数报告…" Then reportText.Text = "本次数学拟合尚未完成，暂无参数报告。"
            progressBar.Value = 0
            stateLabel.Text = If(TypeOf failure Is OperationCanceledException, "已取消", "失败 · 请查看下方原因")
            stateLabel.ForeColor = Drawing.Color.Firebrick
            fitButton.Text = "重试拟合"
            SetStatus(failure.Message)
        End If
        runCancellation.Dispose()
        If closeWhenFinished Then Me.Close()
    End Sub
End Class

Public Module Program
    <STAThread>
    Public Function Main(ByVal args() As String) As Integer
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.Run(New MainForm())
        Return 0
    End Function
End Module
