"""Select a region on X11 or use the desktop's screenshot portal on Wayland."""

import os
from uuid import uuid4

import cairo
from gi.repository import Gdk, GdkPixbuf, Gio, GLib, Gtk

from annotations import ANCHORS, bounds, clamp, closest_handle, resize

PORTAL = "org.freedesktop.portal.Desktop"
PORTAL_PATH = "/org/freedesktop/portal/desktop"
REQUEST = "org.freedesktop.portal.Request"


def select_region(application, completed):
    display = Gdk.Display.get_default()
    if os.environ.get("XDG_SESSION_TYPE") == "wayland" or display.__gtype__.name == "GdkWaylandDisplay":
        PortalCapture(completed).start()
        return

    display.sync()
    _, x, y = display.get_default_seat().get_pointer().get_position()
    monitor = display.get_monitor_at_point(x, y)
    monitor_index = next(index for index in range(display.get_n_monitors()) if display.get_monitor(index) == monitor)
    area = monitor.get_geometry()
    pixels = Gdk.pixbuf_get_from_window(Gdk.get_default_root_window(), area.x, area.y, area.width, area.height)
    if pixels is None:
        raise RuntimeError("X11 no ha devuelto una imagen de la pantalla.")
    RegionWindow(application, pixels, monitor_index, completed).show()


class PortalCapture:
    def __init__(self, completed):
        self.completed = completed
        self.bus = None
        self.subscription = 0
        self.request_path = None
        self.finished = False

    def start(self):
        try:
            self.bus = Gio.bus_get_sync(Gio.BusType.SESSION)
            token = "capture_" + uuid4().hex
            sender = self.bus.get_unique_name()[1:].replace(".", "_")
            self.expected = f"{PORTAL_PATH}/request/{sender}/{token}"
            self.subscription = self.bus.signal_subscribe(
                PORTAL, REQUEST, "Response", None, None, Gio.DBusSignalFlags.NONE, self.response)
            options = {"handle_token": GLib.Variant("s", token), "interactive": GLib.Variant("b", True)}
            self.bus.call(PORTAL, PORTAL_PATH, PORTAL + ".Screenshot", "Screenshot",
                          GLib.Variant("(sa{sv})", ("", options)), GLib.VariantType.new("(o)"),
                          Gio.DBusCallFlags.NONE, 15000, None, self.request_started)
        except GLib.Error as error:
            self.finish(error=f"El portal de capturas de Wayland no está disponible. {error.message}")

    def request_started(self, connection, result):
        try:
            self.request_path = connection.call_finish(result).unpack()[0]
        except GLib.Error as error:
            self.finish(error=f"El portal de capturas de Wayland no respondió. {error.message}")

    def response(self, _connection, _sender, path, _interface, _signal, parameters):
        if path not in (self.expected, self.request_path):
            return
        status, values = parameters.unpack()
        if status == 1:
            self.finish()
        elif status != 0:
            self.finish(error="El escritorio no pudo completar el recorte.")
        else:
            try:
                uri = values.get("uri", "")
                path = Gio.File.new_for_uri(uri).get_path() if uri else None
                if path is None:
                    raise ValueError("El portal no devolvió un archivo de imagen local.")
                self.finish(pixels=GdkPixbuf.Pixbuf.new_from_file(path))
            except (GLib.Error, ValueError) as error:
                self.finish(error=str(error))

    def finish(self, pixels=None, error=None):
        if self.finished:
            return
        self.finished = True
        if self.subscription:
            self.bus.signal_unsubscribe(self.subscription)
        self.completed(pixels, error)


