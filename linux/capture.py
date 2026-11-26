#!/usr/bin/env python3
"""Capture's GTK desktop application."""

import io
import os
from pathlib import Path
import sys
from tempfile import NamedTemporaryFile

try:
    import cairo
    import gi
    gi.require_version("Gtk", "3.0")
    gi.require_version("Gdk", "3.0")
    gi.require_version("Pango", "1.0")
    gi.require_version("PangoCairo", "1.0")
    gi.require_foreign("cairo")
except (ImportError, ValueError) as error:
    sys.exit(f"Capture requiere GTK 3 y los paquetes python3-gi y python3-gi-cairo. {error}")

from gi.repository import Gdk, GdkPixbuf, Gio, GLib, Gtk, Pango

from annotations import BLUE, ORANGE
from canvas import Canvas
from screen import select_region

APP_ID = "io.github.cronos880_lang.Capture"
FOLDER = Path(__file__).resolve().parent
ICON = FOLDER / "capture.png"
TOOLS = {"select": "Seleccionar", "rectangle": "Rectángulo", "arrow": "Flecha",
         "line": "Línea", "text": "Texto"}
PALETTE = {
    "Azul": BLUE, "Naranja": ORANGE, "Verde": (0.06, 0.52, 0.40),
    "Morado": (0.47, 0.26, 0.71), "Negro": (0.11, 0.16, 0.24),
}
CSS = b"""
window.capture-root { background-color: #111820; color: #e9eff6; }
.capture-toolbar, .capture-hint { background-color: #19232e; color: #e9eff6; padding: 6px 9px; }
.capture-toolbar button { background-image: none; background-color: #25384b; color: #e9eff6;
  border: 1px solid #4d6377; padding: 4px 9px; min-height: 24px; }
.capture-toolbar button:hover, .capture-toolbar button:checked { background-color: #1b3e5f; }
.capture-toolbar button:checked { border-color: #74bafb; color: #c9e4ff; }
.capture-toolbar button.suggested-action { background-color: #136cbe; color: white; }
.capture-toolbar button:disabled { background-color: #19232e; color: #899aab; }
.capture-status { color: #b8c8d7; padding: 4px 10px; }
.capture-options { padding: 10px; }
"""


def execute_argument(value):
    # Desktop Entry Exec quoting has a field-code pass and a string-escape pass.
    if "\n" in value or "\r" in value:
        raise ValueError("La ruta de Capture contiene un salto de línea.")
    argument = value.replace("%", "%%")
    for character in ('\\', '"', '`', '$'):
        argument = argument.replace(character, '\\' + character)
    return '"' + argument.replace('\\', '\\\\') + '"'


def xdg_path(variable, fallback):
    value = os.environ.get(variable, "")
    path = Path(value)
    return path if path.is_absolute() else Path.home() / fallback


def launcher_path(startup=False):
    directory = (xdg_path("XDG_CONFIG_HOME", ".config") / "autostart" if startup else
                 xdg_path("XDG_DATA_HOME", ".local/share") / "applications")
    return directory / (APP_ID + ".desktop")


def install_launcher(startup=False):
    dest = launcher_path(startup)
    icon = str(ICON).replace("\\", "\\\\")
    command = execute_argument(sys.executable) + " -B " + execute_argument(str(FOLDER / "capture.py"))
    command += " --inicio" if startup else " --recortar"
    dest.parent.mkdir(parents=True, exist_ok=True)
    content = ("[Desktop Entry]\nType=Application\nName=Capture\nComment=Captura y explica una zona de la pantalla\n"
               f"Exec={command}\nIcon={icon}\nTerminal=false\nCategories=Graphics;Utility;\nStartupWMClass={APP_ID}\n")
    # Replace the user's Capture shortcut as one operation.
    with NamedTemporaryFile(dir=dest.parent, mode="w", encoding="utf-8", delete=False) as stream:
        tmp = Path(stream.name)
        stream.write(content)
    try:
        tmp.replace(dest)
    finally:
        tmp.unlink(missing_ok=True)
    return dest


def action(label, handler, primary=False):
    button = Gtk.Button(label=label)
    if primary:
        button.get_style_context().add_class("suggested-action")
    button.connect("clicked", handler)
    return button


