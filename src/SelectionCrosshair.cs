using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace Capture
{
    sealed class SelectionCrosshair : IDisposable
    {
        const int Size = 32;
        const int Center = Size / 2;
        readonly CursorHandle handle;

        public Cursor Cursor { get; private set; }

        public SelectionCrosshair()
        {
            byte[] andMask, xorMask;
            BuildMasks(out andMask, out xorMask);
            handle = CreateCursor(IntPtr.Zero, Center, Center, Size, Size, andMask, xorMask);
            if (handle.IsInvalid)
            {
                Cursor = Cursors.Cross;
                return;
            }

            try { Cursor = new Cursor(handle.DangerousGetHandle()); }
            catch { handle.Dispose(); throw; }
        }

        internal static void BuildMasks(out byte[] andMask, out byte[] xorMask)
        {
            const int stride = Size / 8;
            andMask = new byte[stride * Size];
            xorMask = new byte[stride * Size];

            // Windows displays (AND, XOR) as (1, 0) transparent, (0, 0) black, (0, 1) white.
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int dx = Math.Abs(x - Center), dy = Math.Abs(y - Center);
                    bool outline = Math.Min(dx, dy) <= 3 && Math.Max(dx, dy) <= 13;
                    bool white = Math.Min(dx, dy) <= 1 && Math.Max(dx, dy) <= 11;
                    int offset = y * stride + x / 8;
                    byte bit = (byte)(0x80 >> (x % 8));
                    if (!outline) andMask[offset] |= bit;
                    if (white) xorMask[offset] |= bit;
                }
            }
        }

        public void Dispose()
        {
            if (!handle.IsInvalid && !handle.IsClosed) Cursor.Dispose();
            handle.Dispose();
        }

        [DllImport("user32.dll")]
        static extern CursorHandle CreateCursor(IntPtr module, int hotX, int hotY, int width, int height,
            byte[] andMask, byte[] xorMask);

        sealed class CursorHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            CursorHandle() : base(true) { }

            protected override bool ReleaseHandle() { return DestroyCursor(handle); }

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            static extern bool DestroyCursor(IntPtr cursor);
        }
    }
}