class RegionWindow:
    def __init__(self, application, pixels, monitor_index, completed):
        self.pixels = pixels
        self.completed = completed
        self.width, self.height = pixels.get_width(), pixels.get_height()
        self.area = None
        self.initial = None
        self.start = None
        self.handle = None
        self.window = Gtk.ApplicationWindow(application=application, title="Recorte de Capture")
        self.window.set_decorated(False)
        self.window.set_keep_above(True)
        self.window.set_skip_taskbar_hint(True)
        self.window.connect("key-press-event", self.key)
        self.window.connect("delete-event", self.cancel)
        self.window.fullscreen_on_monitor(self.window.get_screen(), monitor_index)

        overlay = Gtk.Overlay()
        self.canvas = Gtk.DrawingArea()
        self.canvas.add_events(Gdk.EventMask.BUTTON_PRESS_MASK | Gdk.EventMask.BUTTON_RELEASE_MASK |
                               Gdk.EventMask.POINTER_MOTION_MASK)
        self.canvas.connect("draw", self.draw)
        self.canvas.connect("button-press-event", self.press)
        self.canvas.connect("motion-notify-event", self.move)
        self.canvas.connect("button-release-event", self.release)
        self.canvas.connect("realize", lambda widget: widget.get_window().set_cursor(
            Gdk.Cursor.new_from_name(widget.get_display(), "crosshair")))
        overlay.add(self.canvas)

        self.hint = Gtk.Label(label="Arrastra para seleccionar un área")
        self.hint.get_style_context().add_class("capture-hint")
        self.hint.set_halign(Gtk.Align.CENTER)
        self.hint.set_valign(Gtk.Align.START)
        self.hint.set_margin_top(18)
        overlay.add_overlay(self.hint)
        overlay.set_overlay_pass_through(self.hint, True)

        self.controls = Gtk.Box(spacing=8)
        self.controls.get_style_context().add_class("capture-toolbar")
        self.controls.set_halign(Gtk.Align.CENTER)
        self.controls.set_valign(Gtk.Align.END)
        self.controls.set_margin_bottom(12)
        self.controls.set_no_show_all(True)
        self.dimensions = Gtk.Label()
        self.controls.pack_start(self.dimensions, False, False, 4)
        for title, callback in (("Cancelar", self.cancel), ("Capturar", self.confirm)):
            button = Gtk.Button(label=title)
            if title == "Capturar":
                button.get_style_context().add_class("suggested-action")
            button.connect("clicked", callback)
            self.controls.pack_start(button, False, False, 0)
        for child in self.controls.get_children():
            child.show()
        overlay.add_overlay(self.controls)
        self.window.add(overlay)

    def show(self):
        self.window.show_all()
        self.window.present()

    def relative(self, point):
        size = self.canvas.get_allocation()
        return (clamp(round(point[0] * self.width / max(1, size.width)), 0, self.width),
                clamp(round(point[1] * self.height / max(1, size.height)), 0, self.height))

    def handles(self):
        if not self.area:
            return ()
        left, top, right, bottom = self.area
        return tuple(((x, y), (left + (x + 1) * (right - left) / 2,
                               top + (y + 1) * (bottom - top) / 2)) for x, y in ANCHORS)

    def draw(self, widget, context):
        size = widget.get_allocation()
        sx, sy = size.width / self.width, size.height / self.height
        context.scale(sx, sy)
        Gdk.cairo_set_source_pixbuf(context, self.pixels, 0, 0)
        context.paint()
        context.set_source_rgba(0.04, 0.07, 0.11, 0.56)
        context.rectangle(0, 0, self.width, self.height)
        if self.area:
            x1, y1, x2, y2 = self.area
            context.rectangle(x1, y1, x2 - x1, y2 - y1)
        context.set_fill_rule(cairo.FILL_RULE_EVEN_ODD)
        context.fill()
        if self.area:
            context.set_source_rgb(0.46, 0.73, 0.98)
            context.set_line_width(2 / sx)
            context.rectangle(x1, y1, x2 - x1, y2 - y1)
            context.stroke()
            for _, (x, y) in self.handles():
                context.rectangle(x - 3 / sx, y - 3 / sy, 6 / sx, 6 / sy)
            context.fill()

    def press(self, _widget, event):
        if event.button != 1:
            return False
        self.start = self.relative((event.x, event.y))
        self.initial = self.area
        radius = 8 * self.width / max(1, self.canvas.get_allocated_width())
        self.handle = closest_handle(self.handles(), self.start, radius)
        self.controls.hide()
        self.hint.hide()
        return True

    def move(self, _widget, event):
        if self.start is None:
            return False
        point = self.relative((event.x, event.y))
        if self.handle:
            hx, hy = self.handle
            edge_x = self.initial[0] if hx < 0 else self.initial[2]
            edge_y = self.initial[1] if hy < 0 else self.initial[3]
            target = edge_x + point[0] - self.start[0], edge_y + point[1] - self.start[1]
            self.area = resize(self.initial, self.handle, target, self.width, self.height)
        else:
            self.area = bounds(self.start, point)
        self.canvas.queue_draw()
        return True

    def release(self, widget, event):
        if event.button != 1 or self.start is None:
            return False
        self.move(widget, event)
        self.start = None
        x1, y1, x2, y2 = self.area
        if x2 > x1 and y2 > y1:
            self.dimensions.set_text(f"{x2 - x1} × {y2 - y1} px")
            self.controls.show()
        else:
            self.area = None
            self.hint.show()
            self.canvas.queue_draw()
        return True

    def key(self, _widget, event):
        if event.keyval == Gdk.KEY_Escape:
            return self.cancel()
        if event.keyval in (Gdk.KEY_Return, Gdk.KEY_KP_Enter):
            self.confirm()
            return True
        return False

    def cancel(self, *_args):
        self.window.destroy()
        self.completed(None, None)
        return True

    def confirm(self, *_args):
        if not self.area or self.start is not None:
            return
        x1, y1, x2, y2 = self.area
        if x2 <= x1 or y2 <= y1:
            return
        selected = self.pixels.new_subpixbuf(x1, y1, x2 - x1, y2 - y1).copy()
        # Transfer focus during the user's click or key event, before X11
        # restores the previously active fullscreen window.
        self.window.set_keep_above(False)
        GLib.idle_add(self.window.destroy)
        self.completed(selected, None)
