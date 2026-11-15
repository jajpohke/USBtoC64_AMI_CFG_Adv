using System.Drawing.Drawing2D;
using UsbToC64.Core.Emulation;

namespace UsbToC64.Gui;

/// <summary>Shows what the C64/Amiga would see on the DB9 port: four directions, three fire buttons, autofire.</summary>
internal sealed class C64Panel : Control
{
    private C64State? _state;
    private bool _live;

    private static readonly Color Off = Color.FromArgb(225, 228, 232);
    private static readonly Color OffBorder = Color.FromArgb(160, 166, 175);
    private static readonly Color DirOn = Color.FromArgb(33, 150, 243);
    private static readonly Color Fire1On = Color.FromArgb(46, 175, 80);
    private static readonly Color Fire2On = Color.FromArgb(229, 57, 53);
    private static readonly Color Fire3On = Color.FromArgb(0, 172, 193);
    private static readonly Color AutoOn = Color.FromArgb(255, 152, 0);

    public C64Panel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        MinimumSize = new Size(220, 300);
    }

    /// <summary>False when no pad is being read: the panel is drawn greyed out.</summary>
    public bool Live
    {
        get => _live;
        set { if (_live != value) { _live = value; Invalidate(); } }
    }

    public C64State? State
    {
        get => _state;
        set { if (!Nullable.Equals(_state, value)) { _state = value; Invalidate(); } }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        var s = _live ? _state ?? default : default;

        using var title = new Font(Font.FontFamily, Font.Size + 1, FontStyle.Bold);
        g.DrawString(_live ? "Uscita verso il C64" : "Uscita verso il C64 (nessun pad)", title,
            _live ? SystemBrushes.ControlText : SystemBrushes.GrayText, 8, 6);

        // --- directions: a cross of four arrow keys
        int cell = Math.Clamp(Math.Min(Width / 4, (Height - 120) / 3), 32, 64);
        int cx = Width / 2, top = 40;
        var up = new Rectangle(cx - cell / 2, top, cell, cell);
        var left = new Rectangle(cx - cell * 3 / 2, top + cell, cell, cell);
        var right = new Rectangle(cx + cell / 2, top + cell, cell, cell);
        var down = new Rectangle(cx - cell / 2, top + cell * 2, cell, cell);
        var center = new Rectangle(cx - cell / 2, top + cell, cell, cell);

        DrawKey(g, up, "▲", s.Up, DirOn);
        DrawKey(g, left, "◀", s.Left, DirOn);
        DrawKey(g, right, "▶", s.Right, DirOn);
        DrawKey(g, down, "▼", s.Down, DirOn);
        using (var b = new SolidBrush(Off)) g.FillRectangle(b, center);

        // --- fire buttons and autofire
        int y = top + cell * 3 + 18;
        int d = Math.Clamp(cell - 6, 30, 52);
        int gap = d / 3;
        int x0 = cx - (d * 3 + gap * 2) / 2;
        DrawFire(g, new Rectangle(x0, y, d, d), "F1", s.Fire1, Fire1On);
        DrawFire(g, new Rectangle(x0 + d + gap, y, d, d), "F2", s.Fire2, Fire2On);
        DrawFire(g, new Rectangle(x0 + (d + gap) * 2, y, d, d), "F3", s.Fire3, Fire3On);

        var auto = new Rectangle(cx - (d * 3 + gap * 2) / 2, y + d + 14, d * 3 + gap * 2, 26);
        DrawKey(g, auto, s.Autofire ? "AUTOFIRE ATTIVO" : "AUTOFIRE", s.Autofire, AutoOn);
    }

    private void DrawKey(Graphics g, Rectangle r, string text, bool on, Color onColor)
    {
        using var path = Rounded(r, 6);
        using var fill = new SolidBrush(on ? onColor : Off);
        using var pen = new Pen(on ? ControlPaint.Dark(onColor) : OffBorder);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
        TextRenderer.DrawText(g, text, Font, r, on ? Color.White : Color.DimGray,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawFire(Graphics g, Rectangle r, string text, bool on, Color onColor)
    {
        using var fill = new SolidBrush(on ? onColor : Off);
        using var pen = new Pen(on ? ControlPaint.Dark(onColor) : OffBorder, 1.5f);
        g.FillEllipse(fill, r);
        g.DrawEllipse(pen, r);
        using var bold = new Font(Font, FontStyle.Bold);
        TextRenderer.DrawText(g, text, bold, r, on ? Color.White : Color.DimGray,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
