﻿using Chatterino.Common;
using SharpDX.Mathematics.Interop;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Message = Chatterino.Common.Message;
using SCB = SharpDX.Direct2D1.SolidColorBrush;

namespace Chatterino.Controls
{
    public class MessageContainerControl : ColumnLayoutItem
    {
        public Padding MessagePadding { get; protected set; } = new Padding(8, 8, SystemInformation.VerticalScrollBarWidth + 8, 8);

        protected CustomScrollBar _scroll = new CustomScrollBar
        {
            Enabled = false,
            SmallChange = 4,
        };

        static ContextMenu urlContextMenu;
        static Link urlContextMenuLink;

        static MessageContainerControl()
        {
            urlContextMenu = new ContextMenu();
            urlContextMenu.MenuItems.Add(new MenuItem("Open in Browser", (s, e) => GuiEngine.Current.HandleLink(urlContextMenuLink)));
            urlContextMenu.MenuItems.Add(new MenuItem("Copy link", (s, e) => Clipboard.SetText(urlContextMenuLink.Value as string ?? "")));
        }

        protected bool scrollAtBottom = true;

        private object messageLock = new object();

        protected virtual object MessageLock
        {
            get { return messageLock; }
        }

        Message[] messages = new Message[0];

        protected virtual Message[] Messages
        {
            get { return messages; }
        }

        protected virtual Message[] GetMessagesClone()
        {
            Message[] M;
            lock (MessageLock)
            {
                M = new Message[Messages.Length];
                Array.Copy(Messages, M, M.Length);
            }
            return M;
        }

        protected Message LastReadMessage { get; set; } = null;

        protected List<GifEmoteState> GifEmotesOnScreen = new List<GifEmoteState>();

        // mouse
        protected double mouseScrollMultiplyer = 1;

        protected Link mouseDownLink = null;
        protected Word mouseDownWord = null;
        protected Selection selection = null;
        protected bool mouseDown = false;

        // buffer
        protected BufferedGraphicsContext context = BufferedGraphicsManager.Current;
        protected BufferedGraphics buffer = null;

        protected object bufferLock = new object();

        // Constructor
        public MessageContainerControl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            Width = 600;
            Height = 500;

            _scroll.Location = new Point(Width - SystemInformation.VerticalScrollBarWidth - 1, 1);
            _scroll.Size = new Size(SystemInformation.VerticalScrollBarWidth, Height - 1);
            _scroll.Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom;

            _scroll.Scroll += (s, e) =>
            {
                checkScrollBarPosition();
                updateMessageBounds();
                Invalidate();
            };

            Controls.Add(_scroll);

            App.GifEmoteFramesUpdated += App_GifEmoteFramesUpdated;
            App.EmoteLoaded += App_EmoteLoaded;

            Disposed += (s, e) =>
            {
                App.GifEmoteFramesUpdated -= App_GifEmoteFramesUpdated;
                App.EmoteLoaded -= App_EmoteLoaded;
            };
        }

        private void App_GifEmoteFramesUpdated(object s, EventArgs e)
        {
            try
            {
                lock (bufferLock)
                {
                    if (buffer != null)
                    {
                        bool hasUpdated = false;

                        if (MessageLock != null)
                        {
                            lock (MessageLock)
                            {
                                //for (int i = 0; i < Messages.Length; i++)
                                //{
                                //    var msg = Messages[i];
                                //    if (msg.IsVisible)
                                //    {
                                //        hasUpdated = true;

                                //        MessageRenderer.DrawGifEmotes(buffer.Graphics, msg, selection, i);
                                //    }
                                //}

                                hasUpdated = true;

                                MessageRenderer.DrawGifEmotes(buffer.Graphics, GifEmotesOnScreen, selection);
                            }
                        }

                        if (hasUpdated)
                        {
                            var borderPen = Selected ? App.ColorScheme.ChatBorderFocused : App.ColorScheme.ChatBorder;
                            buffer.Graphics.DrawRectangle(borderPen, 0, Height - 1, Width - 1, 1);

                            var g = CreateGraphics();

                            buffer.Render(g);

                            g.Dispose();
                        }
                    }
                }
            }
            catch { }
        }

