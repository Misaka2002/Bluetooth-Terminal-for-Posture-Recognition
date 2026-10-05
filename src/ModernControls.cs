using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PostureStatistics
{
    internal static class ModernDrawing
    {
        public static GraphicsPath Round(RectangleF rectangle, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rectangle.X, rectangle.Y, d, d, 180, 90);
            path.AddArc(rectangle.Right - d, rectangle.Y, d, d, 270, 90);
            path.AddArc(rectangle.Right - d, rectangle.Bottom - d, d, d, 0, 90);
            path.AddArc(rectangle.X, rectangle.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
    }

    internal sealed class RoundedButton : Button
    {
        private bool hover;
        private bool pressed;
        public bool Primary { get; set; }
        public RoundedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Height = 40; TabStop = true;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent == null ? Color.FromArgb(242, 246, 251) : Parent.BackColor);
            Color fill = Primary ? Color.FromArgb(33, 106, 227) : Color.White;
            if (hover) fill = Primary ? Color.FromArgb(25, 91, 203) : Color.FromArgb(234, 242, 253);
            if (pressed) fill = Primary ? Color.FromArgb(22, 76, 172) : Color.FromArgb(221, 234, 252);
            if (!Enabled) fill = Color.FromArgb(235, 239, 244);
            // 边框留在客户区内，避免底边被布局或高 DPI 裁掉。
            using (GraphicsPath path = ModernDrawing.Round(new RectangleF(1, 1, Width - 3, Height - 3), 10))
            using (Brush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(Primary ? fill : Color.FromArgb(211, 222, 237)))
            { g.FillPath(brush, path); g.DrawPath(pen, path); }
            Color text = !Enabled ? Color.FromArgb(147, 158, 174) : Primary ? Color.White : Color.FromArgb(42, 65, 93);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(8, 2, Width - 16, Height - 4), text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues)
                using (GraphicsPath focus = ModernDrawing.Round(new RectangleF(4, 4, Width - 9, Height - 9), 7))
                using (Pen pen = new Pen(Primary ? Color.White : Color.FromArgb(33, 106, 227)) { DashStyle = DashStyle.Dot }) g.DrawPath(pen, focus);
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        public RoundedPanel() { DoubleBuffered = true; BackColor = Color.FromArgb(242, 246, 251); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = ModernDrawing.Round(new RectangleF(1, 1, Width - 3, Height - 3), 10))
            using (Brush brush = new SolidBrush(Color.White))
            using (Pen pen = new Pen(Color.FromArgb(211, 222, 237)))
            { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
        }
    }
}
