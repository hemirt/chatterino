﻿using Chatterino.Common;
using Meebey.SmartIrc4net;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Message = Chatterino.Common.Message;

namespace Chatterino.Controls
{
    public class ChatControl : ColumnLayoutItemBase
    {
        // Properties
        public const int TopMenuBarHeight = 32;

        public Padding TextPadding { get; private set; } = new Padding(12, 8 + TopMenuBarHeight, 16 + SystemInformation.VerticalScrollBarWidth, 8);

        public Message SendMessage { get; set; } = null;

        ChatControlHeader _header = null;

        // vars
        private bool scrollAtBottom = true;

        TwitchChannel channel = null;

        public TwitchChannel Channel
        {
            get { return channel; }
        }

        private string channelName;

        public string ChannelName
        {
            get { return channelName; }
            set
            {
                value = value.Trim();
                if (value != channelName)
                {
                    if (channel != null)
                    {
                        channel.MessageAdded -= Channel_MessageAdded;
                        channel.MessagesAddedAtStart -= Channel_MessagesAddedAtStart;
                        channel.ChatCleared -= Channel_ChatCleared;
                        channel.RoomStateChanged -= Channel_RoomStateChanged;
                        channel = null;
                        TwitchChannel.RemoveChannel(ChannelName);
                    }

                    channelName = value;

                    if (!string.IsNullOrWhiteSpace(channelName))
                    {
                        channel = TwitchChannel.AddChannel(channelName);
                        channel.MessageAdded += Channel_MessageAdded;
                        channel.MessagesAddedAtStart += Channel_MessagesAddedAtStart;
                        channel.ChatCleared += Channel_ChatCleared;
                        channel.RoomStateChanged += Channel_RoomStateChanged;
                    }

                    this.Invoke(() =>
                    {
                        _header?.Invalidate();

                        Invalidate();
                    });
                }
            }
        }

        CustomScrollBar vscroll = new CustomScrollBar
        {
            Enabled = false,
            SmallChange = 4,
        };

        string lastTabComplete = null;
        int currentTabIndex = 0;

        // ctor
        public ChatControl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.ResizeRedraw, true);

            //IrcManager.MessageReceived += IrcManager_MessageReceived;
            //IrcManager.ChatCleared += IrcManager_IrcChatCleared;
            //IrcManager.Connected += IrcManager_Connected;
            //IrcManager.Disconnected += IrcManager_Disconnected;
            //IrcManager.ConnectionError += IrcManager_ConnectionError;
            //IrcManager.OldMessagesReceived += IrcManager_OldMessagesReceived;
            App.GifEmoteFramesUpdated += App_GifEmoteFramesUpdated;
            App.EmoteLoaded += App_EmoteLoaded;

            Disposed += (s, e) =>
            {
                //IrcManager.MessageReceived -= IrcManager_MessageReceived;
                //IrcManager.ChatCleared -= IrcManager_IrcChatCleared;
                //IrcManager.Connected -= IrcManager_Connected;
                //IrcManager.Disconnected -= IrcManager_Disconnected;
                //IrcManager.ConnectionError -= IrcManager_ConnectionError;
                //IrcManager.OldMessagesReceived -= IrcManager_OldMessagesReceived;
                App.GifEmoteFramesUpdated -= App_GifEmoteFramesUpdated;
                App.EmoteLoaded -= App_EmoteLoaded;

                TwitchChannel.RemoveChannel(ChannelName);
            };

            Font = Fonts.Medium;

            ChatControlHeader header = _header = new ChatControlHeader(this);
            Controls.Add(header);

            GotFocus += (s, e) => { header.Invalidate(); };
            LostFocus += (s, e) => { header.Invalidate(); };

            Controls.Add(vscroll);

            vscroll.Height = Height - TopMenuBarHeight;
            vscroll.Location = new Point(Width - SystemInformation.VerticalScrollBarWidth - 1, TopMenuBarHeight);
            vscroll.Size = new Size(SystemInformation.VerticalScrollBarWidth, Height - TopMenuBarHeight - 1);
            vscroll.Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom;

