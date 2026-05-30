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

    internal enum IconKind
    {
        None,
        Server,
        Account,
        ClientId,
        Printer,
        Document,
        Settings,
        LinkOff,
        Bars,
        Lightning
    }

    internal static class UiDrawing
    {
        public static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
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

        public static Color ResolveSurfaceColor(Control control, Color fallback)
        {
            if (control.Parent is RoundedPanel roundedParent)
            {
                return roundedParent.FillColor;
            }

            for (Control? parent = control.Parent; parent != null; parent = parent.Parent)
            {
                if (parent.BackColor.A > 0)
                {
                    return parent.BackColor;
                }
            }

            return fallback;
        }
    }

    internal static class UiIconPainter
    {
        public static void DrawIcon(Graphics g, IconKind kind, Rectangle bounds, Color color, float strokeWidth = 2.2F)
        {
            if (kind == IconKind.None)
            {
                return;
            }

            using var pen = new Pen(color, strokeWidth)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            using var brush = new SolidBrush(color);

            var x = bounds.X;
            var y = bounds.Y;
            var w = bounds.Width;
            var h = bounds.Height;

            switch (kind)
            {
                case IconKind.Server:
                    DrawServer(g, pen, brush, x, y, w, h);
                    break;

                case IconKind.Account:
                    DrawAccount(g, pen, x, y, w, h);
                    break;

                case IconKind.ClientId:
                    DrawClientId(g, pen, x, y, w, h);
                    break;

                case IconKind.Printer:
                    DrawPrinter(g, pen, x, y, w, h);
                    break;

                case IconKind.Document:
                    DrawDocument(g, pen, x, y, w, h);
                    break;

                case IconKind.Settings:
                    DrawSettings(g, pen, x, y, w, h);
                    break;

                case IconKind.LinkOff:
                    DrawLinkOff(g, pen, x, y, w, h);
                    break;

                case IconKind.Bars:
                    DrawBars(g, brush, x, y, w, h);
                    break;

                case IconKind.Lightning:
                    DrawLightning(g, brush, x, y, w, h);
                    break;
            }
        }

        private static void DrawServer(Graphics g, Pen pen, Brush brush, int x, int y, int w, int h)
        {
            var r1 = new Rectangle(x + w / 5, y + h / 4, w * 3 / 5, h / 5);
            var r2 = new Rectangle(x + w / 5, y + h / 2, w * 3 / 5, h / 5);

            using var p1 = UiDrawing.CreateRoundedRectangle(r1, 4);
            using var p2 = UiDrawing.CreateRoundedRectangle(r2, 4);

            g.DrawPath(pen, p1);
            g.DrawPath(pen, p2);

            g.FillEllipse(brush, r1.X + 5, r1.Y + r1.Height / 2 - 2, 4, 4);
            g.FillEllipse(brush, r2.X + 5, r2.Y + r2.Height / 2 - 2, 4, 4);
        }

        private static void DrawAccount(Graphics g, Pen pen, int x, int y, int w, int h)
        {
            var head = new Rectangle(x + w / 2 - w / 8, y + h / 5, w / 4, w / 4);
            g.DrawEllipse(pen, head);

            var body = new Rectangle(x + w / 4, y + h / 2, w / 2, h / 3);
            g.DrawArc(pen, body, 200, 140);
        }

        private static void DrawClientId(Graphics g, Pen pen, int x, int y, int w, int h)
        {
            var card = new Rectangle(x + w / 5, y + h / 4, w * 3 / 5, h / 2);
            g.DrawRectangle(pen, card);

            var avatar = new Rectangle(card.X + 5, card.Y + 8, 8, 8);
            g.DrawEllipse(pen, avatar);
            g.DrawArc(pen, card.X + 3, card.Y + 17, 12, 10, 200, 140);

            g.DrawLine(pen, card.X + 22, card.Y + 12, card.Right - 6, card.Y + 12);
            g.DrawLine(pen, card.X + 22, card.Y + 24, card.Right - 10, card.Y + 24);
        }

        private static void DrawPrinter(Graphics g, Pen pen, int x, int y, int w, int h)
        {
            var paper = new Rectangle(x + w / 3, y + h / 7, w / 3, h / 4);
            var body = new Rectangle(x + w / 5, y + h / 3, w * 3 / 5, h / 3);
            var output = new Rectangle(x + w / 3, y + h * 3 / 5, w / 3, h / 4);

            g.DrawRectangle(pen, paper);
            g.DrawRectangle(pen, body);
            g.DrawRectangle(pen, output);
            g.DrawLine(pen, body.X + 6, body.Y + 8, body.X + 12, body.Y + 8);
        }

        private static void DrawDocument(Graphics g, Pen pen, int x, int y, int w, int h)
        {
            var left = x + w / 4;
            var top = y + h / 6;
            var right = x + w * 3 / 4;
            var bottom = y + h * 5 / 6;
            var fold = w / 6;

            using var path = new GraphicsPath();
            path.AddLine(left, top, right - fold, top);
            path.AddLine(right - fold, top, right, top + fold);
            path.AddLine(right, top + fold, right, bottom);
            path.AddLine(right, bottom, left, bottom);
            path.AddLine(left, bottom, left, top);
            path.CloseFigure();

            g.DrawPath(pen, path);
            g.DrawLine(pen, right - fold, top, right - fold, top + fold);
            g.DrawLine(pen, right - fold, top + fold, right, top + fold);

            g.DrawLine(pen, left + 7, top + h / 3, right - 7, top + h / 3);
            g.DrawLine(pen, left + 7, top + h / 2, right - 12, top + h / 2);
        }

        private static void DrawSettings(Graphics g, Pen pen, int x, int y, int w, int h)
        {
            var cx = x + w / 2;
            var cy = y + h / 2;
            var outer = Math.Min(w, h) / 3;
            var inner = Math.Min(w, h) / 9;

            for (var i = 0; i < 8; i++)
            {
                var angle = Math.PI * 2 * i / 8;
                var x1 = cx + (int)(Math.Cos(angle) * (outer - 3));
                var y1 = cy + (int)(Math.Sin(angle) * (outer - 3));
                var x2 = cx + (int)(Math.Cos(angle) * (outer + 3));
                var y2 = cy + (int)(Math.Sin(angle) * (outer + 3));
                g.DrawLine(pen, x1, y1, x2, y2);
            }

            g.DrawEllipse(pen, cx - outer + 3, cy - outer + 3, (outer - 3) * 2, (outer - 3) * 2);
            g.DrawEllipse(pen, cx - inner, cy - inner, inner * 2, inner * 2);
        }

        private static void DrawLinkOff(Graphics g, Pen pen, int x, int y, int w, int h)
        {
            var left = new Rectangle(x + w / 5, y + h / 3, w / 3, h / 3);
            var right = new Rectangle(x + w * 7 / 15, y + h / 3, w / 3, h / 3);

            g.DrawArc(pen, left, 110, 220);
            g.DrawArc(pen, right, -70, 220);
            g.DrawLine(pen, x + w / 4, y + h * 3 / 4, x + w * 3 / 4, y + h / 4);
        }

        private static void DrawBars(Graphics g, Brush brush, int x, int y, int w, int h)
        {
            var barWidth = Math.Max(3, w / 8);
            var gap = Math.Max(3, w / 10);
            var baseY = y + h * 3 / 4;

            g.FillRectangle(brush, x + w / 4, baseY - h / 4, barWidth, h / 4);
            g.FillRectangle(brush, x + w / 4 + barWidth + gap, baseY - h / 2, barWidth, h / 2);
            g.FillRectangle(brush, x + w / 4 + (barWidth + gap) * 2, baseY - h * 2 / 3, barWidth, h * 2 / 3);
        }

        private static void DrawLightning(Graphics g, Brush brush, int x, int y, int w, int h)
        {
            var points = new[]
            {
                new PointF(x + w * 0.55F, y + h * 0.08F),
                new PointF(x + w * 0.25F, y + h * 0.54F),
                new PointF(x + w * 0.48F, y + h * 0.54F),
                new PointF(x + w * 0.37F, y + h * 0.92F),
                new PointF(x + w * 0.76F, y + h * 0.42F),
                new PointF(x + w * 0.52F, y + h * 0.42F)
            };

            g.FillPolygon(brush, points);
        }
    }

    internal class IconBadge : Control
    {
        public IconKind Kind { get; set; } = IconKind.None;
        public bool Circle { get; set; } = true;
        public Color CircleBackColor { get; set; } = UiTheme.AccentSoft;
        public Color IconColor { get; set; } = UiTheme.Accent;

        public IconBadge()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var brush = new SolidBrush(UiDrawing.ResolveSurfaceColor(this, UiTheme.PageBackground));
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;

            if (Circle)
            {
                using var circleBrush = new SolidBrush(CircleBackColor);
                e.Graphics.FillEllipse(circleBrush, rect);
            }

            var padding = Circle ? 14 : 2;
            var iconRect = Rectangle.Inflate(rect, -padding, -padding);
            UiIconPainter.DrawIcon(e.Graphics, Kind, iconRect, IconColor);
        }
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
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using var brush = new SolidBrush(UiDrawing.ResolveSurfaceColor(this, UiTheme.PageBackground));
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;

            using var path = UiDrawing.CreateRoundedRectangle(rect, CornerRadius);
            using var fillBrush = new SolidBrush(FillColor);
            e.Graphics.FillPath(fillBrush, path);

            if (BorderThickness > 0)
            {
                using var pen = new Pen(BorderColor, BorderThickness);
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    internal class RoundedButton : Button
    {
        private bool _useAccentFill;
        private IconKind _iconKind = IconKind.None;

        public bool UseAccentFill
        {
            get => _useAccentFill;
            set
            {
                _useAccentFill = value;
                Invalidate();
            }
        }

        public IconKind IconKind
        {
            get => _iconKind;
            set
            {
                _iconKind = value;
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
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var backgroundBrush = new SolidBrush(UiDrawing.ResolveSurfaceColor(this, UiTheme.PageBackground));
            e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);

            var rect = ClientRectangle;
            rect.Inflate(-1, -1);

            var fillColor = Enabled
                ? UseAccentFill ? UiTheme.Accent : Color.White
                : UiTheme.DisabledBackground;

            var borderColor = Enabled
                ? UseAccentFill ? UiTheme.Accent : UiTheme.Accent
                : UiTheme.Border;

            var contentColor = Enabled
                ? UseAccentFill ? Color.White : UiTheme.Text
                : UiTheme.DisabledText;

            using var path = UiDrawing.CreateRoundedRectangle(rect, CornerRadius);
            using var fillBrush = new SolidBrush(fillColor);
            using var borderPen = new Pen(borderColor, 1.4F);

            e.Graphics.FillPath(fillBrush, path);
            e.Graphics.DrawPath(borderPen, path);

            DrawButtonContent(e.Graphics, rect, contentColor);
        }

        private void DrawButtonContent(Graphics graphics, Rectangle rect, Color contentColor)
        {
            var text = Text ?? string.Empty;
            var iconSize = IconKind == IconKind.None ? 0 : 22;
            var gap = IconKind == IconKind.None ? 0 : 10;

            var textSize = TextRenderer.MeasureText(
                graphics,
                text,
                Font,
                new Size(rect.Width, rect.Height),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            var totalWidth = iconSize + gap + textSize.Width;
            var startX = rect.X + Math.Max(0, (rect.Width - totalWidth) / 2);
            var centerY = rect.Y + rect.Height / 2;

            if (IconKind != IconKind.None)
            {
                var iconRect = new Rectangle(startX, centerY - iconSize / 2, iconSize, iconSize);
                UiIconPainter.DrawIcon(graphics, IconKind, iconRect, contentColor, 2.1F);
                startX += iconSize + gap;
            }

            var textRect = new Rectangle(
                startX,
                rect.Y,
                Math.Min(textSize.Width + 6, rect.Right - startX),
                rect.Height);

            TextRenderer.DrawText(
                graphics,
                text,
                Font,
                textRect,
                contentColor,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding);
        }
    }
}