        private void App_EmoteLoaded(object s, EventArgs e)
        {
            updateMessageBounds(true);
            Invalidate();
        }

        // overrides
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (_scroll.Enabled)
            {
                int scrollDistance = (int)(e.Delta * AppSettings.ScrollMultiplyer);

                if (MessageLock != null)
                {
                    Graphics graphics = App.UseDirectX ? null : CreateGraphics();

                    lock (MessageLock)
                    {
                        if (e.Delta > 0)
                        {
                            int i = (int)_scroll.Value;
                            double val = _scroll.Value;

                            double scrollFactor = _scroll.Value % 1;
                            int currentScrollLeft = (int)(scrollFactor * Messages[i].Height);

                            for (; i >= 0; i--)
                            {
                                if (scrollDistance < currentScrollLeft)
                                {
                                    val -= scrollFactor * ((double)scrollDistance / currentScrollLeft);
                                    _scroll.Value = val;
                                    break;
                                }
                                else
                                {
                                    scrollDistance -= currentScrollLeft;
                                    val -= scrollFactor;
                                }

                                if (i == 0)
                                {
                                    _scroll.Value = 0;
                                }
                                else
                                {
                                    Messages[i - 1].CalculateBounds(graphics, Width - MessagePadding.Left - MessagePadding.Right);

                                    scrollFactor = 1;
                                    currentScrollLeft = Messages[i - 1].Height;
                                }
                            }
                        }
                        else
                        {
                            scrollDistance = -scrollDistance;

                            int i = (int)_scroll.Value;
                            double val = _scroll.Value;

                            double scrollFactor = 1 - (_scroll.Value % 1);
                            int currentScrollLeft = (int)(scrollFactor * Messages[i].Height);

                            for (; i < Messages.Length; i++)
                            {
                                if (scrollDistance < currentScrollLeft)
                                {
                                    val += scrollFactor * ((double)scrollDistance / currentScrollLeft);
                                    _scroll.Value = val;
                                    break;
                                }
                                else
                                {
                                    scrollDistance -= currentScrollLeft;
                                    val += scrollFactor;
                                }

                                if (i == Messages.Length - 1)
                                {
                                    //_scroll.Value = 0;
                                }
                                else
                                {
                                    Messages[i + 1].CalculateBounds(graphics, Width - MessagePadding.Left - MessagePadding.Right);

                                    scrollFactor = 1;
                                    currentScrollLeft = Messages[i + 1].Height;
                                }
                            }

                        }
                    }

                    graphics?.Dispose();
                }



                //int scrollDistance = (int)(e.Delta * AppSettings.ScrollMultiplyer);

                //double value = _scroll.Value;
                //int index = (int)_scroll.Value;

                //if (MessageLock != null)
                //{
                //    Graphics graphics = App.UseDirectX ? null : CreateGraphics();

                //    lock (MessageLock)
                //    {
                //        while (true)
                //        {
                //            Messages[index].CalculateBounds(graphics, Width - MessagePadding.Left - MessagePadding.Right);

                //            if (scrollDistance - (Messages[index].Height * (value % 1)) < 0)
                //            {


                //                break;
                //            }
                //            else
                //            {
                //                scrollDistance -= Messages[index].Height;

                //                value -= _scroll.Value % 1;
                //            }

                //            index--;
                //        }

                //        _scroll.Value = value;
                //    }

                //    graphics?.Dispose();
                //}

                //if (e.Delta > 0)
                //    scrollAtBottom = false;
                //else
                //    checkScrollBarPosition();

                //updateMessageBounds();

                //Invalidate();

                //_scroll.Value -= ((double)e.Delta / 40 * mouseScrollMultiplyer * AppSettings.ScrollMultiplyer);

                if (e.Delta > 0)
                    scrollAtBottom = false;
                else
                    checkScrollBarPosition();

                updateMessageBounds();

                Invalidate();
            }

