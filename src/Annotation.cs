using System;
using System.Drawing;

namespace Capture
{
    enum Tool { Select, Rectangle, Arrow, Line, Text }

    sealed class AnnotationStyle
    {
        public Color Color = Theme.Blue;
        public int StrokeWidth = 4;
        public int ArrowHeadSize = 16;
        public int TextSize = 22;
        public bool Bold;

        public AnnotationStyle Copy() { return (AnnotationStyle)MemberwiseClone(); }

        public Font CreateTextFont()
        {
            return new Font("Segoe UI", TextSize, Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        }
    }

    sealed class Annotation
    {
        public Tool Kind;
        public Point Start;
        public Point End;
        public AnnotationStyle Style;
        public string Text;

        // With an arrow the first click anchors its tip. The moving endpoint is the tail.
        public static Annotation StartAt(Tool tool, Point anchor, AnnotationStyle style)
        {
            return new Annotation { Kind = tool, Start = anchor, End = anchor, Style = style.Copy() };
        }

        public bool IsSegment { get { return Kind == Tool.Arrow || Kind == Tool.Line; } }

        public bool Valid
        {
            get
            {
                if (IsSegment) return Start != End;
                if (Kind == Tool.Rectangle) return Start.X != End.X && Start.Y != End.Y;
                return !string.IsNullOrWhiteSpace(Text);
            }
        }

        public Rectangle Bounds
        {
            get
            {
                return Rectangle.FromLTRB(Math.Min(Start.X, End.X), Math.Min(Start.Y, End.Y),
                    Math.Max(Start.X, End.X), Math.Max(Start.Y, End.Y));
            }
        }

        public void Draw(Graphics g)
        {
            if (!Valid) return;
            using (var brush = new SolidBrush(Style.Color))
            {
                if (Kind == Tool.Text)
                {
                    using (var font = Style.CreateTextFont()) g.DrawString(Text, font, brush, Start);
                    return;
                }
                using (var pen = new Pen(Style.Color, Style.StrokeWidth))
                {
                    if (Kind == Tool.Rectangle) g.DrawRectangle(pen, Bounds);
                    else if (Kind == Tool.Line) g.DrawLine(pen, Start, End);
                    else
                    {
                        PointF neck;
                        PointF[] head = ArrowHead(out neck);
                        float dx = head[1].X - head[2].X, dy = head[1].Y - head[2].Y;
                        float headWidth = (float)Math.Sqrt(dx * dx + dy * dy);
                        pen.Width = Math.Min(Style.StrokeWidth, Math.Max(1, headWidth));
                        g.DrawLine(pen, (PointF)End, neck);
                        g.FillPolygon(brush, head);
                    }
                }
            }
        }

        internal PointF[] ArrowHead(out PointF shaftEnd)
        {
            Point tail = End, tip = Start;
            double dx = (double)tip.X - tail.X, dy = (double)tip.Y - tail.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance == 0)
            {
                shaftEnd = tail;
                return new[] { (PointF)tip, tip, tip };
            }
            double ux = dx / distance, uy = dy / distance;
            double length = Math.Min(distance * 0.60, Style.ArrowHeadSize);
            double half = Math.Max(length * 0.42, Math.Min(distance * 0.42, Style.StrokeWidth * 0.60));
            double x = tip.X - ux * length, y = tip.Y - uy * length;
            shaftEnd = new PointF((float)(x + ux * length * 0.10), (float)(y + uy * length * 0.10));
            return new[] { (PointF)tip, new PointF((float)(x - uy * half), (float)(y + ux * half)),
                new PointF((float)(x + uy * half), (float)(y - ux * half)) };
        }

        public bool Hit(PointF point, RectangleF textBounds, float tolerance)
        {
            if (Kind == Tool.Text)
                return RectangleF.Inflate(textBounds, tolerance, tolerance).Contains(point);

            if (Kind == Tool.Rectangle)
            {
                RectangleF outer = Bounds;
                RectangleF inside = RectangleF.Inflate(outer, -tolerance, -tolerance);
                outer.Inflate(tolerance, tolerance);
                return outer.Contains(point) && !inside.Contains(point);
            }

            double dx = (double)End.X - Start.X, dy = (double)End.Y - Start.Y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared == 0) return false;
            double t = Math.Max(0, Math.Min(1, ((point.X - Start.X) * dx + (point.Y - Start.Y) * dy) / lengthSquared));
            double x = point.X - Start.X - t * dx, y = point.Y - Start.Y - t * dy;
            return x * x + y * y <= tolerance * tolerance;
        }
    }

    static class Geometry
    {
        public static int Clamp(int value, int min, int max) { return Math.Max(min, Math.Min(value, max)); }

        public static Rectangle Between(Point a, Point b)
        {
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        public static Rectangle AdjustSelection(Rectangle original, Point handle, Point start, Point location, Size size)
        {
            int x = Clamp(location.X, 0, size.Width);
            int y = Clamp(location.Y, 0, size.Height);
            if (handle == Point.Empty) return Between(start, new Point(x, y));
            return Rectangle.FromLTRB(
                handle.X == -1 ? Math.Min(x, original.Right - 1) : original.Left,
                handle.Y == -1 ? Math.Min(y, original.Bottom - 1) : original.Top,
                handle.X == 1 ? Math.Max(x, original.Left + 1) : original.Right,
                handle.Y == 1 ? Math.Max(y, original.Top + 1) : original.Bottom);
        }
    }
}
