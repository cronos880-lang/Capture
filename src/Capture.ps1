#Requires -Version 5.1
using namespace System.Drawing
using namespace System.Windows.Forms
using namespace System.Collections.Generic

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public static class CaptureDisplay {
    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    public static extern bool IsWindowEnabled(IntPtr window);
}

public sealed class CaptureOverlay : Form {
    public CaptureOverlay() {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
    }
}

public sealed class CaptureCanvas : Panel {
    public CaptureCanvas() {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }
}

public sealed class CaptureHotKey : NativeWindow, IDisposable {
    private const int HotKeyId = 1;
    private const int HotKeyMessage = 0x0312;
    private const uint ControlAltNoRepeat = 0x4003;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    public event EventHandler Pressed;
    public bool Registered { get; private set; }

    public CaptureHotKey() {
        var parameters = new CreateParams { Parent = new IntPtr(-3) }; // HWND_MESSAGE
        CreateHandle(parameters);
        Registered = RegisterHotKey(Handle, HotKeyId, ControlAltNoRepeat, (uint)Keys.R);
    }

    protected override void WndProc(ref Message message) {
        if (message.Msg == HotKeyMessage && message.WParam.ToInt32() == HotKeyId) {
            var handler = Pressed;
            if (handler != null) handler(this, EventArgs.Empty);
            return;
        }
        base.WndProc(ref message);
    }

    public void Dispose() {
        if (Registered) {
            UnregisterHotKey(Handle, HotKeyId);
            Registered = false;
        }
        DestroyHandle();
    }
}
'@ -ReferencedAssemblies System.Windows.Forms, System.Drawing -ErrorAction Stop
[void][CaptureDisplay]::SetProcessDpiAwarenessContext([IntPtr](-4))
[Application]::EnableVisualStyles()

$accent = [Color]::RoyalBlue
$font = [Font]::new('Segoe UI', 11)
$script:selection = [Rectangle]::Empty
$script:drag = $null

function Get-Rectangle($a, $b) {
    [Rectangle]::FromLTRB(
        [Math]::Min($a.X, $b.X), [Math]::Min($a.Y, $b.Y),
        [Math]::Max($a.X, $b.X), [Math]::Max($a.Y, $b.Y))
}

function New-Button($caption, $action) {
    $button = [Button]::new()
    $button.Text = $caption
    $button.AutoSize = $true
    $button.Add_Click($action)
    $button
}