            base.OnMouseWheel(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int index;

            var graphics = App.UseDirectX ? null : CreateGraphics();

            var msg = MessageAtPoint(e.Location, out index);
            if (msg != null)
            {
                var word = msg.WordAtPoint(new CommonPoint(e.X - MessagePadding.Left, e.Y - msg.Y));

                var pos = msg.MessagePositionAtPoint(graphics, new CommonPoint(e.X - MessagePadding.Left, e.Y - msg.Y), index);
                //Console.WriteLine($"pos: {pos.MessageIndex} : {pos.WordIndex} : {pos.SplitIndex} : {pos.CharIndex}");

                if (selection != null && mouseDown)
                {
                    var newSelection = new Selection(selection.Start, pos);
                    if (!newSelection.Equals(selection))
                    {
                        selection = newSelection;
                        clearOtherSelections();
                        Invalidate();
                    }
                }

                if (word != null)
                {
                    if (word.Link != null)
                    {
                        Cursor = Cursors.Hand;
                    }
                    else if (word.Type == SpanType.Text)
                    {
                        Cursor = Cursors.IBeam;
                    }
                    else
                    {
                        Cursor = Cursors.Default;
                    }

                    if (word.Tooltip != null)
                    {
                        App.ShowToolTip(PointToScreen(new Point(e.Location.X + 16, e.Location.Y + 16)), word.Tooltip);
                    }
                    else
                    {
                        App.HideToolTip();
                    }
                }
                else
                {
                    Cursor = Cursors.Default;
                    App.HideToolTip();
                }
            }

            graphics?.Dispose();

            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            Cursor = Cursors.Default;

            App.HideToolTip();

            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            //if (e.Button == MouseButtons.Left)
            {
                mouseDown = true;

                int index;

                var msg = MessageAtPoint(e.Location, out index);
                if (msg != null)
                {
                    var graphics = App.UseDirectX ? null : CreateGraphics();
                    var position = msg.MessagePositionAtPoint(graphics, new CommonPoint(e.X - MessagePadding.Left, e.Y - msg.Y), index);
                    graphics?.Dispose();

                    selection = new Selection(position, position);

                    var word = msg.WordAtPoint(new CommonPoint(e.X - MessagePadding.Left, e.Y - msg.Y));
                    if (word != null)
                    {
                        if (word.Link != null)
                        {
                            mouseDownLink = word.Link;
                            mouseDownWord = word;
                        }
                    }
                }
                else
                    selection = null;
            }

            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            mouseDown = false;

            int index;

            var msg = MessageAtPoint(e.Location, out index);
            if (msg != null)
            {
                var word = msg.WordAtPoint(new CommonPoint(e.X - MessagePadding.Left, e.Y - msg.Y));
                if (word != null)
                {
                    if (mouseDownLink != null && mouseDownWord == word)
                    {
                        if (e.Button == MouseButtons.Left)
                        {
                            if (!AppSettings.ChatLinksDoubleClickOnly)
                            {
                                GuiEngine.Current.HandleLink(mouseDownLink);
                            }
                        }
                        else if (e.Button == MouseButtons.Right)
                        {
                            if (mouseDownLink.Type == LinkType.Url)
                            {
                                urlContextMenuLink = mouseDownLink;
                                urlContextMenu.Show(this, e.Location);
                            }
                            else
                            {
                                GuiEngine.Current.HandleLink(mouseDownLink);
                            }
                        }
                    }
                }
            }

            mouseDownLink = null;

            base.OnMouseUp(e);
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);

            if (AppSettings.ChatLinksDoubleClickOnly)
            {
                if (mouseDownLink != null)
                {
                    GuiEngine.Current.HandleLink(mouseDownLink);
                }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            lock (bufferLock)
            {
                if (buffer != null)
                {
                    buffer.Dispose();
                    buffer = null;
                }
            }

            updateMessageBounds();
        }

