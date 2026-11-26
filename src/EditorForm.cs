using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Capture
{
    static class Theme
    {
        public static readonly Color Ink = Color.FromArgb(28, 42, 61);
        public static readonly Color Muted = Color.FromArgb(78, 95, 115);
        public static readonly Color Blue = Color.FromArgb(23, 80, 190);
        public static readonly Color Light = Color.FromArgb(239, 243, 248);
        public static readonly Color Border = Color.FromArgb(216, 225, 236);
        public static readonly Color Surface = Color.White;

        public static Button Button(string text, bool primary = false)
        {
            var button = new Button
            {
                Text = text, AutoSize = true, Height = 34,
                MinimumSize = new Size(0, 34),
                Padding = new Padding(10, 0, 10, 0),
                Margin = new Padding(0, 0, 7, 0),
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                BackColor = primary ? Blue : Surface,
                ForeColor = primary ? Color.White : Ink
            };
            button.FlatAppearance.BorderColor = primary ? Blue : Border;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(19, 67, 156) : Light;
            return button;
        }

        public static Label Label(string text, bool muted = false)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = muted ? Muted : Ink, Margin = new Padding(0) };
        }
    }

    sealed class EditorForm : Form
    {
        readonly CaptureCanvas canvas = new CaptureCanvas();
        readonly Dictionary<Tool, Button> toolButtons = new Dictionary<Tool, Button>();
        readonly Button newCapture = Theme.Button("Nueva captura", true);
        readonly Button copy = Theme.Button("Copiar");
        readonly Button save = Theme.Button("Guardar PNG");
        readonly Label status = Theme.Label("", true);
        readonly Label inspectorTitle = Theme.Label("Anotaciones");
        readonly Label inspectorHint = Theme.Label("Selecciona una anotación.", true);
        readonly Label widthLabel = Theme.Label("Grosor (px)");
        readonly FlowLayoutPanel inspectorControls = new FlowLayoutPanel();
        readonly NumericUpDown width = new NumericUpDown { Minimum = 1, Maximum = 30, Value = 4, Width = 74 };
        readonly Button otherColor = Theme.Button("Otro color…");
        readonly ToolTip shortcuts = new ToolTip();
        readonly Font inspectorFont;
        string hotKeyLabel;
        string notice;
        bool syncing;

        public event EventHandler CaptureRequested;
        public bool HasUnsavedChanges { get { return canvas.Modified; } }
        public bool IsEditing { get { return canvas.IsDragging; } }

        public EditorForm(Icon icon)
        {
            Text = "Capture";
            Icon = icon;
            Font = SystemFonts.MessageBoxFont;
            BackColor = Theme.Light;
            ForeColor = Theme.Ink;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1080, 710);
            MinimumSize = new Size(690, 480);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(16) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var commands = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 12),
                Padding = new Padding(10), BackColor = Theme.Surface
            };
            commands.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            commands.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var tools = CreateBar();
            tools.WrapContents = true;
            var actions = CreateBar();
            actions.FlowDirection = FlowDirection.RightToLeft;
            actions.Controls.Add(newCapture);
            actions.Controls.Add(save);
            actions.Controls.Add(copy);
            foreach (Tool tool in Enum.GetValues(typeof(Tool)))
            {
                var button = Theme.Button(ToolName(tool));
                button.Click += delegate { canvas.ChooseTool(tool); };
                tools.Controls.Add(button);
                toolButtons.Add(tool, button);
            }
            commands.Controls.Add(tools, 0, 0);
            commands.Controls.Add(actions, 1, 0);

            var workspace = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0)
            };
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 219));
            workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            canvas.Dock = DockStyle.Fill;
            canvas.Margin = new Padding(0, 0, 12, 0);
            canvas.EmptyClicked += delegate { RequestCapture(); };

            var inspector = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(16),
                Margin = new Padding(0), FlowDirection = FlowDirection.TopDown, WrapContents = false
            };
            inspectorFont = new Font(Font, FontStyle.Bold);
            inspectorTitle.Font = inspectorFont;
            inspectorTitle.Margin = new Padding(0, 0, 0, 16);
            inspector.Controls.Add(inspectorTitle);
            inspector.Controls.Add(inspectorHint);
            inspectorControls.AutoSize = true;
            inspectorControls.FlowDirection = FlowDirection.TopDown;
            inspectorControls.WrapContents = false;
            inspectorControls.Margin = new Padding(0);
            inspectorControls.Controls.Add(Theme.Label("Color"));
            var colors = CreateBar();
            colors.Padding = new Padding(0, 8, 0, 4);
            foreach (Color color in new[] { Theme.Blue, Color.FromArgb(209, 74, 19),
                Color.FromArgb(16, 132, 103), Color.FromArgb(119, 66, 181), Theme.Ink })
            {
                var swatch = Theme.Button("");
                swatch.AutoSize = false;
                swatch.MinimumSize = Size.Empty;
                swatch.Size = new Size(24, 24);
                swatch.Padding = new Padding(0);
                swatch.BackColor = color;
                swatch.FlatAppearance.MouseOverBackColor = ControlPaint.Light(color);
                swatch.AccessibleName = "Color " + ColorTranslator.ToHtml(color);
                swatch.Click += delegate { if (canvas.Selected != null) canvas.SetSelectedColor(color); };
                colors.Controls.Add(swatch);
            }
            inspectorControls.Controls.Add(colors);
            otherColor.Click += ChooseColor;
            inspectorControls.Controls.Add(otherColor);
            widthLabel.Margin = new Padding(0, 16, 0, 5);
            width.Margin = new Padding(0, 0, 0, 12);
            width.ValueChanged += delegate { if (!syncing) canvas.SetSelectedWidth((int)width.Value); };
            inspectorControls.Controls.Add(widthLabel);
            inspectorControls.Controls.Add(width);
            var delete = Theme.Button("Eliminar");
            delete.Click += delegate { canvas.DeleteSelected(); };
            inspectorControls.Controls.Add(delete);
            inspector.Controls.Add(inspectorControls);
            workspace.Controls.Add(canvas, 0, 0);
            workspace.Controls.Add(inspector, 1, 0);

            status.Margin = new Padding(1, 10, 0, 0);
            layout.Controls.Add(commands, 0, 0);
            layout.Controls.Add(workspace, 0, 1);
            layout.Controls.Add(status, 0, 2);
            Controls.Add(layout);

            canvas.StateChanged += delegate { notice = null; RefreshState(); };
            newCapture.Click += delegate { RequestCapture(); };
            copy.Click += delegate { Export(false); };
            save.Click += delegate { Export(true); };
            RefreshState();
        }

        static FlowLayoutPanel CreateBar()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false, Margin = new Padding(0)
            };
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

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !ConfirmDiscard()) e.Cancel = true;
            base.OnFormClosing(e);
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
            bool hasImage = canvas.HasImage;
            foreach (var pair in toolButtons)
            {
                pair.Value.Enabled = hasImage;
                bool active = hasImage && canvas.ActiveTool == pair.Key;
                pair.Value.BackColor = active ? Theme.Light : Theme.Surface;
                pair.Value.FlatAppearance.BorderColor = active ? Theme.Blue : Theme.Border;
                pair.Value.ForeColor = active ? Theme.Blue : Theme.Ink;
            }

            Annotation selected = canvas.Selected;
            inspectorTitle.Text = selected == null ? "Anotaciones" : ToolName(selected.Kind);
            inspectorHint.Visible = selected == null && hasImage;
            inspectorControls.Visible = selected != null;
            widthLabel.Visible = width.Visible = selected != null && selected.Kind != Tool.Text;
            if (selected != null)
            {
                syncing = true;
                width.Value = selected.Width;
                syncing = false;
                otherColor.FlatAppearance.BorderColor = selected.Color;
            }
            copy.Enabled = save.Enabled = hasImage;
            string dimensions = hasImage ? string.Format("{0} × {1} px", canvas.ImageSize.Width, canvas.ImageSize.Height) : "";
            string usage = canvas.ActiveTool == Tool.Arrow ? "Pulsa donde irá la punta y arrastra para extender el trazo."
                : canvas.Selected != null && canvas.Selected.IsSegment ? "Arrastra un extremo para ajustar el trazo." : hotKeyLabel;
            string message = notice ?? usage;
            status.Text = dimensions.Length == 0 ? message : dimensions + "    ·    " + message;
        }

        void ChooseColor(object sender, EventArgs e)
        {
            if (canvas.Selected == null) return;
            using (var dialog = new ColorDialog { Color = canvas.Selected.Color, FullOpen = true })
                if (dialog.ShowDialog(this) == DialogResult.OK) canvas.SetSelectedColor(dialog.Color);
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
            if (disposing)
            {
                shortcuts.Dispose();
                inspectorFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
