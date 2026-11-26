using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Capture
{
    static class Theme
    {
        public static readonly Color Ink = Color.FromArgb(233, 239, 246);
        public static readonly Color Muted = Color.FromArgb(184, 200, 215);
        public static readonly Color Blue = Color.FromArgb(23, 80, 190);
        public static readonly Color Accent = Color.FromArgb(116, 186, 251);
        public static readonly Color Primary = Color.FromArgb(19, 108, 190);
        public static readonly Color Background = Color.FromArgb(17, 24, 32);
        public static readonly Color Surface = Color.FromArgb(25, 35, 46);
        public static readonly Color ButtonSurface = Color.FromArgb(37, 56, 75);
        public static readonly Color Active = Color.FromArgb(27, 62, 95);
        public static readonly Color Border = Color.FromArgb(52, 67, 84);
        public static readonly Color ControlBorder = Color.FromArgb(77, 99, 119);

        public static Button Button(string text, bool primary = false)
        {
            var button = new Button
            {
                Text = text, AutoSize = true,
                MinimumSize = new Size(0, 32),
                Padding = new Padding(9, 0, 9, 0),
                Margin = new Padding(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                BackColor = primary ? Primary : ButtonSurface,
                ForeColor = primary ? Color.White : Ink
            };
            button.FlatAppearance.BorderColor = primary ? Primary : ControlBorder;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(21, 94, 161) : Active;
            return button;
        }

        public static Label Label(string text, bool muted = false)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = muted ? Muted : Ink, Margin = new Padding(0) };
        }

        public static void ApplyDarkTitleBar(IntPtr window)
        {
            if (Environment.OSVersion.Version.Build < 17763) return;
            const int immersiveDarkMode = 20;
            const int legacyImmersiveDarkMode = 19;
            int dark = 1;
            if (DwmSetWindowAttribute(window, immersiveDarkMode, ref dark, sizeof(int)) < 0)
                DwmSetWindowAttribute(window, legacyImmersiveDarkMode, ref dark, sizeof(int));
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }

    sealed class EditorForm : Form
    {
        readonly CaptureCanvas canvas = new CaptureCanvas();
        readonly Dictionary<Tool, Button> toolButtons = new Dictionary<Tool, Button>();
        readonly Button newCapture = Theme.Button("Nueva captura", true);
        readonly Button copy = Theme.Button("Copiar");
        readonly Button save = Theme.Button("Guardar PNG");
        readonly Label status = new Label
        {
            AutoEllipsis = true, Dock = DockStyle.Fill, ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(2, 4, 0, 0)
        };
        readonly Label widthLabel = Theme.Label("Grosor (px)");
        readonly Label headLabel = Theme.Label("Punta (px)");
        readonly Label textSizeLabel = Theme.Label("Texto (px)");
        readonly RowStyle toolbarRow = new RowStyle(SizeType.Absolute, 44);
        readonly RowStyle statusRow = new RowStyle(SizeType.Absolute, 23);
        readonly Panel toolbar = new Panel();
        readonly FlowLayoutPanel tools = CreateBar();
        readonly FlowLayoutPanel actions = CreateBar();
        readonly FlowLayoutPanel colors = CreateBar();
        readonly FlowLayoutPanel toolOptions = new FlowLayoutPanel();
        readonly NumericUpDown width = new NumericUpDown { Minimum = 1, Maximum = 80, Value = 4, Width = 62 };
        readonly NumericUpDown headSize = new NumericUpDown { Minimum = 6, Maximum = 180, Value = 16, Width = 62 };
        readonly NumericUpDown textSize = new NumericUpDown { Minimum = 8, Maximum = 200, Value = 22, Width = 62 };
        readonly CheckBox bold = new CheckBox { Text = "Negrita", AutoSize = true, Margin = new Padding(0, 7, 8, 3) };
        readonly Button otherColor = Theme.Button("Color…");
        readonly Button delete = Theme.Button("Eliminar");
        readonly ToolTip shortcuts = new ToolTip();
        string hotKeyLabel;
        string notice;
        bool syncing;
        bool arrangingToolbar;

        public event EventHandler CaptureRequested;
        public bool HasCapture { get { return canvas.HasImage; } }
        public bool HasUnsavedChanges { get { return canvas.Modified; } }
        public bool IsEditing { get { return canvas.IsDragging; } }

        public EditorForm(Icon icon)
        {
            Text = "Capture";
            Icon = icon;
            Font = SystemFonts.MessageBoxFont;
            BackColor = Theme.Background;
            ForeColor = Theme.Ink;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1080, 710);
            MinimumSize = new Size(690, 480);
            WindowState = FormWindowState.Maximized;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(toolbarRow);
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(statusRow);

            toolbar.Dock = DockStyle.Fill;
            toolbar.Margin = new Padding(0, 0, 0, 4);
            toolbar.BackColor = Theme.Surface;
            actions.Controls.Add(copy);
            actions.Controls.Add(save);
            actions.Controls.Add(newCapture);
            foreach (Tool tool in Enum.GetValues(typeof(Tool)))
            {
                var button = Theme.Button(ToolName(tool));
                button.Click += delegate { canvas.ChooseTool(tool); };
                tools.Controls.Add(button);
                toolButtons.Add(tool, button);
            }
            toolbar.Controls.Add(tools);
            toolbar.Controls.Add(actions);

            canvas.Dock = DockStyle.Fill;
            canvas.Margin = new Padding(0);
            canvas.EmptyClicked += delegate { RequestCapture(); };

            toolOptions.AutoSize = false;
            toolOptions.WrapContents = true;
            toolOptions.Margin = new Padding(0);
            colors.Margin = new Padding(0, 6, 4, 2);
            foreach (Color color in new[] { Theme.Blue, Color.FromArgb(209, 74, 19),
                Color.FromArgb(16, 132, 103), Color.FromArgb(119, 66, 181), Color.FromArgb(28, 42, 61) })
            {
                var swatch = Theme.Button("");
                swatch.AutoSize = false;
                swatch.MinimumSize = Size.Empty;
                swatch.Size = new Size(20, 20);
                swatch.Margin = new Padding(0, 0, 3, 0);
                swatch.Padding = new Padding(0);
                swatch.BackColor = color;
                swatch.FlatAppearance.MouseOverBackColor = ControlPaint.Light(color);
                swatch.AccessibleName = "Color " + ColorTranslator.ToHtml(color);
                swatch.Click += delegate { SetColor(color); };
                colors.Controls.Add(swatch);
            }
            toolOptions.Controls.Add(colors);
            otherColor.Click += ChooseColor;
            toolOptions.Controls.Add(otherColor);
            AddMeasurement(widthLabel, width, (style, value) => style.StrokeWidth = value);
            AddMeasurement(headLabel, headSize, (style, value) => style.ArrowHeadSize = value);
            AddMeasurement(textSizeLabel, textSize, (style, value) => style.TextSize = value);
            bold.CheckedChanged += delegate { if (!syncing) canvas.UpdateStyle(style => style.Bold = bold.Checked); };
            toolOptions.Controls.Add(bold);
            delete.Click += delegate { canvas.DeleteSelected(); };
            toolOptions.Controls.Add(delete);
            toolbar.Controls.Add(toolOptions);

            layout.Controls.Add(toolbar, 0, 0);
            layout.Controls.Add(canvas, 0, 1);
            layout.Controls.Add(status, 0, 2);
            Controls.Add(layout);
            toolbar.SizeChanged += delegate { ArrangeToolbar(); };
            toolbar.DpiChangedAfterParent += delegate { ArrangeToolbar(); };

            canvas.StateChanged += delegate { notice = null; RefreshState(); };
            newCapture.Click += delegate { RequestCapture(); };
            copy.Click += delegate { Export(false); };
            save.Click += delegate { Export(true); };
            RefreshState();
            ArrangeToolbar();
        }

        void AddMeasurement(Label label, NumericUpDown input, Action<AnnotationStyle, int> update)
        {
            var group = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                Margin = new Padding(3, 5, 8, 3)
            };
            label.Margin = new Padding(0, 3, 5, 0);
            input.Margin = new Padding(0);
            input.BackColor = Theme.ButtonSurface;
            input.ForeColor = Theme.Ink;
            input.AccessibleName = label.Text;
            input.ValueChanged += delegate
            {
                if (!syncing) canvas.UpdateStyle(style => update(style, (int)input.Value));
            };
            group.Controls.Add(label);
            group.Controls.Add(input);
            toolOptions.Controls.Add(group);
        }

        static FlowLayoutPanel CreateBar()
        {
            return new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false, Margin = new Padding(0)
            };
        }

        void ArrangeToolbar()
        {
            if (arrangingToolbar || toolbar.ClientSize.Width <= 0 || IsDisposed) return;
            arrangingToolbar = true;
            try
            {
                int dpi = toolbar.DeviceDpi;
                Size palette = OccupiedSize(colors);
                Size colorButton = OccupiedSize(otherColor);
                Size remove = OccupiedSize(delete);
                var arrow = new[] { palette, colorButton, OccupiedSize(width.Parent), OccupiedSize(headSize.Parent), remove };
                var text = new[] { palette, colorButton, OccupiedSize(textSize.Parent), OccupiedSize(bold), remove };

                var geometry = ToolbarLayout.Arrange(toolbar.ClientSize.Width, 8 * dpi / 96, 5 * dpi / 96, 6 * dpi / 96,
                    tools.PreferredSize, actions.PreferredSize, arrow, text);
                toolbarRow.Height = geometry.Height + toolbar.Margin.Vertical;
                statusRow.Height = Math.Max(23 * dpi / 96, status.Font.Height + status.Margin.Vertical);
                tools.Location = geometry.Tools.Location;
                actions.Location = geometry.Actions.Location;
                toolOptions.Bounds = geometry.Options;
            }
            finally { arrangingToolbar = false; }
        }

        static Size OccupiedSize(Control control)
        {
            Size size = control.AutoSize ? control.PreferredSize : control.Size;
            return size + control.Margin.Size;
        }

        static string ToolName(Tool tool)
        {
            switch (tool)
            {
                case Tool.Rectangle: return "Rectángulo";
                case Tool.Arrow: return "Flecha";
                case Tool.Line: return "Línea";
                case Tool.Text: return "Texto";
                default: return "Seleccionar";
            }
        }

        public void SetShortcutAvailable(bool available)
        {
            hotKeyLabel = available ? "Ctrl+Alt+R · Nueva captura" : "Nueva captura desde el botón o el área de notificación";
            shortcuts.SetToolTip(newCapture, hotKeyLabel);
            RefreshState();
        }

        void RequestCapture()
        {
            if (canvas.IsDragging) return;
            var handler = CaptureRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public void SetCapture(Bitmap bitmap)
        {
            canvas.SetImage(bitmap);
            canvas.Focus();
        }

        public bool ConfirmDiscard()
        {
            return !canvas.Modified || MessageBox.Show(this, "¿Descartar los cambios de la captura actual?",
                "Capture", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyDarkTitleBar(Handle);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (canvas.HasImage && !(ActiveControl is NumericUpDown))
            {
                if (keyData == (Keys.Control | Keys.C)) { Export(false); return true; }
                if (keyData == (Keys.Control | Keys.S)) { Export(true); return true; }
            }
            if (keyData == Keys.Escape && canvas.IsDragging) { canvas.CancelDrag(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void RefreshState()
        {
            // Keep the viewport fixed while mouse coordinates are driving a drag.
            if (canvas.IsDragging) return;
            bool hasImage = canvas.HasImage;
            foreach (var pair in toolButtons)
            {
                pair.Value.Enabled = hasImage;
                bool active = hasImage && canvas.ActiveTool == pair.Key;
                pair.Value.BackColor = active ? Theme.Active : Theme.ButtonSurface;
                pair.Value.FlatAppearance.BorderColor = active ? Theme.Accent : Theme.ControlBorder;
                pair.Value.ForeColor = active ? Theme.Accent : Theme.Ink;
            }

            var selected = canvas.Selected;
            var tool = canvas.StyleTool;
            var style = canvas.EditableStyle;
            toolOptions.SuspendLayout();
            width.Parent.Visible = style != null && tool != Tool.Text;
            headSize.Parent.Visible = tool == Tool.Arrow;
            textSize.Parent.Visible = bold.Visible = tool == Tool.Text;
            delete.Visible = selected != null;
            if (style != null)
            {
                syncing = true;
                width.Value = style.StrokeWidth;
                headSize.Value = style.ArrowHeadSize;
                textSize.Value = style.TextSize;
                bold.Checked = style.Bold;
                syncing = false;
                otherColor.FlatAppearance.BorderColor = style.Color;
            }
            toolOptions.Visible = style != null;
            toolOptions.ResumeLayout();
            copy.Enabled = save.Enabled = hasImage;
            string dimensions = hasImage ? string.Format("{0} × {1} px", canvas.ImageSize.Width, canvas.ImageSize.Height) : "";
            string shapeSize = MeasureShape(selected);
            string usage = canvas.ActiveTool == Tool.Arrow ? "Pulsa donde irá la punta y arrastra para extender el trazo."
                : canvas.Selected != null && canvas.Selected.IsSegment ? "Arrastra un extremo para ajustar el trazo." : hotKeyLabel;
            string message = notice ?? usage;
            string details = shapeSize.Length == 0 ? dimensions : dimensions + "    ·    " + shapeSize;
            status.Text = details.Length == 0 ? message : details + "    ·    " + message;
        }

        static string MeasureShape(Annotation shape)
        {
            if (shape == null || shape.Kind == Tool.Text) return "";
            var size = shape.Bounds.Size;
            if (shape.IsSegment)
            {
                var length = Math.Sqrt((double)size.Width * size.Width + (double)size.Height * size.Height);
                return string.Format("Longitud {0:0} px", length);
            }
            return string.Format("{0} × {1} px", size.Width, size.Height);
        }

        void ChooseColor(object sender, EventArgs e)
        {
            var style = canvas.EditableStyle;
            if (style == null) return;
            using (var dialog = new ColorDialog { Color = style.Color, FullOpen = true })
                if (dialog.ShowDialog(this) == DialogResult.OK) SetColor(dialog.Color);
        }

        void SetColor(Color color)
        {
            var style = canvas.EditableStyle;
            if (style != null && style.Color != color) canvas.UpdateStyle(current => current.Color = color);
        }

        void Export(bool toFile)
        {
            if (!canvas.HasImage || canvas.IsDragging) return;
            try
            {
                if (toFile)
                {
                    using (var dialog = new SaveFileDialog
                    {
                        Filter = "Imagen PNG (*.png)|*.png", FileName = "Captura.png", RestoreDirectory = true
                    })
                    {
                        if (dialog.ShowDialog(this) != DialogResult.OK) return;
                        using (Bitmap image = canvas.RenderImage()) image.Save(dialog.FileName, ImageFormat.Png);
                    }
                    notice = "Captura guardada";
                }
                else
                {
                    using (Bitmap image = canvas.RenderImage()) Clipboard.SetImage(image);
                    notice = "Captura copiada";
                }
                canvas.MarkExported();
                RefreshState();
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, "No se pudo exportar la captura", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) shortcuts.Dispose();
            base.Dispose(disposing);
        }
    }
}
