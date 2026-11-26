"""Annotation geometry in pixels of the original capture."""

from dataclasses import dataclass
from math import hypot

import cairo
from gi.repository import Pango, PangoCairo

BLUE = (23 / 255, 80 / 255, 190 / 255)
ORANGE = (209 / 255, 74 / 255, 19 / 255)
ANCHORS = tuple((x, y) for y in (-1, 0, 1) for x in (-1, 0, 1) if (x, y) != (0, 0))


@dataclass
class Style:
    color: tuple = BLUE
    stroke: int = 4
    head: int = 16
    size: int = 22
    bold: bool = False


@dataclass
class Mark:
    kind: str
    start: tuple
    end: tuple
    style: Style
    text: str = ""

    @property
    def bounds(self):
        return bounds(self.start, self.end)

    @property
    def valid(self):
        if self.kind == "text":
            return bool(self.text)
        if self.kind == "rectangle":
            x1, y1, x2, y2 = self.bounds
            return x1 < x2 and y1 < y2
        return self.start != self.end

    def handles(self):
        if self.kind in ("line", "arrow"):
            return ((0, self.start), (1, self.end))
        if self.kind == "rectangle":
            x1, y1, x2, y2 = self.bounds
            return tuple(((x, y), (x1 + (x + 1) * (x2 - x1) / 2, y1 + (y + 1) * (y2 - y1) / 2))
                         for x, y in ANCHORS)
        return ()


def clamp(value, minimum, maximum):
    return min(maximum, max(minimum, value))


def bounds(start, end):
    return min(start[0], end[0]), min(start[1], end[1]), max(start[0], end[0]), max(start[1], end[1])


def fit(image_width, image_height, view_width, view_height, padding=8):
    scale = min(max(0, view_width - 2 * padding) / image_width,
                max(0, view_height - 2 * padding) / image_height)
    return (view_width - image_width * scale) / 2, (view_height - image_height * scale) / 2, scale


def resize(rectangle, handle, point, width, height):
    x1, y1, x2, y2 = rectangle
    x = clamp(point[0], 0, width)
    y = clamp(point[1], 0, height)
    hx, hy = handle
    return (min(x, x2 - 1) if hx < 0 else x1,
            min(y, y2 - 1) if hy < 0 else y1,
            max(x, x1 + 1) if hx > 0 else x2,
            max(y, y1 + 1) if hy > 0 else y2)


def closest_handle(handles, point, radius):
    nearby = [(hypot(point[0] - x, point[1] - y), item) for item, (x, y) in handles
              if abs(point[0] - x) <= radius and abs(point[1] - y) <= radius]
    return min(nearby, key=lambda pair: pair[0])[1] if nearby else None


def arrow_geometry(tip, tail, style):
    """The user's first click fixes the tip, and dragging extends the tail."""
    dx, dy = tip[0] - tail[0], tip[1] - tail[1]
    length = hypot(dx, dy)
    if length == 0:
        return tail, (tip, tip, tip)
    ux, uy = dx / length, dy / length
    depth = min(length * 0.60, style.head)
    half = max(depth * 0.42, min(length * 0.42, style.stroke * 0.60))
    x, y = tip[0] - ux * depth, tip[1] - uy * depth
    neck = (x + ux * depth * 0.10, y + uy * depth * 0.10)
    return neck, (tip, (x - uy * half, y + ux * half), (x + uy * half, y - ux * half))


def text_layout(context, mark):
    font = Pango.FontDescription()
    font.set_family("Sans")
    font.set_absolute_size(mark.style.size * Pango.SCALE)
    font.set_weight(Pango.Weight.BOLD if mark.style.bold else Pango.Weight.NORMAL)
    layout = PangoCairo.create_layout(context)
    layout.set_font_description(font)
    layout.set_text(mark.text, -1)
    return layout


def text_size(mark):
    context = cairo.Context(cairo.RecordingSurface(cairo.CONTENT_COLOR_ALPHA, None))
    return text_layout(context, mark).get_pixel_size()


def hit(mark, point, tolerance):
    x, y = point
    x1, y1, x2, y2 = mark.bounds
    if mark.kind == "text":
        w, h = text_size(mark)
        return x1 - tolerance <= x <= x1 + w + tolerance and y1 - tolerance <= y <= y1 + h + tolerance
    tolerance += mark.style.stroke / 2
    if mark.kind == "rectangle":
        outer = x1 - tolerance <= x <= x2 + tolerance and y1 - tolerance <= y <= y2 + tolerance
        inner = x1 + tolerance < x < x2 - tolerance and y1 + tolerance < y < y2 - tolerance
        return outer and not inner
    ax, ay = mark.start
    dx, dy = mark.end[0] - ax, mark.end[1] - ay
    square = dx * dx + dy * dy
    if square == 0:
        return False
    t = clamp(((x - ax) * dx + (y - ay) * dy) / square, 0, 1)
    return hypot(x - ax - t * dx, y - ay - t * dy) <= tolerance


def paint(context, mark):
    if not mark.valid:
        return
    context.set_source_rgb(*mark.style.color)
    if mark.kind == "text":
        context.move_to(*mark.start)
        PangoCairo.show_layout(context, text_layout(context, mark))
        return
    context.set_line_width(mark.style.stroke)
    if mark.kind == "rectangle":
        x1, y1, x2, y2 = mark.bounds
        context.rectangle(x1, y1, x2 - x1, y2 - y1)
        context.stroke()
    elif mark.kind == "line":
        context.move_to(*mark.start)
        context.line_to(*mark.end)
        context.stroke()
    elif mark.kind == "arrow":
        neck, head = arrow_geometry(mark.start, mark.end, mark.style)
        width = hypot(head[1][0] - head[2][0], head[1][1] - head[2][1])
        context.set_line_width(min(mark.style.stroke, max(1, width)))
        context.move_to(*mark.end)
        context.line_to(*neck)
        context.stroke()
        context.move_to(*head[0])
        context.line_to(*head[1])
        context.line_to(*head[2])
        context.close_path()
        context.fill()