function Get-Crop($desktop, $bounds) {
    $overlay = [CaptureOverlay]::new()
    $overlay.StartPosition = 'Manual'
    $overlay.FormBorderStyle = 'None'
    $overlay.Bounds = $bounds
    $overlay.TopMost = $true
    $overlay.ShowInTaskbar = $false
    $overlay.KeyPreview = $true
    $overlay.Font = $font

    $hint = [Label]::new()
    $hint.AutoSize = $true
    $hint.BackColor = [Color]::White
    $hint.Text = 'Arrastra para seleccionar un área'
    $hint.Location = [Point]::new(16, 16)
    $overlay.Controls.Add($hint)

    $actions = [FlowLayoutPanel]::new()
    $actions.AutoSize = $true
    $actions.WrapContents = $false
    $actions.BackColor = [Color]::White
    $actions.Visible = $false
    $size = [Label]::new()
    $size.AutoSize = $true
    $size.Margin = [Padding]::new(8, 8, 4, 4)
    $actions.Controls.Add($size)
    $actions.Controls.Add((New-Button 'Cancelar' { $overlay.DialogResult = 'Cancel' }))
    $actions.Controls.Add((New-Button 'Capturar' { $overlay.DialogResult = 'OK' }))
    $overlay.Controls.Add($actions)

    $overlay.Add_Paint({
        param($sender, $eventArgs)
        $clip = [Rectangle]::Intersect($eventArgs.ClipRectangle, $overlay.ClientRectangle)
        if ($clip.Width -eq 0 -or $clip.Height -eq 0) { return }
        $g = $eventArgs.Graphics
        $g.DrawImage($desktop, $clip, $clip, [GraphicsUnit]::Pixel)
        $r = $script:selection
        $shade = [SolidBrush]::new([Color]::FromArgb(150, 0, 0, 0))
        $region = [Region]::new($clip)
        $pen = [Pen]::new($accent, 2)
        try {
            $region.Exclude($r)
            $g.FillRegion($shade, $region)
            if ($r.Width -gt 0 -and $r.Height -gt 0) {
                $g.DrawRectangle($pen, $r)
                foreach ($handle in (Get-Handles $r)) {
                    $g.FillRectangle([Brushes]::White, $handle.Box)
                    $g.DrawRectangle($pen, $handle.Box)
                }
            }
        } finally { $pen.Dispose(); $region.Dispose(); $shade.Dispose() }
    })
    $overlay.Add_MouseDown({
        param($sender, $e)
        if ($e.Button -ne 'Left') { return }
        $handle = Get-Handles $script:selection | Where-Object { $_.Box.Contains($e.Location) } | Select-Object -First 1
        $script:drag = @{ Start = $e.Location; Rect = $script:selection; Handle = $handle }
        $hint.Hide()
        $actions.Hide()
        $overlay.Capture = $true
    })
    function Update-CropSelection($location) {
        $previous = $script:selection
        $x = [Math]::Max(0, [Math]::Min($overlay.ClientSize.Width, $location.X))
        $y = [Math]::Max(0, [Math]::Min($overlay.ClientSize.Height, $location.Y))
        $h = $script:drag.Handle
        if ($null -eq $h) {
            $script:selection = Get-Rectangle $script:drag.Start ([Point]::new($x, $y))
        } else {
            $r = $script:drag.Rect
            $left = $r.Left; $top = $r.Top; $right = $r.Right; $bottom = $r.Bottom
            if ($h.X -eq -1) { $left = [Math]::Min($x, $right - 1) }
            if ($h.X -eq 1) { $right = [Math]::Max($x, $left + 1) }
            if ($h.Y -eq -1) { $top = [Math]::Min($y, $bottom - 1) }
            if ($h.Y -eq 1) { $bottom = [Math]::Max($y, $top + 1) }
            $script:selection = [Rectangle]::FromLTRB($left, $top, $right, $bottom)
        }
        if (-not $previous.Equals($script:selection)) {
            $dirty = $script:selection
            if ($previous.Width -gt 0 -and $previous.Height -gt 0) {
                $dirty = [Rectangle]::Union($previous, $dirty)
            }
            $dirty.Inflate(7, 7)
            $overlay.Invalidate($dirty)
        }
    }
    $overlay.Add_MouseMove({
        param($sender, $e)
        if ($null -ne $script:drag) { Update-CropSelection $e.Location }
    })
    $overlay.Add_MouseUp({
        param($sender, $e)
        if ($e.Button -ne 'Left' -or $null -eq $script:drag) { return }
        Update-CropSelection $e.Location
        $script:drag = $null
        $overlay.Capture = $false
        $r = $script:selection
        if ($r.Width -eq 0 -or $r.Height -eq 0) { $hint.Show(); return }
        $size.Text = '{0} × {1} px' -f $r.Width, $r.Height
        $preferred = $actions.GetPreferredSize([Size]::Empty)
        $x = [Math]::Max(0, $r.Right - $preferred.Width)
        $y = $r.Bottom + 8
        if ($y + $preferred.Height -gt $overlay.ClientSize.Height) {
            $y = [Math]::Max(0, $r.Top - $preferred.Height - 8)
        }
        $actions.Location = [Point]::new($x, $y)
        $actions.Show()
    })
    $overlay.Add_KeyDown({
        param($sender, $e)
        if ($e.KeyCode -eq 'Escape') { $overlay.DialogResult = 'Cancel' }
        if ($e.KeyCode -eq 'Enter' -and $actions.Visible) { $overlay.DialogResult = 'OK' }
    })
    try {
        if ($overlay.ShowDialog() -eq 'OK') {
            $desktop.Clone($script:selection, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        }
    } finally { $overlay.Dispose() }
}

function Get-Handles($rect) {
    if ($rect.Width -eq 0 -or $rect.Height -eq 0) { return }
    foreach ($x in -1, 0, 1) {
        foreach ($y in -1, 0, 1) {
            if ($x -eq 0 -and $y -eq 0) { continue }
            $px = $rect.Left + [int](($x + 1) * $rect.Width / 2)
            $py = $rect.Top + [int](($y + 1) * $rect.Height / 2)
            @{ X = $x; Y = $y; Box = [Rectangle]::new($px - 5, $py - 5, 10, 10) }
        }
    }
}

function Get-ArrowShape($item) {
    $dx = $item.B.X - $item.A.X
    $dy = $item.B.Y - $item.A.Y
    $length = [Math]::Sqrt($dx * $dx + $dy * $dy)
    if ($length -eq 0) { return }
    $ux = $dx / $length
    $uy = $dy / $length
    $headLength = [Math]::Min($length * 0.6, [Math]::Max(10.0, 4.0 * $item.Width))
    $halfWidth = $headLength * 0.42
    $baseX = $item.B.X - $ux * $headLength
    $baseY = $item.B.Y - $uy * $headLength
    @{
        ShaftEnd = [PointF]::new($baseX + $ux * $headLength * 0.08, $baseY + $uy * $headLength * 0.08)
        Head = [PointF[]]@(
            [PointF]::new($item.B.X, $item.B.Y)
            [PointF]::new($baseX - $uy * $halfWidth, $baseY + $ux * $halfWidth)
            [PointF]::new($baseX + $uy * $halfWidth, $baseY - $ux * $halfWidth)
        )
    }
}

function Draw-Annotation($graphics, $item) {
    $pen = [Pen]::new($item.Color, $item.Width)
    $brush = [SolidBrush]::new($item.Color)
    try {
        switch ($item.Kind) {
            'Rectángulo' { $graphics.DrawRectangle($pen, (Get-Rectangle $item.A $item.B)) }
            'Flecha' {
                $arrow = Get-ArrowShape $item
                if ($null -eq $arrow) { break }
                $graphics.DrawLine($pen, [PointF]::new($item.A.X, $item.A.Y), $arrow.ShaftEnd)
                $graphics.FillPolygon($brush, $arrow.Head)
            }
            'Línea' { $graphics.DrawLine($pen, $item.A, $item.B) }
            'Texto' { $graphics.DrawString($item.Text, $annotationFont, $brush, [PointF]::new($item.A.X, $item.A.Y)) }
        }
    } finally { $pen.Dispose(); $brush.Dispose() }
}

function Get-AnnotationBounds($item) {
    if ($item.Kind -eq 'Texto') {
        $extent = [TextRenderer]::MeasureText($item.Text, $annotationFont)
        return [Rectangle]::new($item.A, $extent)
    }
    Get-Rectangle $item.A $item.B
}

function Test-AnnotationHit($item, $point) {
    if ($item.Kind -in 'Flecha', 'Línea') {
        $dx = $item.B.X - $item.A.X
        $dy = $item.B.Y - $item.A.Y
        $lengthSquared = $dx * $dx + $dy * $dy
        if ($lengthSquared -eq 0) { return $false }
        $fraction = (($point.X - $item.A.X) * $dx + ($point.Y - $item.A.Y) * $dy) / $lengthSquared
        $fraction = [Math]::Max(0.0, [Math]::Min(1.0, [double]$fraction))
        $distanceX = $point.X - ($item.A.X + $fraction * $dx)
        $distanceY = $point.Y - ($item.A.Y + $fraction * $dy)
        $tolerance = [Math]::Max(7, $item.Width * 2)
        return ($distanceX * $distanceX + $distanceY * $distanceY -le $tolerance * $tolerance)
    }
    $bounds = Get-AnnotationBounds $item
    $bounds.Inflate(5, 5)
    if ($item.Kind -eq 'Rectángulo' -and $bounds.Contains($point)) {
        $inner = Get-Rectangle $item.A $item.B
        $inner.Inflate(-5, -5)
        return -not $inner.Contains($point)
    }
    $bounds.Contains($point)
}

function Get-EndpointHandle($endpoint, $view) {
    [RectangleF]::new(
        $view.X + $endpoint.X * $view.Scale - 5,
        $view.Y + $endpoint.Y * $view.Scale - 5, 10, 10)
}

function Get-EndpointAt($item, $view, $location) {
    $closest = $null
    $distance = [double]::PositiveInfinity
    foreach ($end in 'B', 'A') {
        $box = Get-EndpointHandle $item[$end] $view
        if (-not $box.Contains($location.X, $location.Y)) { continue }
        $dx = $location.X - ($box.Left + 5)
        $dy = $location.Y - ($box.Top + 5)
        $candidate = $dx * $dx + $dy * $dy
        if ($candidate -lt $distance) {
            $closest = $end
            $distance = $candidate
        }
    }
    $closest
}

function Get-DraggedEndpoint($drag, $point, $imageSize) {
    $origin = $drag[$drag.Endpoint]
    [Point]::new(
        [Math]::Max(0, [Math]::Min($imageSize.Width - 1, $origin.X + $point.X - $drag.Start.X)),
        [Math]::Max(0, [Math]::Min($imageSize.Height - 1, $origin.Y + $point.Y - $drag.Start.Y)))
}

function Read-AnnotationText {
    $dialog = [Form]::new()
    $dialog.Text = 'Añadir texto'
    $dialog.StartPosition = 'CenterParent'
    $dialog.ClientSize = [Size]::new(360, 80)
    $dialog.Font = $font
    $inputBox = [TextBox]::new()
    $inputBox.Dock = 'Top'
    $accept = New-Button 'Añadir' { $dialog.DialogResult = 'OK' }
    $accept.Dock = 'Bottom'
    $dialog.Controls.Add($inputBox)
    $dialog.Controls.Add($accept)
    $dialog.AcceptButton = $accept
    $dialog.Add_KeyDown({ param($sender, $e) if ($e.KeyCode -eq 'Escape') { $dialog.DialogResult = 'Cancel' } })
    $dialog.KeyPreview = $true
    try {
        if ($dialog.ShowDialog($editor) -eq 'OK') { $inputBox.Text.Trim() }
    } finally { $dialog.Dispose() }
}

function Export-Capture($destination) {
    $bitmap = [Bitmap]$capture.Clone()
    $graphics = [Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = 'AntiAlias'
        foreach ($item in $items) { Draw-Annotation $graphics $item }
        if ($destination -eq 'Clipboard') {
            [Clipboard]::SetImage($bitmap)
            $status.Text = 'Captura copiada'
            $state.Modified = $false
        } else {
            $dialog = [SaveFileDialog]::new()
            $dialog.Filter = 'Imagen PNG (*.png)|*.png'
            $dialog.DefaultExt = 'png'
            $dialog.FileName = 'Captura.png'
            try {
                if ($dialog.ShowDialog($editor) -eq 'OK') {
                    $bitmap.Save($dialog.FileName, [System.Drawing.Imaging.ImageFormat]::Png)
                    $status.Text = 'Captura guardada'
                    $state.Modified = $false
                }
            } finally { $dialog.Dispose() }
        }
    } catch {
        [void][MessageBox]::Show($editor, $_.Exception.Message, 'Error de exportación')
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}

function Show-Editor($capture) {
    $editor = [Form]::new()
    $editor.Text = 'Captura y anota'
    $editor.StartPosition = 'CenterScreen'
    $editor.ClientSize = [Size]::new(1080, 700)
    $editor.MinimumSize = [Size]::new(820, 480)
    $editor.KeyPreview = $true
    $editor.Font = $font
    $annotationFont = [Font]::new('Segoe UI', 22, [FontStyle]::Regular, [GraphicsUnit]::Pixel)
    $items = [List[object]]::new()
    $state = @{ Tool = 'Seleccionar'; Selected = $null; Drag = $null; Synchronizing = $false; Modified = $false }

    $toolbar = [FlowLayoutPanel]::new()
    $toolbar.Dock = 'Top'
    $toolbar.Padding = [Padding]::new(8)
    $toolbar.Height = 52
    $toolbar.WrapContents = $false
    $tools = @{}
    foreach ($name in 'Seleccionar', 'Rectángulo', 'Flecha', 'Línea', 'Texto') {
        $button = New-Button $name {
            param($sender, $e)
            $state.Tool = $sender.Text
            $state.Selected = $null
            Update-Editor
        }
        $tools[$name] = $button
        $toolbar.Controls.Add($button)
    }
    $toolbar.Controls.Add((New-Button 'Copiar' { Export-Capture 'Clipboard' }))
    $toolbar.Controls.Add((New-Button 'Guardar' { Export-Capture 'File' }))
    $toolbar.Controls.Add((New-Button 'Nueva captura' { Request-Capture }))

    $status = [Label]::new()
    $status.Dock = 'Bottom'
    $status.Height = 27
    $status.Text = '{0} × {1} px' -f $capture.Width, $capture.Height

    $properties = [FlowLayoutPanel]::new()
    $properties.Dock = 'Right'
    $properties.Width = 190
    $properties.Padding = [Padding]::new(14)
    $properties.FlowDirection = 'TopDown'
    $properties.WrapContents = $false
    $title = [Label]::new()
    $title.AutoSize = $true
    $properties.Controls.Add($title)
    $colorButton = New-Button 'Color…' {
        $dialog = [ColorDialog]::new()
        $dialog.FullOpen = $true
        $dialog.Color = $state.Selected.Color
        try {
            if ($dialog.ShowDialog($editor) -eq 'OK') {
                $state.Selected.Color = $dialog.Color
                $state.Modified = $true
                Update-Editor
            }
        } finally { $dialog.Dispose() }
    }
    $properties.Controls.Add($colorButton)
    $widthLabel = [Label]::new()
    $widthLabel.Text = 'Grosor (px)'
    $widthLabel.AutoSize = $true
    $properties.Controls.Add($widthLabel)
    $width = [NumericUpDown]::new()
    $width.Minimum = 1
    $width.Maximum = 30
    $width.Value = 4
    $width.Add_ValueChanged({
        if ($null -eq $state.Selected -or $state.Synchronizing) { return }
        $state.Selected.Width = [int]$width.Value
        $state.Modified = $true
        $canvas.Invalidate()
    })
    $properties.Controls.Add($width)
    $delete = New-Button 'Eliminar' { Remove-SelectedAnnotation }
    $properties.Controls.Add($delete)

    $canvas = [CaptureCanvas]::new()
    $canvas.Dock = 'Fill'
    $canvas.BackColor = [Color]::White
    $editor.Controls.Add($canvas)
    $editor.Controls.Add($properties)
    $editor.Controls.Add($status)
    $editor.Controls.Add($toolbar)

    function Update-Editor {
        foreach ($name in $tools.Keys) {
            $tools[$name].BackColor = [SystemColors]::Control
        }
        $tools[$state.Tool].BackColor = [Color]::LightSteelBlue
        $item = $state.Selected
        $title.Text = 'Anotaciones'
        $colorButton.Visible = $null -ne $item
        $delete.Visible = $null -ne $item
        $width.Visible = $null -ne $item -and $item.Kind -ne 'Texto'
        $widthLabel.Visible = $null -ne $item -and $item.Kind -ne 'Texto'
        if ($null -ne $item) {
            $title.Text = $item.Kind
            $colorButton.BackColor = $item.Color
            $colorButton.ForeColor = [Color]::White
            if ($item.Color.GetBrightness() -gt 0.52) { $colorButton.ForeColor = [Color]::Black }
            $state.Synchronizing = $true
            $width.Value = $item.Width
            $state.Synchronizing = $false
        }
        $canvas.Invalidate()
    }

    function Remove-SelectedAnnotation {
        if ($null -eq $state.Selected) { return }
        [void]$items.Remove($state.Selected)
        $state.Selected = $null
        $state.Modified = $true
        Update-Editor
    }

    function Get-CanvasView {
        $scale = [Math]::Min(1.0, [Math]::Min(
            $canvas.ClientSize.Width / $capture.Width, $canvas.ClientSize.Height / $capture.Height))
        @{
            Scale = $scale
            X = ($canvas.ClientSize.Width - $capture.Width * $scale) / 2
            Y = ($canvas.ClientSize.Height - $capture.Height * $scale) / 2
        }
    }

    function Get-ImagePoint($location, $view = (Get-CanvasView)) {
        if ($view.Scale -le 0) { return }
        [Point]::new([int][Math]::Floor(($location.X - $view.X) / $view.Scale),
                     [int][Math]::Floor(($location.Y - $view.Y) / $view.Scale))
    }

    function Update-AnnotationDrag($location) {
        $point = Get-ImagePoint $location
        if ($null -eq $point) { return }
        $item = $state.Selected
        $drag = $state.Drag
        if ($drag.Endpoint) {
            $item[$drag.Endpoint] = Get-DraggedEndpoint $drag $point $capture.Size
        } else {
            $bounds = Get-AnnotationBounds $item
            $minX = $bounds.Left - ($item.A.X - $drag.A.X)
            $minY = $bounds.Top - ($item.A.Y - $drag.A.Y)
            $maxX = [Math]::Max(-$minX, $capture.Width - $minX - [Math]::Max(1, $bounds.Width))
            $maxY = [Math]::Max(-$minY, $capture.Height - $minY - [Math]::Max(1, $bounds.Height))
            $dx = [Math]::Max(-$minX, [Math]::Min($maxX, $point.X - $drag.Start.X))
            $dy = [Math]::Max(-$minY, [Math]::Min($maxY, $point.Y - $drag.Start.Y))
            $item.A = [Point]::new($drag.A.X + $dx, $drag.A.Y + $dy)
            $item.B = [Point]::new($drag.B.X + $dx, $drag.B.Y + $dy)
        }
        $canvas.Invalidate()
    }

    function Complete-AnnotationDrag($commit) {
        $drag = $state.Drag
        $item = $state.Selected
        $valid = -not $item.A.Equals($item.B)
        if ($item.Kind -eq 'Rectángulo') {
            $valid = $item.A.X -ne $item.B.X -and $item.A.Y -ne $item.B.Y
        } elseif ($item.Kind -eq 'Texto') {
            $valid = $true
        }

        if (-not $commit -or -not $valid) {
            if ($drag.Creating) {
                [void]$items.Remove($item)
                $state.Selected = $null
            } else {
                $item.A = $drag.A
                $item.B = $drag.B
            }
        } elseif ($drag.Creating -or -not $item.A.Equals($drag.A) -or -not $item.B.Equals($drag.B)) {
            $state.Modified = $true
        }
        if ($drag.Creating) { $state.Tool = 'Seleccionar' }
        $state.Drag = $null
        $canvas.Capture = $false
        Update-Editor
    }

    $canvas.Add_Paint({
        param($sender, $e)
        $view = Get-CanvasView
        if ($view.Scale -le 0) { return }
        $g = $e.Graphics
        $saved = $g.Save()
        $selected = $state.Selected
        $showHandles = $null -ne $selected -and $selected.Kind -in 'Línea', 'Flecha'
        try {
            $g.TranslateTransform([single]$view.X, [single]$view.Y)
            $g.ScaleTransform([single]$view.Scale, [single]$view.Scale)
            $g.SetClip([Rectangle]::new(0, 0, $capture.Width, $capture.Height))
            $g.DrawImage($capture, 0, 0, $capture.Width, $capture.Height)
            $g.SmoothingMode = 'AntiAlias'
            foreach ($item in $items) { Draw-Annotation $g $item }
            if ($null -ne $selected -and -not $showHandles) {
                $bounds = Get-AnnotationBounds $selected
                $bounds.Inflate(5, 5)
                $pen = [Pen]::new($accent, [single](1 / $view.Scale))
                try {
                    $pen.DashStyle = 'Dash'
                    $g.DrawRectangle($pen, $bounds)
                } finally { $pen.Dispose() }
            }
        } finally { $g.Restore($saved) }
        if ($showHandles -and $null -eq $state.Drag) {
            $pen = [Pen]::new($accent)
            try {
                foreach ($endpoint in $selected.A, $selected.B) {
                    $box = Get-EndpointHandle $endpoint $view
                    $g.FillRectangle([Brushes]::White, $box)
                    $g.DrawRectangle($pen, $box.X, $box.Y, $box.Width, $box.Height)
                }
            } finally { $pen.Dispose() }
        }
    })
    $canvas.Add_MouseDown({
        param($sender, $e)
        if ($e.Button -ne 'Left' -or $null -ne $state.Drag) { return }
        $view = Get-CanvasView
        $point = Get-ImagePoint $e.Location $view
        if ($null -eq $point) { return }

        $selected = $state.Selected
        if ($state.Tool -eq 'Seleccionar' -and $null -ne $selected -and $selected.Kind -in 'Línea', 'Flecha') {
            $endpoint = Get-EndpointAt $selected $view $e.Location
            if ($endpoint) {
                $state.Drag = @{ Start = $point; A = $selected.A; B = $selected.B; Endpoint = $endpoint; Creating = $false }
                $canvas.Capture = $true
                $canvas.Invalidate()
                return
            }
        }
        if ($point.X -lt 0 -or $point.Y -lt 0 -or $point.X -ge $capture.Width -or $point.Y -ge $capture.Height) {
            $state.Selected = $null
            Update-Editor
            return
        }
        if ($state.Tool -eq 'Seleccionar') {
            $state.Selected = $null
            for ($i = $items.Count - 1; $i -ge 0; $i--) {
                if (Test-AnnotationHit $items[$i] $point) {
                    $state.Selected = $items[$i]
                    $state.Drag = @{ Start = $point; A = $items[$i].A; B = $items[$i].B; Creating = $false }
                    break
                }
            }
        } else {
            $item = @{ Kind = $state.Tool; A = $point; B = $point; Width = 4; Color = $accent; Text = '' }
            if ($state.Tool -eq 'Texto') {
                $item.Text = Read-AnnotationText
                if ([string]::IsNullOrWhiteSpace($item.Text)) { return }
                $state.Tool = 'Seleccionar'
                $state.Modified = $true
            } else {
                if ($state.Tool -eq 'Flecha') { $item.Color = [Color]::DarkOrange }
                $state.Drag = @{ Start = $point; A = $point; B = $point; Endpoint = 'B'; Creating = $true }
            }
            $items.Add($item)
            $state.Selected = $item
        }
        if ($null -ne $state.Drag) { $canvas.Capture = $true }
        Update-Editor
    })
    $canvas.Add_MouseMove({
        param($sender, $e)
        if ($null -ne $state.Drag) { Update-AnnotationDrag $e.Location }
    })
    $canvas.Add_MouseUp({
        param($sender, $e)
        if ($e.Button -ne 'Left' -or $null -eq $state.Drag) { return }
        Update-AnnotationDrag $e.Location
        Complete-AnnotationDrag $true
    })
    $canvas.Add_MouseCaptureChanged({
        if ($null -ne $state.Drag -and -not $canvas.Capture) { Complete-AnnotationDrag $false }
    })
    $editor.Add_KeyDown({
        param($sender, $e)
        if ($e.KeyCode -eq 'Delete' -and $null -eq $state.Drag) { Remove-SelectedAnnotation }
        if ($e.KeyCode -eq 'Escape' -and $null -ne $state.Drag) { Complete-AnnotationDrag $false }
    })
    $editor.Add_FormClosing({
        param($sender, $e)
        if ($e.CloseReason -ne 'UserClosing' -or -not $state.Modified) { return }
        Restore-CaptureEditor
        $answer = [MessageBox]::Show($editor, '¿Descartar los cambios de la captura actual?', 'Capture',
            [MessageBoxButtons]::YesNo, [MessageBoxIcon]::Question, [MessageBoxDefaultButton]::Button2)
        if ($answer -eq 'Yes') { return }
        $e.Cancel = $true
        $appState.ExitRequested = $false
        if ($null -ne $appState.NextCapture) {
            $appState.NextCapture.Dispose()
            $appState.NextCapture = $null
        }
    })
    Update-Editor
    $appState.Editor = $editor
    try { [void]$editor.ShowDialog() }
    finally {
        $appState.Editor = $null
        $editor.Dispose()
        $annotationFont.Dispose()
    }
}

function Get-ScreenCapture {
    $script:selection = [Rectangle]::Empty
    $script:drag = $null
    $desktopBounds = [SystemInformation]::VirtualScreen
    $desktop = [Bitmap]::new($desktopBounds.Width, $desktopBounds.Height)
    try {
        $graphics = [Graphics]::FromImage($desktop)
        try {
            $graphics.CopyFromScreen($desktopBounds.Location, [Point]::Empty, $desktopBounds.Size)
        } finally { $graphics.Dispose() }
        Get-Crop $desktop $desktopBounds
    } finally { $desktop.Dispose() }
}

function Restore-CaptureEditor {
    if ($null -ne $appState.WindowState -and $null -ne $appState.Editor) {
        $appState.Editor.WindowState = $appState.WindowState
    }
    $appState.WindowState = $null
}

function Request-Capture {
    if ($appState.Selecting -or $captureTimer.Enabled -or $appState.ExitRequested -or
        $null -ne $appState.NextCapture) { return }
    $window = $appState.Editor
    if ($null -ne $window) {
        if (-not [CaptureDisplay]::IsWindowEnabled($window.Handle)) { return }
        $appState.WindowState = $window.WindowState
        $window.WindowState = 'Minimized'
    }
    $captureTimer.Start()
}

function Invoke-CaptureRequest {
    $captureTimer.Stop()
    $appState.Selecting = $true
    try {
        $next = Get-ScreenCapture
    } catch {
        [void][MessageBox]::Show($_.Exception.Message, 'Error de captura')
    } finally {
        $appState.Selecting = $false
    }
    if ($null -eq $next) {
        Restore-CaptureEditor
        return
    }
    if ($null -ne $appState.Editor) {
        $appState.NextCapture = $next
        $appState.Editor.Close()
        $appState.WindowState = $null
        return
    }

    while ($null -ne $next) {
        $appState.NextCapture = $null
        try { Show-Editor $next }
        finally { $next.Dispose() }
        $next = $appState.NextCapture
    }
    if ($appState.ExitRequested) { $context.ExitThread() }
}

$appState = @{ Editor = $null; WindowState = $null; NextCapture = $null; Selecting = $false; ExitRequested = $false }
$context = [ApplicationContext]::new()
$captureTimer = [Timer]::new()
$captureTimer.Interval = 250
$captureTimer.Add_Tick({ Invoke-CaptureRequest })

$hotKey = [CaptureHotKey]::new()
$hotKey.Add_Pressed({ Request-Capture })
$menu = [ContextMenuStrip]::new()
$captureMenu = $menu.Items.Add('Nueva captura')
$captureMenu.Add_Click({ Request-Capture })
if ($hotKey.Registered) {
    $captureMenu.ShortcutKeyDisplayString = 'Ctrl+Alt+R'
}
$exitMenu = $menu.Items.Add('Salir')
$exitMenu.Add_Click({
    if ($appState.Selecting -or $null -ne $appState.NextCapture) { return }
    $window = $appState.Editor
    if ($null -ne $window -and -not [CaptureDisplay]::IsWindowEnabled($window.Handle)) { return }
    $captureTimer.Stop()
    Restore-CaptureEditor
    $appState.ExitRequested = $true
    if ($null -ne $window) { $window.Close() }
    else { $context.ExitThread() }
})

$tray = [NotifyIcon]::new()
$tray.Icon = [SystemIcons]::Application
$tray.Text = 'Capture'
$tray.ContextMenuStrip = $menu
$tray.Add_DoubleClick({ Request-Capture })
try {
    $tray.Visible = $true
    $message = 'Abre el menú de Capture y elige Nueva captura.'
    if ($hotKey.Registered) {
        $tray.Text = 'Capture (Ctrl+Alt+R)'
        $message = 'Pulsa Ctrl+Alt+R para seleccionar una zona.'
    }
    $tray.ShowBalloonTip(3500, 'Capture listo', $message, [ToolTipIcon]::Info)
    [Application]::Run($context)
} catch {
    [void][MessageBox]::Show($_.Exception.Message, 'Capture')
    exit 1
} finally {
    $tray.Dispose()
    $menu.Dispose()
    $hotKey.Dispose()
    $captureTimer.Dispose()
    $context.Dispose()
    $font.Dispose()
}
