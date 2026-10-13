using System.Globalization;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using GlobalDeformationLab.Core;
using GlobalDeformationLab.IO;
using GlobalDeformationLab.Rendering;
using HelixToolkit.Wpf;
using Microsoft.Win32;

namespace GlobalDeformationLab;

public partial class MainWindow : Window
{
    private readonly Stack<AppSnapshot> _undo = new();
    private readonly Stack<AppSnapshot> _redo = new();
    private readonly List<ModelVisual3D> _controlPointVisuals = [];
    private readonly Dictionary<GeometryModel3D, int> _controlPointModels = [];
    private LinesVisual3D? _latticeLines;
    private MeshData? _mesh;
    private Vector3[] _originalPositions = [];
    private Vector3[] _committedPositions = [];
    private Vector3[] _displayedPositions = [];
    private FfdLattice? _ffd;
    private DeformationMode _mode = DeformationMode.Taper;
    private int _selectedControlPoint;
    private bool _initialized;
    private bool _suppressControlEvents;

    public MainWindow()
    {
        InitializeComponent();
        _initialized = true;
        Loaded += MainWindow_Loaded;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        UpdateHistoryButtons();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var samplePath = ExtractBundledSample();
        if (samplePath is not null)
        {
            LoadMesh(samplePath, "示例模型");
        }
        else
        {
            SetStatus("请选择 OBJ 模型");
        }
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入 OBJ 模型",
            Filter = "Wavefront OBJ (*.obj)|*.obj",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            LoadMesh(dialog.FileName, "已导入");
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mesh is null)
        {
            ShowNoModelMessage();
            return;
        }

        var defaultName = Path.GetFileNameWithoutExtension(_mesh.Name) + "_deformed.obj";
        var dialog = new SaveFileDialog
        {
            Title = "导出变形后的 OBJ 模型",
            Filter = "Wavefront OBJ (*.obj)|*.obj",
            FileName = defaultName,
            AddExtension = true,
            DefaultExt = ".obj"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ObjMeshService.Save(dialog.FileName, _mesh, _displayedPositions);
            SetStatus($"已导出 {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ModeButton_Checked(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _suppressControlEvents || sender is not RadioButton { Tag: string tag } || !Enum.TryParse(tag, out DeformationMode mode))
        {
            return;
        }

        if (_mode == DeformationMode.Ffd && mode != DeformationMode.Ffd)
        {
            ClearLatticeVisuals();
            _ffd = null;
        }

        _mode = mode;
        ConfigureModeControls();

        if (_mode == DeformationMode.Ffd)
        {
            if (_mesh is not null)
            {
                EnsureFfd(GetSelectedGridSize());
            }
        }
        else
        {
            PreviewCurrentDeformation();
        }
    }

    private void ParameterSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized || _suppressControlEvents)
        {
            return;
        }