class Editor(Gtk.ApplicationWindow):
    def __init__(self, app):
        super().__init__(application=app, title="Capture")
        self.app = app
        self.syncing = False
        self.notice = ""
        self.set_default_size(1050, 700)
        self.get_style_context().add_class("capture-root")
        self.connect("destroy", lambda *_args: app.quit())
        self.connect("key-press-event", self.key)
        self.canvas = Canvas(self.update_state, self.prompt_text)

        column = Gtk.Box(orientation=Gtk.Orientation.VERTICAL)
        commands = Gtk.Box(spacing=6)
        commands.get_style_context().add_class("capture-toolbar")
        self.tools = {}
        for kind, label in TOOLS.items():
            button = Gtk.ToggleButton(label=label)
            button.connect("toggled", self.tool_changed, kind)
            self.tools[kind] = button
            commands.pack_start(button, False, False, 0)

        self.options = Gtk.MenuButton(label="Ajustes")
        self.options.set_popover(self.make_options())
        commands.pack_start(self.options, False, False, 5)
        self.copy_button = action("Copiar", self.copy)
        self.save_button = action("Guardar PNG", self.save)
        menu = Gtk.MenuButton()
        menu.set_image(Gtk.Image.new_from_icon_name("open-menu-symbolic", Gtk.IconSize.BUTTON))
        menu.get_accessible().set_name("Menú de Capture")
        menu.set_popup(self.make_menu())
        commands.pack_end(menu, False, False, 0)
        commands.pack_end(action("Nueva captura", lambda *_: app.request_capture(), True), False, False, 0)
        commands.pack_end(self.save_button, False, False, 0)
        commands.pack_end(self.copy_button, False, False, 0)
        column.pack_start(commands, False, False, 0)

        overlay = Gtk.Overlay()
        overlay.add(self.canvas)
        self.empty = action("Seleccionar una zona de la pantalla", lambda *_: app.request_capture())
        self.empty.set_halign(Gtk.Align.CENTER)
        self.empty.set_valign(Gtk.Align.CENTER)
        overlay.add_overlay(self.empty)
        column.pack_start(overlay, True, True, 0)

        self.status = Gtk.Label(xalign=0)
        self.status.set_ellipsize(Pango.EllipsizeMode.END)
        self.status.get_style_context().add_class("capture-status")
        column.pack_end(self.status, False, False, 0)
        self.add(column)

    def display(self):
        self.show_all()
        self.update_state()
        timestamp = Gtk.get_current_event_time()
        if timestamp:
            self.present_with_time(timestamp)
        else:
            self.present()

    def make_options(self):
        popover = Gtk.Popover()
        settings = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
        settings.get_style_context().add_class("capture-options")
        palette = Gtk.Box(spacing=4)
        for name, color in PALETTE.items():
            swatch = Gtk.DrawingArea()
            swatch.set_size_request(19, 16)

            def draw_color(widget, context, rgb=color):
                context.set_source_rgb(*rgb)
                context.paint()
            swatch.connect("draw", draw_color)
            button = Gtk.Button()
            button.add(swatch)
            button.set_tooltip_text(name)
            button.get_accessible().set_name(name)
            button.connect("clicked", lambda _button, rgb=color: self.change_style(color=rgb))
            palette.pack_start(button, False, False, 0)
        settings.pack_start(palette, False, False, 0)
        self.color = Gtk.ColorButton()
        self.color.set_title("Color del trazo")
        self.color.connect("color-set", self.color_changed)
        settings.pack_start(self.settings_row("Color", self.color), False, False, 0)

        self.stroke, self.stroke_row = self.add_size(settings, "Grosor (px)", "stroke", 1, 80)
        self.head, self.head_row = self.add_size(settings, "Punta (px)", "head", 6, 180)
        self.size, self.size_row = self.add_size(settings, "Texto (px)", "size", 8, 200)
        self.bold = Gtk.CheckButton(label="Negrita")
        self.bold.connect("toggled", lambda widget: self.change_style(bold=widget.get_active()))
        settings.pack_start(self.bold, False, False, 0)
        self.remove = action("Eliminar", lambda *_: self.canvas.remove_selected())
        settings.pack_start(self.remove, False, False, 0)
        settings.show_all()
        popover.add(settings)
        return popover

    @staticmethod
    def settings_row(label, field):
        row = Gtk.Box(spacing=14)
        title = Gtk.Label(label=label, xalign=0)
        title.set_mnemonic_widget(field)
        row.pack_start(title, True, True, 0)
        row.pack_end(field, False, False, 0)
        return row

    def add_size(self, settings, label, field, low, high):
        spin = Gtk.SpinButton.new_with_range(low, high, 1)
        spin.set_numeric(True)
        spin.connect("value-changed", lambda widget: self.change_style(**{field: widget.get_value_as_int()}))
        row = self.settings_row(label, spin)
        settings.pack_start(row, False, False, 0)
        return spin, row

    def make_menu(self):
        menu = Gtk.Menu()
        launcher = Gtk.MenuItem(label="Añadir al menú de aplicaciones")
        launcher.connect("activate", self.add_launcher)
        menu.append(launcher)
        startup = Gtk.CheckMenuItem(label="Iniciar al entrar en Linux")
        startup.set_active(launcher_path(True).is_file())
        startup.connect("toggled", self.set_startup)
        menu.append(startup)
        menu.show_all()
        return menu

    def add_launcher(self, *_args):
        try:
            install_launcher()
            self.message("Capture ya aparece en el menú de aplicaciones. Puedes añadirlo a los favoritos de tu escritorio.")
        except (OSError, ValueError) as error:
            self.message(str(error), True)

    def set_startup(self, item):
        if self.syncing:
            return
        try:
            if item.get_active():
                install_launcher(True)
            else:
                launcher_path(True).unlink(missing_ok=True)
        except (OSError, ValueError) as error:
            self.syncing = True
            item.set_active(launcher_path(True).is_file())
            self.syncing = False
            self.message(str(error), True)

    def message(self, text, error=False):
        dialog = Gtk.MessageDialog(transient_for=self, modal=True, buttons=Gtk.ButtonsType.OK,
                                   message_type=Gtk.MessageType.ERROR if error else Gtk.MessageType.INFO, text=text)
        dialog.run()
        dialog.destroy()

    def tool_changed(self, button, kind):
        if self.syncing:
            return
        if button.get_active():
            self.notice = ""
            self.canvas.set_tool(kind)
        elif self.canvas.tool == kind:
            self.update_state()

    def color_changed(self, button):
        color = button.get_rgba()
        self.change_style(color=(color.red, color.green, color.blue))

    def change_style(self, **values):
        if not self.syncing:
            self.canvas.update_style(**values)

    def update_state(self):
        has_image = self.canvas.pixels is not None
        self.copy_button.set_sensitive(has_image)
        self.save_button.set_sensitive(has_image)
        self.empty.set_visible(not has_image)
        style, kind = self.canvas.style, self.canvas.style_kind
        self.options.set_sensitive(bool(style))
        self.syncing = True
        try:
            for name, button in self.tools.items():
                button.set_sensitive(has_image)
                button.set_active(name == self.canvas.tool)
            if style:
                self.color.set_rgba(Gdk.RGBA(*style.color, 1))
                self.stroke.set_value(style.stroke)
                self.head.set_value(style.head)
                self.size.set_value(style.size)
                self.bold.set_active(style.bold)
            self.stroke_row.set_visible(bool(style) and kind != "text")
            self.head_row.set_visible(kind == "arrow")
            self.size_row.set_visible(kind == "text")
            self.bold.set_visible(kind == "text")
            self.remove.set_visible(self.canvas.selected is not None)
        finally:
            self.syncing = False
        if has_image:
            pixels = self.canvas.pixels
            status = f"{pixels.get_width()} × {pixels.get_height()} px"
            if self.canvas.tool == "arrow":
                status += "  ·  Pulsa donde irá la punta y arrastra para extender el trazo."
            if self.notice:
                status += "  ·  " + self.notice
        else:
            status = "Capture"
        self.status.set_text(status)

    def prompt_text(self):
        dialog = Gtk.Dialog(title="Añadir texto", transient_for=self, modal=True)
        dialog.add_button("Cancelar", Gtk.ResponseType.CANCEL)
        dialog.add_button("Añadir", Gtk.ResponseType.OK)
        dialog.set_default_response(Gtk.ResponseType.OK)
        field = Gtk.Entry(activates_default=True)
        field.set_width_chars(35)
        field.get_accessible().set_name("Texto de la anotación")
        dialog.get_content_area().pack_start(field, True, True, 12)
        field.show()
        field.grab_focus()
        answer = field.get_text().strip() if dialog.run() == Gtk.ResponseType.OK else ""
        dialog.destroy()
        return answer

    def confirm_discard(self):
        if not self.canvas.modified:
            return True
        dialog = Gtk.MessageDialog(transient_for=self, modal=True, buttons=Gtk.ButtonsType.YES_NO,
                                   message_type=Gtk.MessageType.QUESTION, text="¿Descartar los cambios de la captura actual?")
        dialog.set_default_response(Gtk.ResponseType.NO)
        answer = dialog.run() == Gtk.ResponseType.YES
        dialog.destroy()
        return answer

    def key(self, _widget, event):
        if event.keyval == Gdk.KEY_Escape and self.canvas.cancel_drag():
            return True
        if isinstance(self.get_focus(), Gtk.Editable):
            return False
        if event.keyval == Gdk.KEY_Delete:
            self.canvas.remove_selected()
            return True
        if event.state & Gdk.ModifierType.CONTROL_MASK:
            if event.keyval in (Gdk.KEY_c, Gdk.KEY_C):
                self.copy()
                return True
            if event.keyval in (Gdk.KEY_s, Gdk.KEY_S):
                self.save()
                return True
        return False

    def copy(self, *_args):
        if self.canvas.pixels is None or self.canvas.drag:
            return
        try:
            data = io.BytesIO()
            self.canvas.render().write_to_png(data)
            loader = GdkPixbuf.PixbufLoader.new_with_type("png")
            loader.write(data.getvalue())
            loader.close()
            clipboard = Gtk.Clipboard.get(Gdk.SELECTION_CLIPBOARD)
            clipboard.set_image(loader.get_pixbuf())
            clipboard.set_can_store(None)
            clipboard.store()
            self.mark_exported("Captura copiada")
        except (GLib.Error, OSError, cairo.Error) as error:
            self.message(str(error), True)

    def save(self, *_args):
        if self.canvas.pixels is None or self.canvas.drag:
            return
        dialog = Gtk.FileChooserNative.new("Guardar captura", self, Gtk.FileChooserAction.SAVE, "Guardar", "Cancelar")
        dialog.set_current_name("Captura.png")
        dialog.set_do_overwrite_confirmation(True)
        try:
            if dialog.run() != Gtk.ResponseType.ACCEPT:
                return
            self.canvas.render().write_to_png(dialog.get_filename())
            self.mark_exported("Captura guardada")
        except (OSError, cairo.Error) as error:
            self.message(str(error), True)
        finally:
            dialog.destroy()

    def mark_exported(self, notice):
        self.notice = notice
        self.canvas.modified = False
        self.update_state()


