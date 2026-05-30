using System.Drawing.Drawing2D;

namespace PrintForm
{
    internal static class UiTheme
    {
        public static readonly Color Accent = Color.FromArgb(215, 83, 43);
        public static readonly Color AccentSoft = Color.FromArgb(255, 241, 236);
        public static readonly Color Success = Color.FromArgb(22, 163, 74);
        public static readonly Color SuccessSoft = Color.FromArgb(232, 247, 238);
        public static readonly Color PageBackground = Color.FromArgb(248, 249, 251);
        public static readonly Color CardBackground = Color.White;
        public static readonly Color Border = Color.FromArgb(224, 226, 230);
        public static readonly Color Text = Color.FromArgb(18, 24, 38);
        public static readonly Color MutedText = Color.FromArgb(100, 107, 120);
        public static readonly Color DisabledText = Color.FromArgb(190, 193, 200);
        public static readonly Color DisabledBackground = Color.FromArgb(246, 247, 249);
    }

    internal class RoundedPanel : Panel
    {
        public int CornerRadius { get; set; } = 16;
        public Color FillColor { get; set; } = UiTheme.CardBackground;
        public Color BorderColor { get; set; } = UiTheme.Border;
        public int BorderThickness { get; set; } = 1;

        public RoundedPanel()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var brush = new SolidBrush(Parent?.BackColor ?? UiTheme.PageBackground);
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;

            using var path = CreateRoundedRectangle(rect, CornerRadius);
            using var fillBrush = new SolidBrush(FillColor);
            e.Graphics.FillPath(fillBrush, path);

            if (BorderThickness > 0)
            {
                using var pen = new Pen(BorderColor, BorderThickness);
                e.Graphics.DrawPath(pen, path);
            }
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            var diameter = Math.Max(1, radius * 2);

            if (diameter > rect.Width) diameter = rect.Width;
            if (diameter > rect.Height) diameter = rect.Height;

            var arc = new Rectangle(rect.Location, new Size(diameter, diameter));

            path.AddArc(arc, 180, 90);

            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);

            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);

            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }
    }

    internal class RoundedButton : Button
    {
        private bool _useAccentFill;

        public bool UseAccentFill
        {
            get => _useAccentFill;
            set
            {
                _useAccentFill = value;
                Invalidate();
            }
        }

        public int CornerRadius { get; set; } = 10;

        public RoundedButton()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;

            var fillColor = Enabled
                ? UseAccentFill ? UiTheme.Accent : Color.White
                : UiTheme.DisabledBackground;

            var borderColor = Enabled
                ? UseAccentFill ? UiTheme.Accent : UiTheme.Accent
                : UiTheme.Border;

            var textColor = Enabled
                ? UseAccentFill ? Color.White : UiTheme.Text
                : UiTheme.DisabledText;

            using var path = CreateRoundedRectangle(rect, CornerRadius);
            using var fillBrush = new SolidBrush(fillColor);
            using var borderPen = new Pen(borderColor, 1.5F);

            e.Graphics.FillPath(fillBrush, path);
            e.Graphics.DrawPath(borderPen, path);

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                rect,
                textColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis);
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            var diameter = Math.Max(1, radius * 2);

            if (diameter > rect.Width) diameter = rect.Width;
            if (diameter > rect.Height) diameter = rect.Height;

            var arc = new Rectangle(rect.Location, new Size(diameter, diameter));

            path.AddArc(arc, 180, 90);

            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);

            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);

            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }
    }
}