        UpdateParameterValueText();
        PreviewCurrentDeformation();
    }

    private void ApplyDeformationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mesh is null)
        {
            ShowNoModelMessage();
            return;
        }

        PushUndo();
        _committedPositions = _displayedPositions.ToArray();
        _ffd = null;
        SetSliderValue(0);
        RenderMesh(_committedPositions);
        SetStatus($"已应用{ModeDisplayName(_mode)}");
    }

    private void RebuildFfdButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mesh is null)
        {
            ShowNoModelMessage();
            return;
        }

        if (_ffd is not null)
        {
            PushUndo();
        }

        _ffd = FfdLattice.Create(_committedPositions, GetSelectedGridSize());
        _selectedControlPoint = 0;
        PopulateControlPointList();
        UpdateLatticeVisuals();
        Viewport.ZoomExtents(250);
        SetStatus($"已建立 {_ffd.GridSize}×{_ffd.GridSize}×{_ffd.GridSize} FFD 晶格");
    }

    private void GridSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _suppressControlEvents || _mode != DeformationMode.Ffd || _mesh is null)
        {
            return;
        }

        if (_ffd is not null)
        {
            PushUndo();
        }

        _ffd = FfdLattice.Create(_committedPositions, GetSelectedGridSize());
        _selectedControlPoint = 0;
        PopulateControlPointList();
        UpdateLatticeVisuals();
        Viewport.ZoomExtents(250);
        SetStatus($"FFD {_ffd.GridSize}×{_ffd.GridSize}×{_ffd.GridSize}");
    }

    private void ControlPointCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressControlEvents || _ffd is null || ControlPointCombo.SelectedIndex < 0)
        {
            return;
        }

        _selectedControlPoint = ControlPointCombo.SelectedIndex;
        UpdateControlPointFields();
        UpdateLatticeVisuals();
    }

    private void UpdateControlPointButton_Click(object sender, RoutedEventArgs e)
    {
        if (_ffd is null)
        {
            return;
        }

        if (!TryReadCoordinate(ControlXText.Text, out var x) ||
            !TryReadCoordinate(ControlYText.Text, out var y) ||
            !TryReadCoordinate(ControlZText.Text, out var z))
        {
            MessageBox.Show(this, "请输入有效的 X、Y、Z 数值。", "坐标格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        PushUndo();
        _ffd.ControlPoints[_selectedControlPoint] = new Vector3(x, y, z);
        ApplyFfdAndRefresh("已更新 FFD 控制点");
    }

    private void NudgeControlPoint_Click(object sender, RoutedEventArgs e)
    {
        if (_ffd is null || sender is not Button { Tag: string direction })
        {
            return;
        }

        var extent = _ffd.Bounds.Size;
        var step = MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z)) * 0.025f;
        var delta = direction switch
        {
            "X-" => new Vector3(-step, 0, 0),
            "X+" => new Vector3(step, 0, 0),
            "Y-" => new Vector3(0, -step, 0),
            "Y+" => new Vector3(0, step, 0),
            "Z-" => new Vector3(0, 0, -step),
            "Z+" => new Vector3(0, 0, step),
            _ => Vector3.Zero
        };

        if (delta == Vector3.Zero)
        {
            return;
        }

        PushUndo();
        _ffd.ControlPoints[_selectedControlPoint] += delta;
        ApplyFfdAndRefresh("已微调 FFD 控制点");
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e) => Undo();

    private void RedoButton_Click(object sender, RoutedEventArgs e) => Redo();

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mesh is null)
        {
            return;
        }

        PushUndo();
        _committedPositions = _originalPositions.ToArray();
        _displayedPositions = _committedPositions.ToArray();
        _ffd = null;
        ClearLatticeVisuals();
        SetSliderValue(0);
        RenderMesh(_committedPositions);
        SetStatus("已恢复导入状态");
    }

    private void ShowLatticeCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _ffd is null)
        {
            return;
        }

        if (ShowLatticeCheck.IsChecked == true)
        {
            UpdateLatticeVisuals();
        }
        else
        {
            ClearLatticeVisuals();
        }
    }

    private void OrthographicCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
        {
            return;
        }

        Viewport.Orthographic = OrthographicCheck.IsChecked == true;
        ProjectionStatusText.Text = Viewport.Orthographic ? "正交视图" : "透视视图";
    }

    private void FitViewButton_Click(object sender, RoutedEventArgs e)
    {
        Viewport.ZoomExtents(250);
        SetStatus("已适配全部模型");
    }

    private void PresetViewButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string view })
        {
            ApplyPresetView(view);
        }
    }

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_ffd is null || ShowLatticeCheck.IsChecked != true || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var hits = Viewport3DHelper.FindHits(Viewport.Viewport, e.GetPosition(Viewport));
        foreach (var hit in hits)
        {
            if (hit.Model is GeometryModel3D model && _controlPointModels.TryGetValue(model, out var index))
            {
                _selectedControlPoint = index;
                _suppressControlEvents = true;
                ControlPointCombo.SelectedIndex = index;
                _suppressControlEvents = false;
                UpdateControlPointFields();
                UpdateLatticeVisuals();
                SetStatus(ControlPointLabel(index));
                e.Handled = true;
                break;
            }
        }
    }

    private void Viewport_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        Viewport.ZoomExtents(250);
        SetStatus("已适配全部模型");
        e.Handled = true;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            switch (e.Key)
            {
                case Key.Z:
                    Undo();
                    e.Handled = true;
                    break;
                case Key.Y:
                    Redo();
                    e.Handled = true;
                    break;
                case Key.O:
                    ImportButton_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.S:
                    ExportButton_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.F:
                FitViewButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.D1:
            case Key.NumPad1:
                ApplyPresetView("Front");
                e.Handled = true;
                break;
            case Key.D2:
            case Key.NumPad2:
                ApplyPresetView("Top");
                e.Handled = true;
                break;
            case Key.D3:
            case Key.NumPad3:
                ApplyPresetView("Right");
                e.Handled = true;
                break;
        }
    }

    private void ApplyPresetView(string view)
    {
        if (_displayedPositions.Length == 0)
        {
            return;
        }

        var bounds = Bounds3.FromPoints(_displayedPositions);
        var center = bounds.Center;
        var extent = MathF.Max(bounds.Size.X, MathF.Max(bounds.Size.Y, bounds.Size.Z));
        var distance = Math.Max(6.0, extent * 2.5);
        var target = new Point3D(center.X, center.Y, center.Z);

        switch (view)
        {
            case "Front":
                SetPresetView(target, new Vector3D(0, 0, distance), new Vector3D(0, 1, 0));
                SetStatus("前视图");
                break;
            case "Top":
                SetPresetView(target, new Vector3D(0, distance, 0), new Vector3D(0, 0, -1));
                SetStatus("顶视图");
                break;
            case "Right":
                SetPresetView(target, new Vector3D(distance, 0, 0), new Vector3D(0, 1, 0));
                SetStatus("右视图");
                break;
        }
    }

    private void SetPresetView(Point3D target, Vector3D cameraOffset, Vector3D upDirection)
    {
        var position = target + cameraOffset;
        var lookDirection = target - position;
        Viewport.SetView(position, lookDirection, upDirection, 250);
        Viewport.ZoomExtents(250);
    }

    private void LoadMesh(string filePath, string statusPrefix)
    {
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            _mesh = ObjMeshService.Load(filePath);
            _originalPositions = _mesh.Positions.ToArray();
            _committedPositions = _mesh.Positions.ToArray();
            _displayedPositions = _mesh.Positions.ToArray();
            _ffd = null;
            _undo.Clear();
            _redo.Clear();
            ClearLatticeVisuals();
            SetSliderValue(DefaultValueFor(_mode));
            PreviewCurrentDeformation();
            ModelInfoText.Text = $"{_mesh.Name}  ·  {_mesh.Positions.Length:N0} 顶点  ·  {_mesh.TriangleIndices.Length / 3:N0} 三角面";
            Viewport.ZoomExtents(350);
            SetStatus($"{statusPrefix} · {_mesh.Name}");
            UpdateHistoryButtons();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "OBJ 导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("导入失败");
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void ConfigureModeControls()
    {
        var isFfd = _mode == DeformationMode.Ffd;
        ParamPanel.Visibility = isFfd ? Visibility.Collapsed : Visibility.Visible;
        FfdPanel.Visibility = isFfd ? Visibility.Visible : Visibility.Collapsed;
        ModeHintText.Text = ModeDisplayName(_mode);

        if (isFfd)
        {
            return;
        }

        _suppressControlEvents = true;
        switch (_mode)
        {
            case DeformationMode.Taper:
                ParameterNameText.Text = "锥化强度";
                ParameterSlider.Minimum = -0.9;
                ParameterSlider.Maximum = 0.9;
                ParameterSlider.TickFrequency = 0.05;
                ParameterSlider.Value = 0.55;
                break;
            case DeformationMode.Twist:
                ParameterNameText.Text = "扭转角度";
                ParameterSlider.Minimum = -360;
                ParameterSlider.Maximum = 360;
                ParameterSlider.TickFrequency = 5;
                ParameterSlider.Value = 180;
                break;
            case DeformationMode.Bend:
                ParameterNameText.Text = "弯曲角度";
                ParameterSlider.Minimum = -170;
                ParameterSlider.Maximum = 170;
                ParameterSlider.TickFrequency = 5;
                ParameterSlider.Value = 75;
                break;
            case DeformationMode.Squeeze:
                ParameterNameText.Text = "Y 轴缩放变化";
                ParameterSlider.Minimum = -0.7;
                ParameterSlider.Maximum = 1.5;
                ParameterSlider.TickFrequency = 0.05;
                ParameterSlider.Value = 0.5;
                break;
        }

        _suppressControlEvents = false;
        UpdateParameterValueText();
    }

    private void PreviewCurrentDeformation()
    {
        if (_mesh is null || _mode == DeformationMode.Ffd)
        {
            return;
        }

        var value = (float)ParameterSlider.Value;
        _displayedPositions = _mode switch
        {
            DeformationMode.Taper => DeformationMath.Taper(_committedPositions, value),
            DeformationMode.Twist => DeformationMath.Twist(_committedPositions, value),
            DeformationMode.Bend => DeformationMath.Bend(_committedPositions, value),
            DeformationMode.Squeeze => DeformationMath.Squeeze(_committedPositions, value),
            _ => _committedPositions.ToArray()
        };
        RenderMesh(_displayedPositions);
        SetStatus($"{ModeDisplayName(_mode)}预览");
    }

    private void RenderMesh(IReadOnlyList<Vector3> positions)
    {
        if (_mesh is null)
        {
            return;
        }

        MeshVisual.Content = MeshRenderFactory.CreateModel(_mesh, positions);
    }

    private void EnsureFfd(int gridSize)
    {
        if (_mesh is null)
        {
            return;
        }

        _displayedPositions = _committedPositions.ToArray();
        RenderMesh(_displayedPositions);
        _ffd = FfdLattice.Create(_committedPositions, gridSize);
        _selectedControlPoint = 0;
        PopulateControlPointList();
        UpdateLatticeVisuals();
        Viewport.ZoomExtents(250);
        SetStatus($"FFD {_ffd.GridSize}×{_ffd.GridSize}×{_ffd.GridSize}");
    }

    private void ApplyFfdAndRefresh(string status)
    {
        if (_ffd is null)
        {
            return;
        }

        _displayedPositions = _ffd.Evaluate();
        _committedPositions = _displayedPositions.ToArray();
        RenderMesh(_displayedPositions);
        UpdateControlPointFields();
        UpdateLatticeVisuals();
        SetStatus(status);
    }

    private void PopulateControlPointList()
    {
        if (_ffd is null)
        {
            return;
        }

        _suppressControlEvents = true;
        ControlPointCombo.Items.Clear();
        for (var i = 0; i < _ffd.ControlPoints.Length; i++)
        {
            ControlPointCombo.Items.Add(ControlPointLabel(i));
        }

        ControlPointCombo.SelectedIndex = Math.Clamp(_selectedControlPoint, 0, _ffd.ControlPoints.Length - 1);
        _suppressControlEvents = false;
        UpdateControlPointFields();
    }

    private void UpdateControlPointFields()
    {
        if (_ffd is null || _selectedControlPoint >= _ffd.ControlPoints.Length)
        {
            return;
        }

        var point = _ffd.ControlPoints[_selectedControlPoint];
        ControlXText.Text = point.X.ToString("0.###", CultureInfo.InvariantCulture);
        ControlYText.Text = point.Y.ToString("0.###", CultureInfo.InvariantCulture);
        ControlZText.Text = point.Z.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private void UpdateLatticeVisuals()
    {
        ClearLatticeVisuals();
        if (_ffd is null || ShowLatticeCheck.IsChecked != true)
        {
            return;
        }

        var points = new Point3DCollection();
        var n = _ffd.GridSize;
        for (var x = 0; x < n; x++)
        {
            for (var y = 0; y < n; y++)
            {
                for (var z = 0; z < n; z++)
                {
                    if (x + 1 < n) AddLatticeEdge(points, x, y, z, x + 1, y, z);
                    if (y + 1 < n) AddLatticeEdge(points, x, y, z, x, y + 1, z);
                    if (z + 1 < n) AddLatticeEdge(points, x, y, z, x, y, z + 1);
                }
            }
        }

        _latticeLines = new LinesVisual3D
        {
            Points = points,
            Color = Color.FromRgb(92, 176, 139),
            Thickness = 1.2
        };
        Viewport.Children.Add(_latticeLines);

        var boundsSize = _ffd.Bounds.Size;
        var radius = Math.Max(0.035, Math.Max(boundsSize.X, Math.Max(boundsSize.Y, boundsSize.Z)) * 0.014);
        for (var i = 0; i < _ffd.ControlPoints.Length; i++)
        {
            var selected = i == _selectedControlPoint;
            var color = selected ? Color.FromRgb(235, 143, 67) : Color.FromRgb(76, 188, 138);
            var material = new DiffuseMaterial(new SolidColorBrush(color));
            var model = new GeometryModel3D
            {
                Geometry = MeshRenderFactory.CreateSphere(_ffd.ControlPoints[i], selected ? radius * 1.45 : radius),
                Material = material,
                BackMaterial = material
            };
            var visual = new ModelVisual3D { Content = model };
            _controlPointModels[model] = i;
            _controlPointVisuals.Add(visual);
            Viewport.Children.Add(visual);
        }
    }

    private void AddLatticeEdge(Point3DCollection points, int ax, int ay, int az, int bx, int by, int bz)
    {
        if (_ffd is null)
        {
            return;
        }

        var a = _ffd.ControlPoints[FfdLattice.IndexOf(_ffd.GridSize, ax, ay, az)];
        var b = _ffd.ControlPoints[FfdLattice.IndexOf(_ffd.GridSize, bx, by, bz)];
        points.Add(new Point3D(a.X, a.Y, a.Z));
        points.Add(new Point3D(b.X, b.Y, b.Z));
    }

    private void ClearLatticeVisuals()
    {
        if (_latticeLines is not null)
        {
            Viewport.Children.Remove(_latticeLines);
            _latticeLines = null;
        }

        foreach (var visual in _controlPointVisuals)
        {
            Viewport.Children.Remove(visual);
        }

        _controlPointVisuals.Clear();
        _controlPointModels.Clear();
    }

    private void Undo()
    {
        if (_undo.Count == 0 || _mesh is null)
        {
            return;
        }

        _redo.Push(CaptureSnapshot());
        RestoreSnapshot(_undo.Pop());
        SetStatus("已撤销");
        UpdateHistoryButtons();
    }

    private void Redo()
    {
        if (_redo.Count == 0 || _mesh is null)
        {
            return;
        }

        _undo.Push(CaptureSnapshot());
        RestoreSnapshot(_redo.Pop());
        SetStatus("已重做");
        UpdateHistoryButtons();
    }

    private void PushUndo()
    {
        if (_mesh is null)
        {
            return;
        }

        _undo.Push(CaptureSnapshot());
        _redo.Clear();
        UpdateHistoryButtons();
    }

    private AppSnapshot CaptureSnapshot()
    {
        return new AppSnapshot(_committedPositions.ToArray(), _ffd?.Clone(), _mode);
    }

    private void RestoreSnapshot(AppSnapshot snapshot)
    {
        _committedPositions = snapshot.Positions.ToArray();
        _displayedPositions = snapshot.Positions.ToArray();
        _ffd = snapshot.Lattice?.Clone();
        _mode = snapshot.Mode;
        SelectModeButton(_mode);
        RenderMesh(_displayedPositions);

        if (_ffd is not null)
        {
            _selectedControlPoint = Math.Clamp(_selectedControlPoint, 0, _ffd.ControlPoints.Length - 1);
            PopulateControlPointList();
            UpdateLatticeVisuals();
        }
        else
        {
            ClearLatticeVisuals();
        }
    }

    private void SelectModeButton(DeformationMode mode)
    {
        _suppressControlEvents = true;
        var button = mode switch
        {
            DeformationMode.Taper => TaperModeButton,
            DeformationMode.Twist => TwistModeButton,
            DeformationMode.Bend => BendModeButton,
            DeformationMode.Squeeze => SqueezeModeButton,
            DeformationMode.Ffd => FfdModeButton,
            _ => TaperModeButton
        };
        button.IsChecked = true;
        _suppressControlEvents = false;
        ConfigureModeControls();
    }

    private void UpdateHistoryButtons()
    {
        UndoButton.IsEnabled = _undo.Count > 0;
        RedoButton.IsEnabled = _redo.Count > 0;
    }

    private void UpdateParameterValueText()
    {
        ParameterValueText.Text = _mode is DeformationMode.Twist or DeformationMode.Bend
            ? $"{ParameterSlider.Value:0}°"
            : $"{ParameterSlider.Value:0.00}";
    }

    private void SetSliderValue(double value)
    {
        _suppressControlEvents = true;
        ParameterSlider.Value = Math.Clamp(value, ParameterSlider.Minimum, ParameterSlider.Maximum);
        _suppressControlEvents = false;
        UpdateParameterValueText();
    }

    private int GetSelectedGridSize()
    {
        return GridSizeCombo.SelectedItem is ComboBoxItem { Content: string value } && int.TryParse(value, out var parsed)
            ? parsed
            : 3;
    }

    private string ControlPointLabel(int index)
    {
        if (_ffd is null)
        {
            return string.Empty;
        }

        var (x, y, z) = _ffd.CoordinatesOf(index);
        return $"P[{x}, {y}, {z}]";
    }

    private static bool TryReadCoordinate(string text, out float value)
    {
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
               float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private static double DefaultValueFor(DeformationMode mode)
    {
        return mode switch
        {
            DeformationMode.Taper => 0.55,
            DeformationMode.Twist => 180,
            DeformationMode.Bend => 75,
            DeformationMode.Squeeze => 0.5,
            _ => 0
        };
    }

    private static string ModeDisplayName(DeformationMode mode)
    {
        return mode switch
        {
            DeformationMode.Taper => "锥化",
            DeformationMode.Twist => "扭曲",
            DeformationMode.Bend => "弯曲",
            DeformationMode.Squeeze => "挤压",
            DeformationMode.Ffd => "FFD",
            _ => string.Empty
        };
    }

    private void SetStatus(string text) => ViewportStatusText.Text = text;

    private void ShowNoModelMessage()
    {
        MessageBox.Show(this, "请先导入一个 OBJ 模型。", "尚未加载模型", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string? ExtractBundledSample()
    {
        const string resourceName = "GlobalDeformationLab.Assets.F-16.obj";
        var assembly = Assembly.GetExecutingAssembly();
        using var source = assembly.GetManifestResourceStream(resourceName);
        if (source is null)
        {
            return null;
        }

        var directory = Path.Combine(Path.GetTempPath(), "GlobalDeformationLab");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "F-16.obj");
        using var destination = File.Create(target);
        source.CopyTo(destination);
        return target;
    }

    private enum DeformationMode
    {
        Taper,
        Twist,
        Bend,
        Squeeze,
        Ffd
    }

    private sealed record AppSnapshot(Vector3[] Positions, FfdLattice? Lattice, DeformationMode Mode);
}