class CaptureApplication(Gtk.Application):
    def __init__(self):
        super().__init__(application_id=APP_ID, flags=Gio.ApplicationFlags.HANDLES_COMMAND_LINE)
        self.editor = None
        self.capturing = False
        self.restore = False
        self.resident = False

    def do_startup(self):
        Gtk.Application.do_startup(self)
        settings = Gtk.Settings.get_default()
        settings.set_property("gtk-application-prefer-dark-theme", True)
        provider = Gtk.CssProvider()
        provider.load_from_data(CSS)
        Gtk.StyleContext.add_provider_for_screen(Gdk.Screen.get_default(), provider, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
        Gtk.Window.set_default_icon_from_file(str(ICON))
        self.hold()

    def do_command_line(self, command_line):
        args = command_line.get_arguments()[1:]
        if args == ["--instalar-acceso"]:
            try:
                install_launcher()
                return 0
            except (OSError, ValueError) as error:
                print(error, file=sys.stderr)
                return 1
            finally:
                if not command_line.get_is_remote():
                    self.quit()
        if not args:
            if not self.capturing:
                self.show_editor()
        elif args == ["--recortar"]:
            self.request_capture()
        elif args == ["--inicio"]:
            self.resident = True
        else:
            print("Uso: Capture [--recortar | --inicio | --instalar-acceso]", file=sys.stderr)
            if not command_line.get_is_remote():
                self.quit()
            return 2
        return 0

    def show_editor(self):
        if self.editor is None:
            self.editor = Editor(self)
            self.editor.maximize()
        self.editor.display()
        return self.editor

    def request_capture(self):
        if self.capturing or (self.editor and self.editor.canvas.drag):
            return
        self.capturing = True
        self.restore = bool(self.editor and self.editor.get_visible())
        if self.restore:
            self.editor.hide()
        GLib.timeout_add(170, self.begin_capture)

    def begin_capture(self):
        try:
            select_region(self, self.finish_capture)
        except (GLib.Error, RuntimeError, StopIteration) as error:
            self.finish_capture(None, str(error) or "No se encontró una pantalla activa.")
        return GLib.SOURCE_REMOVE

    def finish_capture(self, pixels, error):
        try:
            if error:
                self.show_editor().message(error, True)
            elif pixels is None:
                if self.restore:
                    self.show_editor()
                elif self.editor is None and not self.resident:
                    self.quit()
            else:
                editor = self.show_editor()
                if editor.confirm_discard():
                    editor.notice = ""
                    editor.canvas.set_image(pixels)
                    editor.canvas.grab_focus()
        finally:
            self.capturing = False

if __name__ == "__main__":
    GLib.set_prgname(APP_ID)
    sys.exit(CaptureApplication().run(sys.argv))
