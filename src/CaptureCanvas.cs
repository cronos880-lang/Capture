using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Capture
{
    sealed class CaptureCanvas : Control
    {
        enum Endpoint { Body, Start, End }

        sealed class Drag
        {
            public Point Pointer, Start, End;
            public Point ResizeHandle;
            public Endpoint Part;
            public bool Creating;
        }

        readonly List<Annotation> annotations = new List<Annotation>();
        readonly Dictionary<Tool, AnnotationStyle> defaults = new Dictionary<Tool, AnnotationStyle>
        {
            { Tool.Rectangle, new AnnotationStyle() },
            { Tool.Ellipse, new AnnotationStyle() },
            { Tool.Arrow, new AnnotationStyle { Color = Color.FromArgb(209, 74, 19) } },
            { Tool.Line, new AnnotationStyle() },
            { Tool.Text, new AnnotationStyle() }
        };
        static readonly Point[] RectangleHandles =
        {
            new Point(-1, -1), new Point(0, -1), new Point(1, -1),
            new Point(-1, 0),                   new Point(1, 0),
            new Point(-1, 1), new Point(0, 1), new Point(1, 1)
        };
        readonly Font emptyFont = new Font("Segoe UI", 14, FontStyle.Regular, GraphicsUnit.Point);
        Bitmap image;
        Drag drag;
        public Tool ActiveTool { get; private set; }
        public Annotation Selected { get; private set; }
        public bool Modified { get; private set; }
        public bool IsDragging { get { return drag != null; } }
        public bool HasImage { get { return image != null; } }
        public Tool StyleTool { get { return Selected == null ? ActiveTool : Selected.Kind; } }
        public AnnotationStyle EditableStyle
        {
            get { return Selected != null ? Selected.Style : ActiveTool != Tool.Select ? defaults[ActiveTool] : null; }
        }
        public Size ImageSize { get { return image == null ? Size.Empty : image.Size; } }
        public event EventHandler StateChanged;
        public event EventHandler EmptyClicked;

        public CaptureCanvas()
        {
            BackColor = Theme.Background;
            AccessibleName = "Lienzo de captura";
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
        }

        public void SetImage(Bitmap bitmap)
        {
            if (image != null) image.Dispose();
            image = bitmap;
            annotations.Clear();
            Selected = null;
            ActiveTool = Tool.Select;
            Modified = false;
            drag = null;
            Changed();
        }

        public void ChooseTool(Tool tool)
        {
            if (drag != null) CancelDrag();
            ActiveTool = tool;
            Selected = null;
            Changed();
        }

        void Changed()
        {
            Cursor = !HasImage ? Cursors.Hand : ActiveTool == Tool.Select ? Cursors.Default : Cursors.Cross;
            Invalidate();
            var handler = StateChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public void UpdateStyle(Action<AnnotationStyle> update)
        {
            var style = EditableStyle;
            if (style == null || drag != null) return;
            update(style);
            if (Selected != null) Modified = true;
            Changed();
        }

        public void DeleteSelected()
        {
            if (drag != null || Selected == null) return;
            annotations.Remove(Selected);
            Selected = null;
            Modified = true;
            Changed();
        }

        public void MarkExported() { Modified = false; }

        public Bitmap RenderImage()
        {
            var output = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppRgb);
            try
            {
                using (var graphics = Graphics.FromImage(output))
                {
                    graphics.DrawImageUnscaled(image, 0, 0);
                    DrawAnnotations(graphics);
                }
                return output;
            }
            catch { output.Dispose(); throw; }
        }

        void DrawAnnotations(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var annotation in annotations) annotation.Draw(graphics);
        }

        RectangleF View()
        {
            return Geometry.Fit(image.Size, ClientSize, 8 * DeviceDpi / 96);
        }

        Point ImagePoint(Point location, RectangleF view)
        {
            float scale = view.Width / image.Width;
            return new Point((int)Math.Floor((location.X - view.Left) / scale),
                (int)Math.Floor((location.Y - view.Top) / scale));
        }

        RectangleF HandleBox(Point p, RectangleF view)
        {
            float scale = view.Width / image.Width;
            float side = Math.Max(9, 9 * DeviceDpi / 96.0f);
            return new RectangleF(view.Left + p.X * scale - side / 2, view.Top + p.Y * scale - side / 2, side, side);
        }

        static double HandleDistance(RectangleF box, Point p)
        {
            if (!box.Contains(p)) return double.PositiveInfinity;
            double dx = p.X - box.Left - box.Width / 2, dy = p.Y - box.Top - box.Height / 2;
            return dx * dx + dy * dy;
        }

        Endpoint PickEndpoint(Point p, RectangleF view)
        {
            if (Selected == null || !Selected.IsSegment) return Endpoint.Body;
            double start = HandleDistance(HandleBox(Selected.Start, view), p);
            double end = HandleDistance(HandleBox(Selected.End, view), p);
            if (double.IsPositiveInfinity(Math.Min(start, end))) return Endpoint.Body;
            return end <= start ? Endpoint.End : Endpoint.Start;
        }

        static Point RectangleHandlePosition(Rectangle bounds, Point direction)
        {
            return new Point(bounds.Left + (direction.X + 1) * bounds.Width / 2,
                bounds.Top + (direction.Y + 1) * bounds.Height / 2);
        }

        Point PickResizeHandle(Point cursor, RectangleF view)
        {
            if (Selected == null || !Selected.IsClosedShape) return Point.Empty;
            Point closest = Point.Empty;
            var distance = double.PositiveInfinity;
            foreach (var direction in RectangleHandles)
            {
                double candidate = HandleDistance(HandleBox(RectangleHandlePosition(Selected.Bounds, direction), view), cursor);
                if (candidate >= distance) continue;
                closest = direction;
                distance = candidate;
            }
            return closest;
        }

        RectangleF TextBounds(Annotation annotation)
        {
            if (annotation.Kind != Tool.Text) return RectangleF.Empty;
            using (var font = annotation.Style.CreateTextFont())
                return new RectangleF(annotation.Start, TextRenderer.MeasureText(annotation.Text, font));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            if (!HasImage)
            {
                TextRenderer.DrawText(g, "Selecciona una zona de la pantalla", emptyFont, ClientRectangle,
                    Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            var view = View();
            if (view.IsEmpty) return;
            using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, view.X - 1, view.Y - 1, view.Width + 2, view.Height + 2);

            GraphicsState saved = g.Save();
            g.SetClip(view);
            g.TranslateTransform(view.Left, view.Top);
            float ratio = view.Width / image.Width;
            g.ScaleTransform(ratio, ratio);
            g.InterpolationMode = ratio == 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, 0, 0, image.Width, image.Height);
            DrawAnnotations(g);

            if (Selected != null && (Selected.Kind == Tool.Text || Selected.Kind == Tool.Ellipse))
            {
                RectangleF bounds = Selected.Kind == Tool.Text ? TextBounds(Selected) : Selected.Bounds;
                bounds.Inflate(4 / ratio, 4 / ratio);
                using (var pen = new Pen(Theme.Accent, 1 / ratio) { DashStyle = DashStyle.Dash })
                    g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            }
            g.Restore(saved);

            if (Selected != null && drag == null)
            {
                using (var pen = new Pen(Theme.Primary, 1.5f))
                {
                    if (Selected.IsSegment)
                    {
                        DrawHandle(g, HandleBox(Selected.Start, view), pen);
                        DrawHandle(g, HandleBox(Selected.End, view), pen);
                    }
                    else if (Selected.IsClosedShape)
                        foreach (var direction in RectangleHandles)
                            DrawHandle(g, HandleBox(RectangleHandlePosition(Selected.Bounds, direction), view), pen);
                }
            }
        }

        static void DrawHandle(Graphics g, RectangleF box, Pen pen)
        {
            g.FillRectangle(Brushes.White, box);
            g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || drag != null) return;
            if (!HasImage)
            {
                var handler = EmptyClicked;
                if (handler != null) handler(this, EventArgs.Empty);
                return;
            }
            Focus();

            var view = View();
            if (view.IsEmpty) return;
            Point pixel = ImagePoint(e.Location, view);

            if (ActiveTool == Tool.Select)
            {
                Point resizeHandle = PickResizeHandle(e.Location, view);
                if (resizeHandle != Point.Empty)
                {
                    StartDrag(pixel, Endpoint.Body, false, resizeHandle);
                    return;
                }
                Endpoint endpoint = PickEndpoint(e.Location, view);
                if (endpoint != Endpoint.Body)
                {
                    StartDrag(pixel, endpoint, false);
                    return;
                }
                Selected = null;
                float tolerance = 5 * image.Width / view.Width;
                if (view.Contains(e.Location))
                    for (int i = annotations.Count - 1; i >= 0; i--)
                    {
                        var item = annotations[i];
                        if (!item.Hit(pixel, TextBounds(item), tolerance + item.Style.StrokeWidth / 2.0f)) continue;
                        Selected = item;
                        StartDrag(pixel, Endpoint.Body, false);
                        return;
                    }
                Changed();
            }
            else if (view.Contains(e.Location))
            {
                var item = Annotation.StartAt(ActiveTool, pixel, defaults[ActiveTool]);
                if (ActiveTool == Tool.Text)
                {
                    item.Text = PromptForText();
                    if (!item.Valid) return;
                    annotations.Add(item);
                    Selected = item;
                    ActiveTool = Tool.Select;
                    Modified = true;
                    Changed();
                }
                else
                {
                    annotations.Add(item);
                    Selected = item;
                    StartDrag(pixel, Endpoint.End, true);
                }
            }
        }

        void StartDrag(Point pointer, Endpoint endpoint, bool creating, Point resizeHandle = default(Point))
        {
            drag = new Drag
            {
                Pointer = pointer, Start = Selected.Start, End = Selected.End, Part = endpoint,
                Creating = creating, ResizeHandle = resizeHandle
            };
            Capture = true;
            Changed();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (drag != null) MoveDrag(e.Location);
            else if (HasImage && ActiveTool == Tool.Select)
            {
                var view = View();
                Cursor = !view.IsEmpty && (PickEndpoint(e.Location, view) != Endpoint.Body ||
                    PickResizeHandle(e.Location, view) != Point.Empty) ? Cursors.Hand : Cursors.Default;
            }
        }

        void MoveDrag(Point location)
        {
            var view = View();
            if (view.IsEmpty) return;
            Point point = ImagePoint(location, view);
            int dx = point.X - drag.Pointer.X, dy = point.Y - drag.Pointer.Y;
            if (drag.Creating && Selected.Kind == Tool.Ellipse)
            {
                Rectangle ellipse = Geometry.EllipseFromCenter(drag.Pointer, point, image.Size);
                Selected.Start = ellipse.Location;
                Selected.End = new Point(ellipse.Right, ellipse.Bottom);
                Invalidate();
                return;
            }
            if (drag.ResizeHandle != Point.Empty)
            {
                var original = Geometry.Between(drag.Start, drag.End);
                var corner = RectangleHandlePosition(original, drag.ResizeHandle);
                var resized = Geometry.AdjustSelection(original, drag.ResizeHandle, drag.Pointer,
                    new Point(corner.X + dx, corner.Y + dy), new Size(image.Width - 1, image.Height - 1));
                if (resized == original)
                {
                    Selected.Start = drag.Start;
                    Selected.End = drag.End;
                }
                else
                {
                    Selected.Start = resized.Location;
                    Selected.End = new Point(resized.Right, resized.Bottom);
                }
                Invalidate();
                return;
            }
            switch (drag.Part)
            {
                case Endpoint.Start:
                    Selected.Start = Clamp(new Point(drag.Start.X + dx, drag.Start.Y + dy));
                    break;
                case Endpoint.End:
                    Selected.End = Clamp(new Point(drag.End.X + dx, drag.End.Y + dy));
                    break;
                default:
                    var original = Geometry.Between(drag.Start, drag.End);
                    int width = Math.Max(1, original.Width + 1), height = Math.Max(1, original.Height + 1);
                    if (Selected.Kind == Tool.Text)
                    {
                        RectangleF bounds = TextBounds(Selected);
                        width = (int)Math.Ceiling(bounds.Width);
                        height = (int)Math.Ceiling(bounds.Height);
                    }
                    dx = Geometry.Clamp(dx, -original.Left, Math.Max(-original.Left, image.Width - original.Left - width));
                    dy = Geometry.Clamp(dy, -original.Top, Math.Max(-original.Top, image.Height - original.Top - height));
                    Selected.Start = new Point(drag.Start.X + dx, drag.Start.Y + dy);
                    Selected.End = new Point(drag.End.X + dx, drag.End.Y + dy);
                    break;
            }
            Invalidate();
        }

        Point Clamp(Point p)
        {
            return new Point(Geometry.Clamp(p.X, 0, image.Width - 1), Geometry.Clamp(p.Y, 0, image.Height - 1));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (drag == null || e.Button != MouseButtons.Left) return;
            MoveDrag(e.Location);
            CompleteDrag(true);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (drag != null && !Capture) CompleteDrag(false);
        }

        public void CancelDrag()
        {
            if (drag != null) CompleteDrag(false);
        }

        void CompleteDrag(bool commit)
        {
            var previous = drag;
            drag = null;
            if (!commit || !Selected.Valid)
            {
                if (previous.Creating)
                {
                    annotations.Remove(Selected);
                    Selected = null;
                }
                else
                {
                    Selected.Start = previous.Start;
                    Selected.End = previous.End;
                }
            }
            else if (previous.Creating || previous.Start != Selected.Start || previous.End != Selected.End) Modified = true;
            if (previous.Creating) ActiveTool = Tool.Select;
            Capture = false;
            Changed();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Delete && drag == null)
            {
                DeleteSelected();
                e.Handled = true;
            }
        }

        string PromptForText()
        {
            using (var dialog = new Form
            {
                Text = "Añadir texto", StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, ShowInTaskbar = false, MaximizeBox = false,
                MinimizeBox = false, ClientSize = new Size(350, 95), Font = Font, Padding = new Padding(12),
                BackColor = Theme.Surface, ForeColor = Theme.Ink
            })
            {
                dialog.HandleCreated += delegate { Theme.ApplyDarkTitleBar(dialog.Handle); };
                var input = new TextBox
                {
                    Dock = DockStyle.Top, AccessibleName = "Texto de la anotación",
                    BackColor = Theme.ButtonSurface, ForeColor = Theme.Ink
                };
                var add = Theme.Button("Añadir", true);
                add.DialogResult = DialogResult.OK;
                var cancel = Theme.Button("Cancelar");
                cancel.DialogResult = DialogResult.Cancel;
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 35, FlowDirection = FlowDirection.RightToLeft };
                buttons.Controls.Add(add);
                buttons.Controls.Add(cancel);
                dialog.Controls.Add(input);
                dialog.Controls.Add(buttons);
                dialog.AcceptButton = add;
                dialog.CancelButton = cancel;
                dialog.ActiveControl = input;
                return dialog.ShowDialog(FindForm()) == DialogResult.OK ? input.Text.Trim() : null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (image != null) { image.Dispose(); image = null; }
                emptyFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