        Brush lastReadMessageBrush = Brushes.Red;

        protected override void OnPaint(PaintEventArgs e)
        {
            lock (bufferLock)
            {
                try
                {
                    List<GifEmoteState> gifEmotesOnScreen = new List<GifEmoteState>();

                    if (buffer == null)
                    {
                        buffer = context.Allocate(e.Graphics, ClientRectangle);
                    }

                    Graphics g = buffer.Graphics;

                    g.Clear((App.ColorScheme.ChatBackground as SolidBrush).Color);

                    var borderPen = Selected ? App.ColorScheme.ChatBorderFocused : App.ColorScheme.ChatBorder;

                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    // DRAW MESSAGES
                    Message[] M = GetMessagesClone();

                    if (M != null && M.Length > 0)
                    {
                        int startIndex = Math.Max(0, (int)_scroll.Value);
                        if (startIndex < M.Length)
                        {
                            int yStart = MessagePadding.Top - (int)(M[startIndex].Height * (_scroll.Value % 1));
                            int h = Height - MessagePadding.Top - MessagePadding.Bottom;

                            if (startIndex < M.Length)
                            {
                                int y = yStart;

                                //for (int i = 0; i < startIndex; i++)
                                //{
                                //    M[i].IsVisible = false;
                                //}

                                for (int i = startIndex; i < M.Length; i++)
                                {
                                    var msg = M[i];
                                    //msg.IsVisible = true;

                                    MessageRenderer.DrawMessage(g, msg, MessagePadding.Left, y, selection, i, !App.UseDirectX, gifEmotesOnScreen);

                                    if (y - msg.Height > h)
                                    {
                                        //for (; i < M.Length; i++)
                                        //{
                                        //    M[i].IsVisible = false;
                                        //}

                                        break;
                                    }

                                    y += msg.Height;

                                    if (AppSettings.ChatShowLastReadMessageIndicator && LastReadMessage == msg && i != M.Length - 1)
                                    {
                                        g.FillRectangle(lastReadMessageBrush, 0, y, Width, 1);
                                    }
                                }

                                GifEmotesOnScreen = gifEmotesOnScreen;
                            }

                            if (App.UseDirectX)
                            {
                                SharpDX.Direct2D1.DeviceContextRenderTarget renderTarget = null;
                                IntPtr dc = g.GetHdc();

                                renderTarget = new SharpDX.Direct2D1.DeviceContextRenderTarget(MessageRenderer.D2D1Factory, MessageRenderer.RenderTargetProperties);

                                renderTarget.BindDeviceContext(dc, new RawRectangle(0, 0, Width, Height));

                                renderTarget.BeginDraw();

                                //renderTarget.TextRenderingParams = new SharpDX.DirectWrite.RenderingParams(Fonts.Factory, 1, 1, 1, SharpDX.DirectWrite.PixelGeometry.Flat, SharpDX.DirectWrite.RenderingMode.CleartypeGdiClassic);
                                renderTarget.TextAntialiasMode = SharpDX.Direct2D1.TextAntialiasMode.Grayscale;

                                int y = yStart;

                                Dictionary<RawColor4, SCB> brushes = new Dictionary<RawColor4, SCB>();

                                var textColor = App.ColorScheme.Text;
                                var textBrush = new SCB(renderTarget, new RawColor4(textColor.R / 255f, textColor.G / 255f, textColor.B / 255f, 1));

                                for (int i = startIndex; i < M.Length; i++)
                                {
                                    var msg = M[i];

                                    foreach (Word word in msg.Words)
                                    {
                                        if (word.Type == SpanType.Text)
                                        {
                                            SCB brush;

                                            if (word.Color == null)
                                            {
                                                brush = textBrush;
                                            }
                                            else
                                            {
                                                HSLColor hsl = word.Color.Value;

                                                if (App.ColorScheme.IsLightTheme)
                                                {
                                                    if (hsl.Saturation > 0.4f)
                                                    {
                                                        hsl = hsl.WithSaturation(0.4f);
                                                    }
                                                    if (hsl.Luminosity > 0.5f)
                                                    {
                                                        hsl = hsl.WithLuminosity(0.5f);
                                                    }
                                                }
                                                else
                                                {
                                                    if (hsl.Luminosity < 0.66f)
                                                    {
                                                        hsl = hsl.WithLuminosity(0.66f);
                                                    }
                                                }

                                                if (hsl.Luminosity >= 0.9f)
                                                {
                                                    hsl = hsl.WithLuminosity(0.9f);
                                                }

                                                float r, _g, b;
                                                hsl.ToRGB(out r, out _g, out b);
                                                RawColor4 color = new RawColor4(r, _g, b, 1f);

                                                if (!brushes.TryGetValue(color, out brush))
                                                {
                                                    brushes[color] = brush = new SCB(renderTarget, color);
                                                }
                                            }

                                            if (word.SplitSegments == null)
                                            {
                                                renderTarget.DrawText((string)word.Value, Fonts.GetTextFormat(word.Font), new RawRectangleF(MessagePadding.Left + word.X, y + word.Y, 10000, 10000), brush);
                                            }
                                            else
                                            {
                                                foreach (var split in word.SplitSegments)
                                                    renderTarget.DrawText(split.Item1, Fonts.GetTextFormat(word.Font), new RawRectangleF(MessagePadding.Left + split.Item2.X, y + split.Item2.Y, 10000, 10000), brush);
                                            }
                                        }
                                    }

                                    if (y - msg.Height > h)
                                    {
                                        break;
                                    }

                                    y += msg.Height;
                                }

                                foreach (var b in brushes.Values)
                                {
                                    b.Dispose();
                                }

                                renderTarget.EndDraw();

                                textBrush.Dispose();
                                g.ReleaseHdc(dc);
                                renderTarget.Dispose();
                            }

                            {
                                int y = yStart;

                                Brush disabledBrush = new SolidBrush(Color.FromArgb(172, (App.ColorScheme.ChatBackground as SolidBrush)?.Color ?? Color.Black));
                                for (int i = startIndex; i < M.Length; i++)
                                {
                                    var msg = M[i];

                                    if (msg.Disabled)
                                    {
                                        g.SmoothingMode = SmoothingMode.None;

                                        g.FillRectangle(disabledBrush, 0, y, Width, msg.Height);
                                    }

                                    if (y - msg.Height > h)
                                    {
                                        break;
                                    }

                                    y += msg.Height;
                                }
                                disabledBrush.Dispose();
                            }
                        }
                    }

                    g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

                    OnPaintOnBuffer(g);

                    buffer.Render(e.Graphics);
                }
                catch (Exception exc)
                {
                    exc.Log("graphics");
                }
            }
        }

