namespace KyeForge.App.Models;

/// <summary>A visual key position parsed from a VIA layout entry.</summary>
public class LayoutKey
{
    public int Row { get; set; }
    public int Col { get; set; }
    public int MatrixRow { get; set; }
    public int MatrixCol { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; } = 1;
    public double H { get; set; } = 1;
    public double Rx { get; set; } = 0;
    public double Ry { get; set; } = 0;
    public bool IsEncoder { get; set; }
    public bool IsStab { get; set; }
    public string? Legend { get; set; }
    public ushort Keycode { get; set; }
}

/// <summary>Rendered keyboard layout (grid of key sizes in "unit" coordinates).</summary>
public class KeyboardLayout
{
    public List<LayoutKey> Keys { get; } = new();
    public int MaxX { get; set; }
    public int MaxY { get; set; }
    public List<string?> MatrixLabels { get; } = new();
}