            vscroll.Scroll += (s, e) =>
            {
                checkScrollBarPosition();
                updateMessageBounds();
                Invalidate();
            };
        }

        private void App_GifEmoteFramesUpdated(object s, EventArgs e)
        {
            channel.Process(c =>
            {
                var g = CreateGraphics();

                lock (c.MessageLock)
                {
                    for (int i = 0; i < c.Messages.Length; i++)
                    {
                        var msg = c.Messages[i];
                        if (msg.IsVisible)
                        {
                            msg.UpdateGifEmotes(g, selection, i);
                        }
                    }
                }
            });
        }

        private void App_EmoteLoaded(object s, EventArgs e)
        {
            updateMessageBounds(true);
            Invalidate();
        }

        private void Channel_MessageAdded(object sender, MessageAddedEventArgs e)
        {
            if (e.RemovedMessage != null)
            {
                if (selection != null)
                {
                    if (selection.Start.MessageIndex == 0)
                        selection = null;
                    else
                        selection = new Selection(selection.Start.WithMessageIndex(selection.Start.MessageIndex - 1), selection.Start.WithMessageIndex(selection.End.MessageIndex - 1));
                }

                vscroll.Value--;

                vscroll.UpdateHighlights(h => h.Position--);
                vscroll.RemoveHighlightsWhere(h => h.Position < 0);
            }

            if (e.Message.Highlighted)
                vscroll.AddHighlight((channel?.MessageCount ?? 1) - 1, Color.Red);

            updateMessageBounds();
            Invalidate();
        }

        private void Channel_MessagesAddedAtStart(object sender, ValueEventArgs<Message[]> e)
        {
            vscroll.UpdateHighlights(h => h.Position += e.Value.Length);

            for (int i = 0; i < e.Value.Length; i++)
            {
                if (e.Value[i].Highlighted)
                    vscroll.AddHighlight(i, Color.Red);
            }

            updateMessageBounds();
            Invalidate();
        }

        private void Channel_ChatCleared(object sender, ChatClearedEventArgs e)
        {
            this.Invoke(() => Invalidate());
        }

        private void Channel_RoomStateChanged(object sender, EventArgs e)
        {
            _header.Invoke(() =>
            {
                var c = channel;
                if (c != null)
                {
                    string text = "";

                    RoomState state = c.RoomState;
                    int count = 0;
                    if (state.HasFlag(RoomState.SlowMode))
                    {
                        text += "slow(" + c.SlowModeTime + "), ";
                        count++;
                    }
                    if (state.HasFlag(RoomState.SubOnly))
                    {
                        text += "sub, ";
                        count++;
                    }
                    if (count == 2)
                        text += "\n";
                    if (state.HasFlag(RoomState.R9k))
                    {
                        text += "r9k, ";
                        count++;
                    }
                    if (count == 2)
                        text += "\n";
                    if (state.HasFlag(RoomState.EmoteOnly))
                    {
                        text += "emote, ";
                        count++;
                    }

                    _header.RoomstateButton.Text = text == "" ? "-" : text.TrimEnd(' ', ',', '\n');
                    _header.Invalidate();
                }
            });
        }

        string mouseDownLink = null;
        Word mouseDownWord = null;
        Selection selection = null;
        bool mouseDown = false;

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            vscroll.Value -= ((double)e.Delta / 10);

            if (e.Delta > 0)
                scrollAtBottom = false;
            else
                checkScrollBarPosition();

            updateMessageBounds();

            Invalidate();

            base.OnMouseWheel(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int index;

            var graphics = CreateGraphics();

            var msg = MessageAtPoint(e.Location, out index);
            if (msg != null)
            {
                var word = msg.WordAtPoint(new CommonPoint(e.X - TextPadding.Left, e.Y - msg.Y));

                var pos = msg.MessagePositionAtPoint(graphics, new CommonPoint(e.X - TextPadding.Left, e.Y - msg.Y), index);
                Console.WriteLine($"pos: {pos.MessageIndex} : {pos.WordIndex} : {pos.SplitIndex} : {pos.CharIndex}");

                if (selection != null && mouseDown)
                {
                    var newSelection = new Selection(selection.Start, pos);
                    if (!newSelection.Equals(selection))
                    {
                        selection = newSelection;
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
                        App.ToolTip?.Hide();
                    }
                }
                else
                {
                    Cursor = Cursors.Default;
                    App.ToolTip?.Hide();
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left)
            {
                mouseDown = true;

                int index;

                var msg = MessageAtPoint(e.Location, out index);
                if (msg != null)
                {
                    var graphics = CreateGraphics();
                    var position = msg.MessagePositionAtPoint(graphics, new CommonPoint(e.X - TextPadding.Left, e.Y - msg.Y), index);
                    selection = new Selection(position, position);

                    var word = msg.WordAtPoint(new CommonPoint(e.X - TextPadding.Left, e.Y - msg.Y));
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
            base.OnMouseUp(e);

            if (e.Button == MouseButtons.Left)
            {
                mouseDown = false;

                int index;

                var msg = MessageAtPoint(e.Location, out index);
                if (msg != null)
                {
                    var word = msg.WordAtPoint(new CommonPoint(e.X - TextPadding.Left, e.Y - msg.Y));
                    if (word != null)
                    {
                        if (mouseDownLink != null && mouseDownWord == word && !AppSettings.ChatLinksDoubleClickOnly)
                        {
                            GuiEngine.Current.HandleLink(mouseDownLink);
                        }
                    }
                }

                mouseDownLink = null;
            }
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);

            //if ((ModifierKeys & ~Keys.Shift) == Keys.None)
            {
                if (e.KeyChar == '\b')
                {
                    if (SendMessage != null)
                    {
                        var message = SendMessage.RawMessage;
                        if (message.Length > 1)
                        {
                            SendMessage = new Message(message.Remove(message.Length - 1));
                        }
                        else
                        {
                            SendMessage = null;
                        }
                    }
                    lastTabComplete = null;
                    currentTabIndex = 0;
                }
                else if (e.KeyChar == '\r')
                {
                    if (SendMessage != null)
                    {
                        IrcManager.SendMessage(channelName, SendMessage.RawMessage);
                        SendMessage = null;
                    }
                    lastTabComplete = null;
                    currentTabIndex = 0;
                }
                else if (e.KeyChar >= ' ')
                {
                    if (SendMessage == null)
                    {
                        SendMessage = new Message(e.KeyChar.ToString());
                    }
                    else
                    {
                        SendMessage = new Message(SendMessage.RawMessage + e.KeyChar.ToString());
                    }
                    lastTabComplete = null;
                    currentTabIndex = 0;
                }

                updateMessageBounds();
                Invalidate();
            }
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);

            if (AppSettings.ChatLinksDoubleClickOnly)
            {
                if (mouseDownLink != null)
                    GuiEngine.Current.HandleLink(mouseDownLink);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            updateMessageBounds();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var borderPen = Focused ? App.ColorScheme.ChatBorderFocused : App.ColorScheme.ChatBorder;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // DRAW MESSAGES
            Message[] M = channel?.CloneMessages();

            if (M != null && M.Length > 0)
            {
                int startIndex = Math.Max(0, (int)vscroll.Value);

                int y = TextPadding.Top - (int)(M[startIndex].Height * (vscroll.Value % 1));
                int h = Height - TextPadding.Top - TextPadding.Bottom;

                for (int i = 0; i < startIndex; i++)
                {
                    M[i].IsVisible = false;
                }

                for (int i = startIndex; i < M.Length; i++)
                {
                    var msg = M[i];
                    msg.IsVisible = true;

                    msg.Draw(e.Graphics, TextPadding.Left, y, selection, i);

                    if (y - msg.Height > h)
                    {
                        for (; i < M.Length; i++)
                        {
                            M[i].IsVisible = false;
                        }

                        break;
                    }

                    y += msg.Height;
                }

                //for (int i = 0; i < M.Length; i++)
                //{
                //    var msg = M[i];
                //    if (y + msg.Height > 0)
                //    {
                //        if (y > Height)
                //        {
                //            for (; i < M.Length; i++)
                //            {
                //                M[i].IsVisible = false;
                //                msg.Y = y;
                //                y += msg.Height;
                //            }
                //            break;
                //        }
                //        msg.Draw(e.Graphics, TextPadding.Left, y, selection, i);
                //        msg.IsVisible = true;
                //    }
                //    else
                //    {
                //        msg.IsVisible = false;
                //    }
                //    msg.Y = y;
                //    y += msg.Height;
                //}
            }

            e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1/* - SystemInformation.VerticalScrollBarWidth*/, Height - 1);

            if (SendMessage != null)
            {
                e.Graphics.FillRectangle(App.ColorScheme.ChatBackground, 1, Height - SendMessage.Height - 4, Width - 3 - SystemInformation.VerticalScrollBarWidth, SendMessage.Height + TextPadding.Bottom - 1);
                e.Graphics.DrawLine(borderPen, 1, Height - SendMessage.Height - 4, Width - 2 - SystemInformation.VerticalScrollBarWidth, Height - SendMessage.Height - 4);
                SendMessage.Draw(e.Graphics, TextPadding.Left, Height - SendMessage.Height, null, -1);
            }
        }

        public void HandleTabCompletion(bool forward)
        {
            if (SendMessage != null)
            {
                string text = SendMessage.RawMessage;
                int index;
                text = (index = text.LastIndexOf(' ')) == -1 ? text : text.Substring(index + 1);

                if (channel != null)
                {
                    if (lastTabComplete == null)
                        currentTabIndex = forward ? -1 : 1;

                    var completion = channel.GetEmoteCompletion(lastTabComplete ?? text, ref currentTabIndex, forward);
                    if (completion != null)
                    {
                        lastTabComplete = lastTabComplete ?? text;

                        SendMessage = new Message((index == -1 ? "" : SendMessage.RawMessage.Remove(index + 1)) + completion);
                        updateMessageBounds();
                        this.Invoke(() => Invalidate());
                    }
                }
            }
        }

        void updateMessageBounds(bool emoteChanged = false)
        {
            var g = CreateGraphics();

            // calculate the send messages bounds
            if (SendMessage != null)
            {
                SendMessage.CalculateBounds(g, Width - TextPadding.Left - TextPadding.Right);
                TextPadding = new Padding(TextPadding.Left, TextPadding.Top, TextPadding.Right, 8 + SendMessage.Height);
            }
            else
                TextPadding = new Padding(TextPadding.Left, TextPadding.Top, TextPadding.Right, 8);

            // determine if
            double scrollbarThumbHeight = 0;
            int totalHeight = Height - TextPadding.Top - TextPadding.Bottom;
            int currentHeight = 0;
            int tmpHeight = Height - TextPadding.Top - TextPadding.Bottom;
            bool enableScrollbar = false;
            int messageCount = 0;

            var c = channel;

            if (c != null)
            {
                lock (channel.MessageLock)
                {
                    var messages = channel.Messages;
                    messageCount = messages.Length;

                    int visibleStart = Math.Max(0, (int)vscroll.Value);

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

                        msg.CalculateBounds(g, Width - TextPadding.Left - TextPadding.Right);
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
                        msg.CalculateBounds(g, Width - TextPadding.Left - TextPadding.Right);
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
            g.Dispose();

            this.Invoke(() =>
            {
                if (enableScrollbar)
                {
                    vscroll.Enabled = true;
                    vscroll.LargeChange = scrollbarThumbHeight;
                    vscroll.Maximum = messageCount - 1;

                    if (scrollAtBottom)
                        vscroll.Value = messageCount - scrollbarThumbHeight;
                }
                else
                {
                    vscroll.Enabled = false;
                    vscroll.Value = 0;
                }
            });
        }

        void checkScrollBarPosition()
        {
            scrollAtBottom = !vscroll.Enabled || vscroll.Maximum < vscroll.Value + vscroll.LargeChange + 0.0001;
        }

        public Message MessageAtPoint(Point p, out int index)
        {
            var c = channel;

            if (c != null)
            {
                lock (c.MessageLock)
                {
                    for (int i = Math.Max(0, (int)vscroll.Value); i < c.Messages.Length; i++)
                    {
                        var m = c.Messages[i];
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

        public void CopySelection()
        {
            var text = GetSelectedText();

            if (text != null)
                Clipboard.SetText(text);
        }

        public string GetSelectedText()
        {
            if (selection == null || selection.IsEmpty)
                return null;

            StringBuilder b = new StringBuilder();

            var c = channel;

            if (c != null)
            {
                lock (c.MessageLock)
                {
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

                        var message = c.Messages[currentLine];

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
                                            //b.Append(' ');
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

        public void PasteText(string text)
        {
            text = Regex.Replace(text, @"\r?\n", " ");

            if (SendMessage == null)
                SendMessage = new Message(text);
            else
                SendMessage = new Message(SendMessage.RawMessage + text);

            Invalidate();
        }


        // header
        class ChatControlHeader : Control
        {
            // static Menu Dropdown
            static ContextMenu contextMenu;
            static ContextMenu roomstateContextMenu;
            static ChatControl selected = null;
            static MenuItem messageCountItem;

            static MenuItem roomstateSlow;
            static MenuItem roomstateSub;
            static MenuItem roomstateEmoteonly;
            static MenuItem roomstateR9K;

            static ChatControlHeader()
            {
                contextMenu = new ContextMenu();
                contextMenu.MenuItems.Add(new MenuItem("Add new Split", (s, e) => { App.MainForm?.AddNewSplit(); }, Shortcut.CtrlT));
                contextMenu.MenuItems.Add(new MenuItem("Close Split", (s, e) => { App.MainForm?.RemoveSelectedSplit(); }, Shortcut.CtrlW));
                contextMenu.MenuItems.Add(new MenuItem("Change Channel", (s, e) => { App.MainForm?.RenameSelectedSplit(); }, Shortcut.CtrlR));
                contextMenu.MenuItems.Add("-");
                contextMenu.MenuItems.Add(new MenuItem("Login", (s, e) => new LoginForm().ShowDialog(), Shortcut.CtrlL));
                contextMenu.MenuItems.Add(new MenuItem("Preferences", (s, e) => App.ShowSettings(), Shortcut.CtrlP));
                contextMenu.MenuItems.Add("-");
                contextMenu.MenuItems.Add(messageCountItem = new MenuItem("MessageCount: 0", (s, e) => { }) { Enabled = false });

                roomstateContextMenu = new ContextMenu();
                roomstateContextMenu.Popup += (s, e) =>
                {
                    roomstateR9K.Checked = (selected.Channel?.RoomState ?? RoomState.None).HasFlag(RoomState.R9k);
                    roomstateSlow.Checked = (selected.Channel?.RoomState ?? RoomState.None).HasFlag(RoomState.SlowMode);
                    roomstateSub.Checked = (selected.Channel?.RoomState ?? RoomState.None).HasFlag(RoomState.SubOnly);
                    roomstateEmoteonly.Checked = (selected.Channel?.RoomState ?? RoomState.None).HasFlag(RoomState.EmoteOnly);
                };

                roomstateContextMenu.MenuItems.Add(roomstateSlow = new MenuItem("Slowmode", (s, e) =>
                {
                    if (selected.Channel != null)
                    {
                        if (selected.Channel.RoomState.HasFlag(RoomState.SlowMode))
                            selected.Channel.SendMessage("/Slowoff");
                        else
                            selected.Channel.SendMessage("/Slow");
                    }
                }));
                roomstateContextMenu.MenuItems.Add(roomstateSub = new MenuItem("Subscribers Only", (s, e) =>
                {
                    if (selected.Channel != null)
                    {
                        if (selected.Channel.RoomState.HasFlag(RoomState.SubOnly))
                            selected.Channel.SendMessage("/Subscribersoff");
                        else
                            selected.Channel.SendMessage("/Subscribers");
                    }
                }));
                roomstateContextMenu.MenuItems.Add(roomstateR9K = new MenuItem("R9K", (s, e) =>
                {
                    if (selected.Channel != null)
                    {
                        if (selected.Channel.RoomState.HasFlag(RoomState.R9k))
                            selected.Channel.SendMessage("/R9KBetaOff");
                        else
                            selected.Channel.SendMessage("/R9KBeta");
                    }
                }));
                roomstateContextMenu.MenuItems.Add(roomstateEmoteonly = new MenuItem("Emote Only", (s, e) =>
                {
                    if (selected.Channel != null)
                    {
                        if (selected.Channel.RoomState.HasFlag(RoomState.EmoteOnly))
                            selected.Channel.SendMessage("/Emoteonlyoff");
                        else
                            selected.Channel.SendMessage("/emoteonly ");
                    }
                }));
            }

            // local controls
            private ChatControl chatControl;

            public ChatControlHeaderButton RoomstateButton { get; private set; }
            public ChatControlHeaderButton DropDownButton { get; private set; }

            // Constructor
            public ChatControlHeader(ChatControl chatControl)
            {
                this.chatControl = chatControl;

                SetStyle(ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

                Dock = DockStyle.Top;
                Height = TopMenuBarHeight + 1;

                // Mousedown
                bool mouseDown = false;

                MouseDown += (s, e) =>
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        mouseDown = true;
                        chatControl.Select();
                    }
                };
                MouseUp += (s, e) =>
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        mouseDown = false;
                    }
                };

                // Drag + Drop
                MouseMove += (s, e) =>
                {
                    if (mouseDown)
                    {
                        if (e.X < 0 || e.Y < 0 || e.X > Width || e.Y > Height)
                        {
                            ColumnLayoutControl layout = chatControl.Parent as ColumnLayoutControl;
                            if (layout != null)
                            {
                                var position = layout.RemoveFromGrid(chatControl);
                                if (DoDragDrop(new ColumnLayoutDragDropContainer { Control = chatControl }, DragDropEffects.Move) == DragDropEffects.None)
                                {
                                    layout.AddToGrid(this, position.Item1, position.Item2);
                                }
                            }
                        }
                    }
                };

                // Buttons
                ChatControlHeaderButton button = DropDownButton = new ChatControlHeaderButton
                {
                    Height = Height - 2,
                    Width = Height - 2,
                    Location = new Point(1, 1),
                    Image = Properties.Resources.settings
                };
                button.MouseDown += (s, e) =>
                {
                    if (e.Button == MouseButtons.Left)
                        chatControl.Select();

                };
                button.Click += (s, e) =>
                {
                    messageCountItem.Text = "MessageCount: " + chatControl.Channel.MessageCount;
                    selected = chatControl;
                    contextMenu.Show(this, new Point(Location.X, Location.Y + Height));
                };

                Controls.Add(button);

                RoomstateButton = button = new ChatControlHeaderButton
                {
                    Height = Height - 2,
                    Width = Height - 2,
                    Location = new Point(Width - Height, 1),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right
                };
                button.Font = new Font(button.Font.FontFamily, 8f);
                button.Text = "-";
                button.MouseDown += (s, e) =>
                {
                    if (e.Button == MouseButtons.Left)
                        chatControl.Select();
                };
                button.Click += (s, e) =>
                {
                    selected = chatControl;
                    roomstateContextMenu.Show(this, new Point(Location.X + Width, Location.Y + Height), LeftRightAlignment.Left);
                };

                Controls.Add(button);
            }

            protected override void OnDoubleClick(EventArgs e)
            {
                base.OnDoubleClick(e);

                using (InputDialogForm dialog = new InputDialogForm("channel name") { Value = chatControl.ChannelName })
                {
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        chatControl.ChannelName = dialog.Value;
                    }
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                // CHANNEL NAME
                e.Graphics.FillRectangle(App.ColorScheme.Menu, 0, 0, Width, ChatControl.TopMenuBarHeight);
                e.Graphics.DrawRectangle(Focused ? App.ColorScheme.ChatBorderFocused : App.ColorScheme.ChatBorder, 0, 0, Width - 1, Height - 1);
                //e.Graphics.DrawLine(Focused ? App.ColorScheme.ChatBorderFocused : App.ColorScheme.ChatBorder, 0, ChatControl.TopMenuBarHeight, Width, ChatControl.TopMenuBarHeight);
                //e.Graphics.DrawLine(Focused ? App.ColorScheme.ChatBorderFocused : App.ColorScheme.ChatBorder, 0, 0, Width, 0);

                string title = string.IsNullOrWhiteSpace(chatControl.ChannelName) ? "<no channel>" : chatControl.ChannelName;

                //var size = TextRenderer.MeasureText(e.Graphics, title, chatControl.Font, Size.Empty, App.DefaultTextFormatFlags);
                //TextRenderer.DrawText(e.Graphics, title, chatControl.Font, new Point((Width / 2) - (size.Width / 2), TopMenuBarHeight / 2 - (size.Height / 2)), chatControl.Focused ? App.ColorScheme.TextFocused : App.ColorScheme.Text, App.DefaultTextFormatFlags);
                TextRenderer.DrawText(e.Graphics, title, chatControl.Font, new Rectangle(DropDownButton.Width, 0, Width - DropDownButton.Width - RoomstateButton.Width, Height), chatControl.Focused ? App.ColorScheme.TextFocused : App.ColorScheme.Text, App.DefaultTextFormatFlags | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            }
        }

        class ChatControlHeaderButton : Control
        {
            bool mouseOver = false;
            bool mouseDown = false;

            private Image image;

            public Image Image
            {
                get { return image; }
                set { image = value; Invalidate(); }
            }

            void calcSize()
            {
                if (Text != "")
                {
                    int width = Width;
                    Width = 16 + TextRenderer.MeasureText(Text, Font).Width;
                    if ((Anchor & AnchorStyles.Right) == AnchorStyles.Right)
                        Location = new Point(Location.X - (Width - width), Location.Y);
                    Invalidate();
                }
            }

            public ChatControlHeaderButton()
            {
                TextChanged += (s, e) => calcSize();
                SizeChanged += (s, e) => calcSize();

                MouseEnter += (s, e) =>
                {
                    mouseOver = true;
                    Invalidate();
                };

                MouseLeave += (s, e) =>
                {
                    mouseOver = false;
                    Invalidate();
                };

                MouseDown += (s, e) =>
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        mouseDown = true;
                        Invalidate();
                    }
                };

                MouseUp += (s, e) =>
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        mouseDown = false;
                        Invalidate();
                    }
                };
            }

            Brush mouseOverBrush = new SolidBrush(Color.FromArgb(48, 255, 255, 255));

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                e.Graphics.FillRectangle(App.ColorScheme.Menu, e.ClipRectangle);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;

                if (mouseDown)
                    g.FillRectangle(mouseOverBrush, 0, 0, Width, Height);

                if (mouseOver)
                    g.FillRectangle(mouseOverBrush, 0, 0, Width, Height);

                if (image != null)
                {
                    g.DrawImage(image, Width / 2 - image.Width / 2, Height / 2 - image.Height / 2);
                }

                if (Text != null)
                {
                    TextRenderer.DrawText(g, Text, Font, ClientRectangle, App.ColorScheme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }
    }
}
