sealed class CarouselDot : Control
{
    bool selected;
    bool hovered;

    public bool Selected
    {
        get => selected;
        set
        {
            if (selected == value)
                return;
            selected = value;
            Invalidate();
        }
    }

    public CarouselDot()
    {
        Cursor = Cursors.Hand;
        TabStop = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = UiTheme.Background;
        UpdateHitRegion();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateHitRegion();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hovered = false;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var diameter = hovered ? 10 : 5;
        var bounds = new Rectangle((Width - diameter) / 2, (Height - diameter) / 2, diameter, diameter);
        using var brush = new SolidBrush(selected ? UiTheme.Accent : hovered ? UiTheme.TextSoft : UiTheme.Muted);
        e.Graphics.FillEllipse(brush, bounds);
    }

    void UpdateHitRegion()
    {
        if (Width < 2 || Height < 2)
            return;

        var old = Region;
        Region = new Region(ClientRectangle);
        old?.Dispose();
    }
}
