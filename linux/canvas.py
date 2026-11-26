"""GTK preview, mouse editing, and rendering at the capture's original size."""

from dataclasses import dataclass, replace
from math import floor

import cairo
from gi.repository import Gdk, Gtk

import annotations as drawing
from annotations import Mark, Style, bounds, clamp, closest_handle, fit, resize


@dataclass
class Drag:
    pointer: tuple
    start: tuple
    end: tuple
    handle: object = None
    creating: bool = False


class Canvas(Gtk.DrawingArea):
    def __init__(self, changed, ask_text):
        super().__init__()
        self.changed = changed
        self.ask_text = ask_text
        self.pixels = None
        self.marks = []
        self.selected = None
        self.drag = None
        self.tool = "select"
        self.modified = False
        self.defaults = {
            "rectangle": Style(), "arrow": Style(color=drawing.ORANGE), "line": Style(), "text": Style()
        }
        self.set_can_focus(True)
        self.add_events(Gdk.EventMask.BUTTON_PRESS_MASK | Gdk.EventMask.BUTTON_RELEASE_MASK |
                        Gdk.EventMask.POINTER_MOTION_MASK)
        for signal, handler in (("draw", self.draw), ("button-press-event", self.press),
                                ("motion-notify-event", self.move), ("button-release-event", self.release),
                                ("grab-broken-event", self.cancel_drag)):
            self.connect(signal, handler)

    @property
    def style(self):
        return self.selected.style if self.selected else self.defaults.get(self.tool)

    @property
    def style_kind(self):
        return self.selected.kind if self.selected else self.tool

    def set_image(self, pixels):
        self.pixels = pixels
        self.marks.clear()
        self.selected = None
        self.drag = None
        self.tool = "select"
        self.modified = False
        self.refresh()

    def set_tool(self, tool):
        self.cancel_drag()
        self.tool = tool
        self.selected = None
        self.refresh()
        self.grab_focus()

    def update_style(self, **values):
        style = self.style
        if not style or self.drag or all(getattr(style, field) == value for field, value in values.items()):
            return
        for field, value in values.items():
            setattr(style, field, value)
        if self.selected:
            self.modified = True
        self.refresh()

    def remove_selected(self):
        if self.selected is not None and self.drag is None:
            self.marks.remove(self.selected)
            self.selected = None
            self.modified = True
            self.refresh()

    def refresh(self):
        if self.get_window():
            name = "default" if self.tool == "select" else "crosshair"
            self.get_window().set_cursor(Gdk.Cursor.new_from_name(self.get_display(), name))
        self.queue_draw()
        self.changed()

    def view(self):
        return fit(self.pixels.get_width(), self.pixels.get_height(), self.get_allocated_width(),
                   self.get_allocated_height())

    def image_point(self, event):
        left, top, scale = self.view()
        if scale == 0:
            return None
        return floor((event.x - left) / scale), floor((event.y - top) / scale)

    def in_image(self, point):
        return 0 <= point[0] < self.pixels.get_width() and 0 <= point[1] < self.pixels.get_height()

    def begin_drag(self, point, handle=None, creating=False):
        self.drag = Drag(point, self.selected.start, self.selected.end, handle, creating)
        self.queue_draw()

    def press(self, _widget, event):
        if self.pixels is None or event.button != 1 or self.drag:
            return False
        point = self.image_point(event)
        if point is None:
            return False
        self.grab_focus()
        scale = self.view()[2]
        if self.tool == "select":
            handle = closest_handle(self.selected.handles(), point, 6 / scale) if self.selected else None
            if handle is not None:
                self.begin_drag(point, handle)
                return True
            self.selected = next((mark for mark in reversed(self.marks) if drawing.hit(mark, point, 5 / scale)),
                                 None) if self.in_image(point) else None
            if self.selected:
                self.begin_drag(point)
            else:
                self.refresh()
        elif self.in_image(point):
            if self.tool == "text":
                text = self.ask_text()
                if text:
                    self.selected = Mark("text", point, point, replace(self.defaults["text"]), text)
                    self.marks.append(self.selected)
                    self.tool = "select"
                    self.modified = True
                    self.refresh()
            else:
                self.selected = Mark(self.tool, point, point, replace(self.defaults[self.tool]))
                self.marks.append(self.selected)
                self.begin_drag(point, 1, True)
        return True

    def move(self, _widget, event):
        if not self.drag:
            return False
        point = self.image_point(event)
        if point is None:
            return True
        drag, mark = self.drag, self.selected
        dx, dy = point[0] - drag.pointer[0], point[1] - drag.pointer[1]
        width, height = self.pixels.get_width(), self.pixels.get_height()
        if isinstance(drag.handle, tuple):
            box = bounds(drag.start, drag.end)
            hx, hy = drag.handle
            x = box[0] if hx < 0 else box[2]
            y = box[1] if hy < 0 else box[3]
            rect = resize(box, drag.handle, (x + dx, y + dy), width - 1, height - 1)
            if rect == box:
                mark.start, mark.end = drag.start, drag.end
            else:
                mark.start, mark.end = rect[:2], rect[2:]
        elif drag.handle is not None:
            origin = drag.start if drag.handle == 0 else drag.end
            target = (clamp(origin[0] + dx, 0, width - 1), clamp(origin[1] + dy, 0, height - 1))
            if drag.handle == 0:
                mark.start = target
            else:
                mark.end = target
        else:
            x1, y1, x2, y2 = bounds(drag.start, drag.end)
            if mark.kind == "text":
                tw, th = drawing.text_size(mark)
                x2, y2 = x1 + tw - 1, y1 + th - 1
            dx = clamp(dx, -x1, max(-x1, width - 1 - x2))
            dy = clamp(dy, -y1, max(-y1, height - 1 - y2))
            mark.start = drag.start[0] + dx, drag.start[1] + dy
            mark.end = drag.end[0] + dx, drag.end[1] + dy
        self.queue_draw()
        return True

    def release(self, widget, event):
        if event.button != 1 or self.drag is None:
            return False
        self.move(widget, event)
        self.finish_drag(True)
        return True

    def cancel_drag(self, *_args):
        if self.drag:
            self.finish_drag(False)
            return True
        return False

    def finish_drag(self, commit):
        mark, drag = self.selected, self.drag
        self.drag = None
        if not commit or not mark.valid:
            if drag.creating:
                self.marks.remove(mark)
                self.selected = None
            else:
                mark.start, mark.end = drag.start, drag.end
        elif drag.creating or (mark.start, mark.end) != (drag.start, drag.end):
            self.modified = True
        if drag.creating:
            self.tool = "select"
        self.refresh()

    def paint_image(self, context):
        Gdk.cairo_set_source_pixbuf(context, self.pixels, 0, 0)
        context.paint()
        for mark in self.marks:
            drawing.paint(context, mark)

    def draw(self, widget, context):
        context.set_source_rgb(0.067, 0.094, 0.125)
        context.paint()
        if not self.pixels:
            return

        x, y, scale = self.view()
        if scale <= 0:
            return
        context.save()
        context.translate(x, y)
        context.scale(scale, scale)
        context.rectangle(0, 0, self.pixels.get_width(), self.pixels.get_height())
        context.clip()
        self.paint_image(context)
        context.restore()

        if self.selected and not self.drag:
            context.set_source_rgb(0.46, 0.73, 0.98)
            context.set_line_width(1.5)
            for _, (hx, hy) in self.selected.handles():
                context.rectangle(x + hx * scale - 4, y + hy * scale - 4, 8, 8)
                context.stroke_preserve()
                context.save()
                context.set_source_rgb(1, 1, 1)
                context.fill()
                context.restore()
            if self.selected.kind == "text":
                tw, th = drawing.text_size(self.selected)
                tx, ty = self.selected.start
                context.set_dash([5, 3])
                context.rectangle(x + tx * scale - 2, y + ty * scale - 2, tw * scale + 4, th * scale + 4)
                context.stroke()

    def render(self):
        result = cairo.ImageSurface(cairo.FORMAT_ARGB32, self.pixels.get_width(), self.pixels.get_height())
        self.paint_image(cairo.Context(result))
        return result
