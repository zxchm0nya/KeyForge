using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace KyeForge.App.Views;

/// <summary>
/// Isometric-ish 3D preview of the keyboard layout built from the same key
/// geometry as the 2D test board. Drag to orbit, wheel to zoom. Pressed and
/// tested keys are highlighted live.
/// </summary>
public partial class Layout3DView : UserControl
{
    public readonly record struct Key3D(string Label, int Usage, double X, double Y, double W, double H);

    private const double KeyHeight = 16;
    private const double Unit = 48;

    private static readonly Color TopIdle = Color.FromRgb(0x16, 0x1F, 0x27);
    private static readonly Color TopTested = Color.FromRgb(0x20, 0x30, 0x3A);
    private static readonly Color TopPressed = Color.FromRgb(0x28, 0xD7, 0xB7);
    private static readonly Color SideIdle = Color.FromRgb(0x0E, 0x14, 0x1A);

    private sealed class KeyEntry
    {
        public int Usage;
        public DiffuseMaterial Top = null!;
        public Model3D Model = null!;
    }

    private readonly List<KeyEntry> _entries = new();
    private readonly Model3DGroup _scene = new();
    private readonly PerspectiveCamera _camera = new();
    private readonly HashSet<int> _pressed = new();
    private readonly HashSet<int> _tested = new();

    private Point3D _center = new(0, 0, 0);
    private double _dist = 900;
    private double _yaw = -35;
    private double _pitch = 42;
    private Point? _dragFrom;
    private double _yawFrom, _pitchFrom;

    public Layout3DView()
    {
        InitializeComponent();
        var visual = new ModelVisual3D { Content = _scene };
        Viewport.Camera = _camera;
        Viewport.Children.Add(visual);
        UpdateCamera();
        Loaded += (_, _) => UpdateCamera();
        SizeChanged += (_, _) => UpdateCamera();
    }

    public void SetBoard(IReadOnlyList<Key3D> keys)
    {
        _entries.Clear();
        _scene.Children.Clear();

        if (keys.Count == 0) return;

        double maxX = 0, maxZ = 0;
        foreach (var k in keys)
        {
            maxX = Math.Max(maxX, (k.X + k.W) * Unit);
            maxZ = Math.Max(maxZ, (k.Y + k.H) * Unit);
            var entry = BuildKey(k);
            _entries.Add(entry);
            _scene.Children.Add(entry.Model);
        }

        _center = new Point3D(maxX / 2, 0, maxZ / 2);
        _dist = Math.Max(700, Math.Max(maxX, maxZ) * 1.35);
        UpdateCamera();
        ApplyHighlight();
    }

    public void SetHighlight(IReadOnlyCollection<int> pressed, IReadOnlyCollection<int> tested)
    {
        _pressed.Clear();
        _tested.Clear();
        foreach (var u in pressed) _pressed.Add(u);
        foreach (var u in tested) _tested.Add(u);
        ApplyHighlight();
    }

    private void ApplyHighlight()
    {
        foreach (var e in _entries)
        {
            var color = _pressed.Contains(e.Usage) ? TopPressed
                : e.Usage > 0 && _tested.Contains(e.Usage) ? TopTested
                : TopIdle;
            e.Top.Brush = new SolidColorBrush(color);
        }
    }

    private KeyEntry BuildKey(Key3D k)
    {
        double x = k.X * Unit + 3;
        double z = k.Y * Unit + 3;
        double w = Math.Max(8, k.W * Unit - 6);
        double d = Math.Max(8, k.H * Unit - 6);

        // Top face (y = KeyHeight) and four sides; bottom is never visible.
        var topPts = new[]
        {
            new Point3D(x, KeyHeight, z),
            new Point3D(x + w, KeyHeight, z),
            new Point3D(x + w, KeyHeight, z + d),
            new Point3D(x, KeyHeight, z + d)
        };
        var topMesh = QuadMesh(topPts, new Point(0, 0), new Point(w, 0), new Point(w, d), new Point(0, d));
        var topMat = new DiffuseMaterial(new SolidColorBrush(TopIdle));

        var sideMesh = new MeshGeometry3D();
        AddQuad(sideMesh, topPts[0], topPts[1], new Point3D(x + w, 0, z), new Point3D(x, 0, z));
        AddQuad(sideMesh, topPts[1], topPts[2], new Point3D(x + w, 0, z + d), new Point3D(x + w, 0, z));
        AddQuad(sideMesh, topPts[2], topPts[3], new Point3D(x, 0, z + d), new Point3D(x + w, 0, z + d));
        AddQuad(sideMesh, topPts[3], topPts[0], new Point3D(x, 0, z), new Point3D(x, 0, z + d));
        sideMesh.Freeze();

        var sideMat = new DiffuseMaterial(new SolidColorBrush(SideIdle));
        var group = new Model3DGroup();
        group.Children.Add(new GeometryModel3D(topMesh, topMat));
        group.Children.Add(new GeometryModel3D(sideMesh, sideMat));

        return new KeyEntry { Usage = k.Usage, Top = topMat, Model = group };
    }

    private static MeshGeometry3D QuadMesh(Point3D[] p, params Point[] uv)
    {
        var mesh = new MeshGeometry3D();
        for (int i = 0; i < 4; i++)
        {
            mesh.Positions.Add(p[i]);
            mesh.TextureCoordinates.Add(uv[i]);
        }
        mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(1); mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(2); mesh.TriangleIndices.Add(3);
        mesh.Freeze();
        return mesh;
    }

    private static void AddQuad(MeshGeometry3D m, Point3D a, Point3D b, Point3D c, Point3D d)
    {
        int baseIdx = m.Positions.Count;
        m.Positions.Add(a); m.Positions.Add(b); m.Positions.Add(c); m.Positions.Add(d);
        m.TriangleIndices.Add(baseIdx); m.TriangleIndices.Add(baseIdx + 1); m.TriangleIndices.Add(baseIdx + 2);
        m.TriangleIndices.Add(baseIdx); m.TriangleIndices.Add(baseIdx + 2); m.TriangleIndices.Add(baseIdx + 3);
    }

    private void UpdateCamera()
    {
        double yawRad = _yaw * Math.PI / 180.0;
        double pitchRad = _pitch * Math.PI / 180.0;

        var offset = new Vector3D(
            Math.Sin(yawRad) * Math.Cos(pitchRad) * _dist,
            Math.Sin(pitchRad) * _dist,
            Math.Cos(yawRad) * Math.Cos(pitchRad) * _dist);

        var pos = _center + offset;
        _camera.Position = pos;
        _camera.LookDirection = _center - pos;
        _camera.UpDirection = new Vector3D(0, 1, 0);
        _camera.FieldOfView = 42;
        _camera.NearPlaneDistance = 1;
        _camera.FarPlaneDistance = Math.Max(5000, _dist * 4);
    }

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragFrom = e.GetPosition(Viewport);
        _yawFrom = _yaw;
        _pitchFrom = _pitch;
        Viewport.CaptureMouse();
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragFrom is not { } from) return;
        var p = e.GetPosition(Viewport);
        _yaw = _yawFrom - (p.X - from.X) * 0.4;
        _pitch = Math.Clamp(_pitchFrom + (p.Y - from.Y) * 0.3, 8, 85);
        UpdateCamera();
    }

    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragFrom = null;
        Viewport.ReleaseMouseCapture();
    }

    private void Viewport_Wheel(object sender, MouseWheelEventArgs e)
    {
        _dist = Math.Clamp(_dist - e.Delta * 0.6, 220, 6000);
        UpdateCamera();
    }
}