        protected virtual void OnPaintOnBuffer(Graphics g)
        {

        }

        public override void HandleKeys(Keys keys)
        {
            switch (keys)
            {
                case Keys.Control | Keys.C:
                    CopySelection(false);
                    break;

                case Keys.Control | Keys.X:
                    CopySelection(true);
                    break;

                default:
                    base.HandleKeys(keys);
                    break;
            }

            base.HandleKeys(keys);
        }

        // Public Functions
        public Message MessageAtPoint(Point p, out int index)
        {
            if (MessageLock != null)
            {
                lock (MessageLock)
                {
                    var messages = Messages;

                    for (int i = Math.Max(0, (int)_scroll.Value); i < messages.Length; i++)
                    {
                        var m = messages[i];
                        if (m.Y > p.Y - m.Height)
                        {
                            index = i;
                            return m;
                        }
                    }
                }
            }
            index = -1;
            return null;
        }

        public void CopySelection(bool clear)
        {
            string text = null;

            text = GetSelectedText(clear);

            if (!string.IsNullOrEmpty(text))
                Clipboard.SetText(text);
        }

        public virtual string GetSelectedText(bool clear)
        {
            if (selection == null || selection.IsEmpty)
                return null;

            StringBuilder b = new StringBuilder();

            if (MessageLock != null)
            {
                lock (MessageLock)
                {
                    Message[] messages = Messages;

                    bool isFirstLine = true;

                    for (int currentLine = selection.First.MessageIndex; currentLine <= selection.Last.MessageIndex; currentLine++)
                    {
                        if (isFirstLine)
                        {
                            isFirstLine = false;
                        }
                        else
                        {
                            b.Append('\n');
                        }

                        var message = messages[currentLine];

                        var first = selection.First;
                        var last = selection.Last;

                        bool appendNewline = false;

                        for (int i = 0; i < message.Words.Count; i++)
                        {
                            if ((currentLine != first.MessageIndex || i >= first.WordIndex) && (currentLine != last.MessageIndex || i <= last.WordIndex))
                            {
                                var word = message.Words[i];

                                if (appendNewline)
                                {
                                    appendNewline = false;
                                    b.Append(' ');
                                }

                                if (word.Type == SpanType.Text)
                                {
                                    for (int j = 0; j < (word.SplitSegments?.Length ?? 1); j++)
                                    {
                                        if ((first.MessageIndex == currentLine && first.WordIndex == i && first.SplitIndex > j) || (last.MessageIndex == currentLine && last.WordIndex == i && last.SplitIndex < j))
                                            continue;

                                        var split = word.SplitSegments?[j];
                                        string text = split?.Item1 ?? (string)word.Value;
                                        CommonRectangle rect = split?.Item2 ?? new CommonRectangle(word.X, word.Y, word.Width, word.Height);

                                        int textLength = text.Length;

                                        int offset = (first.MessageIndex == currentLine && first.SplitIndex == j && first.WordIndex == i) ? first.CharIndex : 0;
                                        int length = ((last.MessageIndex == currentLine && last.SplitIndex == j && last.WordIndex == i) ? last.CharIndex : textLength) - offset;

                                        b.Append(text.Substring(offset, length));

                                        if (j + 1 == (word.SplitSegments?.Length ?? 1) && ((last.MessageIndex > currentLine) || last.WordIndex > i))
                                            appendNewline = true;
                                    }
                                }
                                else if (word.Type == SpanType.Image)
                                {
                                    int textLength = word.Type == SpanType.Text ? ((string)word.Value).Length : 2;

                                    int offset = (first.MessageIndex == currentLine && first.WordIndex == i) ? first.CharIndex : 0;
                                    int length = ((last.MessageIndex == currentLine && last.WordIndex == i) ? last.CharIndex : textLength) - offset;

                                    if (word.CopyText != null)
                                    {
                                        if (offset == 0)
                                            b.Append(word.CopyText);
                                        if (offset + length == 2)
                                            appendNewline = true;
                                        //b.Append(' ');
                                    }
                                }
                                else if (word.Type == SpanType.Emote)
                                {
                                    int textLength = word.Type == SpanType.Text ? ((string)word.Value).Length : 2;

                                    int offset = (first.MessageIndex == currentLine && first.WordIndex == i) ? first.CharIndex : 0;
                                    int length = ((last.MessageIndex == currentLine && last.WordIndex == i) ? last.CharIndex : textLength) - offset;

                                    if (word.CopyText != null)
                                    {
                                        if (offset == 0)
                                            b.Append(word.CopyText);
                                        if (offset + length == 2)
                                            appendNewline = true;
                                        //b.Append(' ');
                                    }
                                }
                            }
                        }
                    }
                    //for (int i = selection.First.MessageIndex; i <= selection.Last.MessageIndex; i++)
                    //{
                    //    if (i != selection.First.MessageIndex)
                    //        b.AppendLine();

                    //    for (int j = (i == selection.First.MessageIndex ? selection.First.WordIndex : 0); j < (i == selection.Last.MessageIndex ? selection.Last.WordIndex : c.Messages[i].Words.Count); j++)
                    //    {
                    //        if (c.Messages[i].Words[j].CopyText != null)
                    //        {
                    //            b.Append(c.Messages[i].Words[j].CopyText);
                    //            b.Append(' ');
                    //        }
                    //    }
                    //}
                }

            }

            return b.ToString();
        }

