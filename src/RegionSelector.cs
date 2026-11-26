using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Capture
{
    sealed class RegionSelector : Form
    {
        const int PaddingSize = 8;
        readonly Rectangle desktopBounds;
        readonly Bitmap desktop;
        readonly LayeredFrame frame;
        readonly Font instructionFont = new Font("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Pixel);
        readonly Font actionFont = new Font("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);
        readonly Rectangle hintBounds;
        Rectangle selection, original, actions, cancel, confirm;
        Point start;
        Point handle;
        bool dragging, hint = true;
        int pressedButton;
        Bitmap result;

        public RegionSelector(Rectangle bounds, Bitmap source)
        {
            desktopBounds = bounds;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            Bounds = bounds;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            hintBounds = HintBounds();
            try { frame = new LayeredFrame(bounds.Size); }
            catch
            {
                instructionFont.Dispose();
                actionFont.Dispose();
                throw;
            }
            desktop = source;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000; // WS_EX_LAYERED. The compositor gets one finished frame.
                return cp;
            }
        }

        public void Open()
        {
            Present(new Rectangle(Point.Empty, desktopBounds.Size));
            Show();
            Activate();
        }

        public Bitmap TakeResult()
        {
            Bitmap bitmap = result;
            result = null;
            return bitmap;
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == Keys.Enter && CanConfirm) { Complete(); return true; }
            return base.ProcessCmdKey(ref message, keyData);
        }

        bool CanConfirm { get { return !dragging && selection.Width > 0 && selection.Height > 0; } }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || dragging) return;
            pressedButton = ButtonAt(e.Location);
            if (pressedButton != 0)
            {
                Capture = true;
                return;
            }

            var dirty = actions;
            original = selection;
            start = e.Location;
            handle = HandleAt(start);
            dragging = true;
            actions = Rectangle.Empty;
            if (hint)
            {
                dirty = hintBounds;
                hint = false;
            }
            Capture = true;
            Present(dirty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) MoveSelection(e.Location);
            else Cursor = ButtonAt(e.Location) != 0 ? Cursors.Hand : CursorFor(HandleAt(e.Location));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (pressedButton != 0)
            {
                int button = pressedButton == ButtonAt(e.Location) ? pressedButton : 0;
                pressedButton = 0;
                Capture = false;
                if (button == 1) Close();
                if (button == 2 && CanConfirm) Complete();
                return;
            }

            if (!dragging) return;
            MoveSelection(e.Location);
            dragging = false;
            Capture = false;
            PositionActions(e.Location);
            Present(actions);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (Capture) return;
            pressedButton = 0;
            if (!dragging) return;
            dragging = false;
            var dirty = ChangedArea(selection, original);
            selection = original;
            PositionActions(start);
            Present(Combine(dirty, actions));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            dragging = false;
            pressedButton = 0;
            base.OnFormClosing(e);
        }

        void MoveSelection(Point location)
        {
            Rectangle next = Geometry.AdjustSelection(original, handle, start, location, desktopBounds.Size);
            if (next == selection) return;
            var dirty = ChangedArea(selection, next);
            selection = next;
            Present(dirty);
        }

        void Complete()
        {
            result = desktop.Clone(selection, PixelFormat.Format32bppRgb);
            Close();
        }

        void PositionActions(Point cursor)
        {
            actions = Rectangle.Empty;
            if (!CanConfirm) return;
            var monitor = Screen.FromPoint(new Point(desktopBounds.Left + cursor.X, desktopBounds.Top + cursor.Y)).Bounds;
            monitor.Offset(-desktopBounds.Left, -desktopBounds.Top);
            const int width = 322, height = 44;
            int x = Geometry.Clamp(selection.Right - width, monitor.Left + 6, monitor.Right - width - 6);
            int y = selection.Bottom + 10;
            if (y + height > monitor.Bottom) y = selection.Top - height - 10;
            y = Geometry.Clamp(y, monitor.Top + 6, monitor.Bottom - height - 6);
            actions = new Rectangle(x, y, width, height);
            cancel = new Rectangle(x + 130, y + 6, 82, 32);
            confirm = new Rectangle(x + 218, y + 6, 97, 32);
        }

        Rectangle HintBounds()
        {
            var monitor = Screen.FromPoint(System.Windows.Forms.Cursor.Position).Bounds;
            const int width = 305;
            return new Rectangle(monitor.Left - desktopBounds.Left + (monitor.Width - width) / 2,
                monitor.Top - desktopBounds.Top + 24, width, 42);
        }

        int ButtonAt(Point p)
        {
            if (actions.IsEmpty) return 0;
            if (cancel.Contains(p)) return 1;
            return confirm.Contains(p) ? 2 : 0;
        }

        Point HandleAt(Point p)
        {
            if (selection.Width <= 0 || selection.Height <= 0) return Point.Empty;
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    if ((x != 0 || y != 0) && HandleBox(selection, x, y).Contains(p))
                        return new Point(x, y);
            return Point.Empty;
        }

        static Cursor CursorFor(Point handle)
        {
            if (handle == Point.Empty) return Cursors.Cross;
            if (handle.X == 0) return Cursors.SizeNS;
            if (handle.Y == 0) return Cursors.SizeWE;
            return handle.X == handle.Y ? Cursors.SizeNWSE : Cursors.SizeNESW;
        }

        static Rectangle HandleBox(Rectangle r, int x, int y)
        {
            return new Rectangle(r.Left + (x + 1) * r.Width / 2 - 4, r.Top + (y + 1) * r.Height / 2 - 4, 8, 8);
        }

        static Rectangle Combine(Rectangle a, Rectangle b)
        {
            if (a.Width <= 0 || a.Height <= 0) return b;
            if (b.Width <= 0 || b.Height <= 0) return a;
            return Rectangle.Union(a, b);
        }

        static Rectangle ChangedArea(Rectangle a, Rectangle b)
        {
            var dirty = Combine(a, b);
            if (dirty.Width > 0 && dirty.Height > 0) dirty.Inflate(PaddingSize, PaddingSize);
            return dirty;
        }

        void Present(Rectangle dirty)
        {
            dirty.Intersect(new Rectangle(Point.Empty, desktopBounds.Size));
            if (dirty.Width <= 0 || dirty.Height <= 0 || IsDisposed) return;

            using (var g = Graphics.FromImage(frame.Bitmap))
            using (var shade = new SolidBrush(Color.FromArgb(135, 13, 23, 41)))
            using (var dimmed = new Region(dirty))
            using (var border = new Pen(Theme.Blue, 2))
            using (var white = new SolidBrush(Color.White))
            using (var blue = new SolidBrush(Theme.Blue))
            using (var gray = new SolidBrush(Theme.Light))
            using (var ink = new SolidBrush(Theme.Ink))
            {
                g.SetClip(dirty);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(desktop, dirty, dirty, GraphicsUnit.Pixel);
                g.CompositingMode = CompositingMode.SourceOver;
                dimmed.Exclude(selection);
                g.FillRegion(shade, dimmed);
                if (selection.Width > 0 && selection.Height > 0)
                {
                    g.DrawRectangle(border, selection);
                    for (int x = -1; x <= 1; x++)
                        for (int y = -1; y <= 1; y++)
                        {
                            if (x == 0 && y == 0) continue;
                            var box = HandleBox(selection, x, y);
                            g.FillRectangle(white, box);
                            g.DrawRectangle(border, box);
                        }
                }

                using (var textStyle = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    if (hint)
                    {
                        g.FillRectangle(white, hintBounds);
                        g.DrawString("Arrastra para seleccionar un área", actionFont, ink, hintBounds, textStyle);
                    }
                    if (!actions.IsEmpty)
                    {
                        g.FillRectangle(white, actions);
                        g.DrawString(string.Format("{0} × {1} px", selection.Width, selection.Height),
                            instructionFont, ink, new Rectangle(actions.X, actions.Y, 125, actions.Height), textStyle);
                        g.FillRectangle(gray, cancel);
                        g.DrawString("Cancelar", actionFont, ink, cancel, textStyle);
                        g.FillRectangle(blue, confirm);
                        g.DrawString("Capturar", actionFont, white, confirm, textStyle);
                    }
                }
            }
            frame.Present(Handle, desktopBounds.Location);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (frame != null) frame.Dispose();
                if (desktop != null) desktop.Dispose();
                if (result != null) result.Dispose();
                instructionFont.Dispose();
                actionFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // A persistent top-down 32-bit DIB. UpdateLayeredWindow swaps the prepared image atomically.
    sealed class LayeredFrame : IDisposable
    {
        readonly Size size;
        IntPtr dc, dib, previous;
        public Bitmap Bitmap { get; private set; }

        public LayeredFrame(Size size)
        {
            this.size = size;
            dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) throw new Win32Exception();
            try
            {
                var header = new BitmapInfo
                {
                    HeaderSize = (uint)Marshal.SizeOf(typeof(BitmapInfo)), Width = size.Width,
                    Height = -size.Height, Planes = 1, BitCount = 32
                };
                IntPtr pixels;
                dib = CreateDIBSection(dc, ref header, 0, out pixels, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero || pixels == IntPtr.Zero) throw new Win32Exception();
                previous = SelectObject(dc, dib);
                if (previous == IntPtr.Zero || previous == new IntPtr(-1)) throw new Win32Exception();
                Bitmap = new Bitmap(size.Width, size.Height, checked(size.Width * 4), PixelFormat.Format32bppPArgb, pixels);
            }
            catch { Dispose(); throw; }
        }

        public void Present(IntPtr window, Point location)
        {
            var target = new NativePoint { X = location.X, Y = location.Y };
            var source = new NativePoint();
            var area = new NativeSize { Width = size.Width, Height = size.Height };
            var blend = new Blend { Opacity = 255, AlphaFormat = 1 };
            if (!UpdateLayeredWindow(window, IntPtr.Zero, ref target, ref area, dc, ref source, 0, ref blend, 2))
                throw new Win32Exception();
        }

        public void Dispose()
        {
            if (Bitmap != null) { Bitmap.Dispose(); Bitmap = null; }
            if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(dc, previous);
            if (dib != IntPtr.Zero) { DeleteObject(dib); dib = IntPtr.Zero; }
            if (dc != IntPtr.Zero) { DeleteDC(dc); dc = IntPtr.Zero; }
            previous = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BitmapInfo
        {
            public uint HeaderSize;
            public int Width, Height;
            public ushort Planes, BitCount;
            public uint Compression, ImageSize;
            public int PelsX, PelsY;
            public uint ColorCount, ImportantColors;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Blend { public byte Operation, Flags, Opacity, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)]
        struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        struct NativeSize { public int Width, Height; }

        [DllImport("gdi32.dll", SetLastError = true)]
        static extern IntPtr CreateCompatibleDC(IntPtr source);
        [DllImport("gdi32.dll", SetLastError = true)]
        static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo header, uint usage, out IntPtr pixels,
            IntPtr section, uint offset);
        [DllImport("gdi32.dll", SetLastError = true)]
        static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
        [DllImport("gdi32.dll")]
        static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr item);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destDC, ref NativePoint dest, ref NativeSize size,
            IntPtr sourceDC, ref NativePoint source, uint colorKey, ref Blend blend, uint flags);
    }
}
