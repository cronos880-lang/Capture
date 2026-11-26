using System;
using System.Drawing;

namespace Capture
{
    sealed class ToolbarLayout
    {
        public Rectangle Tools;
        public Rectangle Actions;
        public Rectangle Options;
        public int Height;

        public static ToolbarLayout Arrange(int width, int side, int vertical, int gap,
            Size tools, Size actions, Size[] arrow, Size[] text)
        {
            int contentWidth = Math.Max(1, width - 2 * side);
            int right = side + contentWidth;
            Size fullOptions = OptionsSize(arrow, text, int.MaxValue);
            if (tools.Width + gap + fullOptions.Width + gap + actions.Width <= contentWidth)
            {
                int rowHeight = Math.Max(Math.Max(tools.Height, actions.Height), fullOptions.Height);
                return new ToolbarLayout
                {
                    Tools = CenterY(side, vertical, tools, rowHeight),
                    Actions = CenterY(right - actions.Width, vertical, actions, rowHeight),
                    Options = CenterY(side + tools.Width + gap, vertical, fullOptions, rowHeight),
                    Height = rowHeight + 2 * vertical
                };
            }

            int top = vertical;
            var result = new ToolbarLayout();
            if (tools.Width + gap + actions.Width <= contentWidth)
            {
                int rowHeight = Math.Max(tools.Height, actions.Height);
                result.Tools = CenterY(side, top, tools, rowHeight);
                result.Actions = CenterY(right - actions.Width, top, actions, rowHeight);
                top += rowHeight + gap;
            }
            else
            {
                result.Tools = new Rectangle(side, top, tools.Width, tools.Height);
                top += tools.Height + gap;
                result.Actions = new Rectangle(Math.Max(side, right - actions.Width), top, actions.Width, actions.Height);
                top += actions.Height + gap;
            }

            Size wrappedOptions = OptionsSize(arrow, text, contentWidth);
            result.Options = new Rectangle(side, top, contentWidth, wrappedOptions.Height);
            result.Height = top + wrappedOptions.Height + vertical;
            return result;
        }

        static Rectangle CenterY(int x, int y, Size size, int rowHeight)
        {
            return new Rectangle(x, y + (rowHeight - size.Height) / 2, size.Width, size.Height);
        }

        static Size OptionsSize(Size[] arrow, Size[] text, int width)
        {
            Size a = MeasureRows(arrow, width);
            Size b = MeasureRows(text, width);
            return new Size(Math.Max(a.Width, b.Width), Math.Max(a.Height, b.Height));
        }

        static Size MeasureRows(Size[] items, int width)
        {
            int rowWidth = 0, rowHeight = 0, totalHeight = 0, widest = 0;
            foreach (Size item in items)
            {
                if (rowWidth > 0 && rowWidth + item.Width > width)
                {
                    widest = Math.Max(widest, rowWidth);
                    totalHeight += rowHeight;
                    rowHeight = rowWidth = 0;
                }
                rowWidth += item.Width;
                rowHeight = Math.Max(rowHeight, item.Height);
            }
            return new Size(Math.Max(widest, rowWidth), totalHeight + rowHeight);
        }
    }
}