        public void ClearSelection()
        {
            if (!(selection?.IsEmpty ?? true))
            {
                selection = null;

                Invalidate();
            }
        }

        static object lastReadMessageTag = "LastReadMessage";

        public void SetLastReadMessage()
        {
            if (MessageLock != null)
            {
                lock (MessageLock)
                {
                    if (Messages.Length != 0)
                    {
                        LastReadMessage = Messages[Messages.Length - 1];

                        _scroll.RemoveHighlightsWhere(highlight => highlight.Tag == lastReadMessageTag);
                        _scroll.AddHighlight(Messages.Length - 1, Color.Red, ScrollBarHighlightStyle.SingleLine,
                            lastReadMessageTag);
                    }
                }
            }
        }

        // Private Helpers
        protected virtual void clearOtherSelections() { }

        protected virtual void updateMessageBounds(bool emoteChanged = false)
        {
            object g = App.UseDirectX ? null : CreateGraphics();

            // determine if
            double scrollbarThumbHeight = 0;
            int totalHeight = Height - MessagePadding.Top - MessagePadding.Bottom;
            int currentHeight = 0;
            int tmpHeight = Height - MessagePadding.Top - MessagePadding.Bottom;
            bool enableScrollbar = false;
            int messageCount = 0;

            if (MessageLock != null)
            {
                lock (MessageLock)
                {

                    var messages = Messages;
                    messageCount = messages.Length;

                    int visibleStart = Math.Max(0, (int)_scroll.Value);

                    // set EmotesChanged for messages
                    if (emoteChanged)
                    {
                        for (int i = 0; i < messages.Length; i++)
                        {
                            messages[i].EmoteBoundsChanged = true;
                        }
                    }

                    // calculate bounds for visible messages
                    for (int i = visibleStart; i < messages.Length; i++)
                    {
                        var msg = messages[i];

                        msg.CalculateBounds(g, Width - MessagePadding.Left - MessagePadding.Right);
                        currentHeight += msg.Height;

                        if (currentHeight > totalHeight)
                        {
                            break;
                        }
                    }

                    // calculate bounds for messages at the bottom to determine the size of the scrollbar thumb
                    for (int i = messages.Length - 1; i >= 0; i--)
                    {
                        var msg = messages[i];
                        msg.CalculateBounds(g, Width - MessagePadding.Left - MessagePadding.Right);
                        scrollbarThumbHeight++;

                        tmpHeight -= msg.Height;
                        if (tmpHeight < 0)
                        {
                            enableScrollbar = true;
                            scrollbarThumbHeight -= 1 - (double)tmpHeight / msg.Height;
                            break;
                        }
                    }
                }
            }

            (g as Graphics)?.Dispose();

            this.Invoke(() =>
            {
                try
                {
                    if (enableScrollbar)
                    {
                        _scroll.Enabled = true;
                        _scroll.LargeChange = scrollbarThumbHeight;
                        _scroll.Maximum = messageCount - 1;

                        if (scrollAtBottom)
                            _scroll.Value = messageCount - scrollbarThumbHeight;
                    }
                    else
                    {
                        _scroll.Enabled = false;
                        _scroll.Value = 0;
                    }
                }
                catch { }
            });
        }

        protected void checkScrollBarPosition()
        {
            scrollAtBottom = !_scroll.Enabled || _scroll.Maximum < _scroll.Value + _scroll.LargeChange + 0.0001;
        }
    }
}