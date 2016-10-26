﻿using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TwitchIrc;

namespace Chatterino.Common
{
    public class TwitchChannel
    {
        const int maxMessages = 10000;

        // properties
        public string Name { get; private set; }

        public int RoomID { get; private set; } = -1;

        public string SubLink { get; private set; }
        public string ChannelLink { get; private set; }
        public string PopoutPlayerLink { get; private set; }

        protected int Uses { get; set; } = 0;

        // Channel Emotes
        public ConcurrentDictionary<string, TwitchEmote> BttvChannelEmotes { get; private set; }
            = new ConcurrentDictionary<string, TwitchEmote>();

        public ConcurrentDictionary<string, TwitchEmote> FfzChannelEmotes { get; private set; }
            = new ConcurrentDictionary<string, TwitchEmote>();


        // Sub Badge
        private TwitchEmote subBadge;

        public TwitchEmote SubscriberBadge
        {
            get
            {
                return subBadge ?? (subBadge = new TwitchEmote
                {
                    LoadAction = () =>
                    {
                        try
                        {
                            string imageUrl = null;

                            var request = WebRequest.Create($"https://api.twitch.tv/kraken/chat/{Name}/badges?client_id=7ue61iz46fz11y3cugd0l3tawb4taal");
                            using (var response = request.GetResponse())
                            using (var stream = response.GetResponseStream())
                            {
                                var json = new JsonParser().Parse(stream);

                                imageUrl = (string)(((Dictionary<string, object>)((Dictionary<string, object>)json)["subscriber"])["image"]);
                            }

                            request = WebRequest.Create(imageUrl);
                            using (var response = request.GetResponse())
                            using (var stream = response.GetResponseStream())
                            {
                                var img = GuiEngine.Current.ReadImageFromStream(stream);
                                GuiEngine.Current.FreezeImage(img);
                                return img;
                            }
                        }
                        catch (Exception exc)
                        {
                            return null;
                        }
                    }
                });
            }
        }

        public ConcurrentDictionary<int, TwitchEmote> SubscriberBadges = new ConcurrentDictionary<int, TwitchEmote>();

        public TwitchEmote GetSubscriberBadge(int months)
        {
            TwitchEmote emote;

            if (SubscriberBadges.TryGetValue(months, out emote))
            {
                return emote;
            }

            return SubscriberBadge;
        }


        // Moderator Badge
        public TwitchEmote ModeratorBadge { get; private set; } = null;


        // Roomstate
        public event EventHandler RoomStateChanged;

        private RoomState roomState;

        public RoomState RoomState
        {
            get { return roomState; }
            set
            {
                if (roomState != value)
                {
                    roomState = value;
                    RoomStateChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private int slowModeTime;

        public int SlowModeTime
        {
            get { return slowModeTime; }
            set
            {
                if (slowModeTime != value)
                {
                    slowModeTime = value;
                    RoomStateChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }


        // Userstate
        public event EventHandler UserStateChanged;

        private bool isMod;

        public bool IsMod
        {
            get
            {
                return isMod;
            }
            set
            {
                if (isMod != value)
                {
                    isMod = value;

                    UserStateChanged?.Invoke(null, EventArgs.Empty);
                }
            }
        }

        public bool IsModOrBroadcaster
        {
            get
            {
                return IsMod || Name.ToLower() == IrcManager.Username.ToLower();
            }
        }

        public bool IsBroadcaster
        {
            get
            {
                return Name.ToLower() == IrcManager.Username.ToLower();
            }
        }

        protected void loadData()
        {
            loadRoomID();
        }

        protected void loadRoomID()
        {
            // Try to load from cache
            RoomID = Cache.roomIDCache.Get(Name);

            if (RoomID == -1) {
                // No room ID was saved in the cache

                if (loadRoomIDFromTwitch()) {
                    // Successfully got a room ID from twitch
                    Cache.roomIDCache.Set(Name, RoomID);
                }
            }
        }

        protected bool loadRoomIDFromTwitch()
        {
            // call twitch kraken api
            try
            {
                var request = WebRequest.Create($"https://api.twitch.tv/kraken/channels/{Name}?client_id=7ue61iz46fz11y3cugd0l3tawb4taal");
                using (var response = request.GetResponse())
                using (var stream = response.GetResponseStream())
                {
                    JsonParser parser = new JsonParser();

                    dynamic json = parser.Parse(stream);

                    int roomID;

                    if (int.TryParse(json["_id"], out roomID))
                    {
                        RoomID = roomID;
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        // ctor
        protected TwitchChannel(string channelName)
        {
            if (!channelName.StartsWith("/"))
            {
                Name = channelName.Trim('#');
                SubLink = $"https://www.twitch.tv/{Name}/subscribe?ref=in_chat_subscriber_link";
                ChannelLink = $"https://twitch.tv/{Name}";
                PopoutPlayerLink = $"https://player.twitch.tv/?channel={Name}";

                Join();

                string bttvChannelEmotesCache = $"./Cache/bttv_channel_{channelName}";
                string ffzChannelEmotesCache = $"./Cache/ffz_channel_{channelName}";

                // recent chat
                Task.Run(() =>
                {
                    loadData();

                    if (RoomID != -1)
                    {
                        try
                        {
                            var request = WebRequest.Create($"https://badges.twitch.tv/v1/badges/channels/{RoomID}/display");
                            using (var response = request.GetResponse())
                            using (var stream = response.GetResponseStream())
                            {
                                JsonParser parser = new JsonParser();

                                dynamic json = parser.Parse(stream);

                                dynamic badgeSets = json["badge_sets"];
                                dynamic subscriber = badgeSets["subscriber"];
                                dynamic versions = subscriber["versions"];

                                foreach (dynamic version in versions)
                                {
                                    int months = int.Parse(version.Key);

                                    dynamic value = version.Value;

                                    string imageUrl = value["image_url_1x"];
                                    string title = value["title"];
                                    string description = value["description"];
                                    string clickUrl = value["click_url"];

                                    SubscriberBadges[months] = new TwitchEmote { Name = title, Url = imageUrl, Tooltip = "Subscriber Badge" + (months == 0 ? "" : $" ({months} months)") };
                                }
                            }
                        }
                        catch { }

                        try
                        {
                            List<Message> messages = new List<Message>();

                            var request = WebRequest.Create($"https://tmi.twitch.tv/api/rooms/{RoomID}/recent_messages?client_id=7ue61iz46fz11y3cugd0l3tawb4taal");
                            using (var response = request.GetResponse())
                            using (var stream = response.GetResponseStream())
                            {
                                JsonParser parser = new JsonParser();

                                dynamic json = parser.Parse(stream);

                                dynamic _messages = json["messages"];

                                foreach (string s in _messages)
                                {
                                    IrcMessage msg;

                                    if (IrcMessage.TryParse(s, out msg))
                                    {
                                        messages.Add(new Message(msg, this));
                                    }
                                }

                                ;

                                //StreamReader reader = new StreamReader(stream);
                                //string line;
                                //while ((line = reader.ReadLine()) != null)
                                //{
                                //    IrcMessage msg;

                                //    if (IrcMessage.TryParse(line, out msg))
                                //    {
                                //        if (msg.Params != null)
                                //            messages.Add(new Message(msg, this, false, false));
                                //    }
                                //}
                            }

                            AddMessagesAtStart(messages.ToArray());
                        }
                        catch { }
                    }
                });

                // bttv channel emotes
                Task.Run(() =>
                {
                    try
                    {
                        JsonParser parser = new JsonParser();

                        //if (!File.Exists(bttvChannelEmotesCache))
                        {
                            try
                            {
                                if (Util.IsLinux)
                                {
                                    Util.LinuxDownloadFile("https://api.betterttv.net/2/channels/" + channelName, bttvChannelEmotesCache);
                                }
                                else
                                {
                                    using (var webClient = new WebClient())
                                    using (var readStream = webClient.OpenRead("https://api.betterttv.net/2/channels/" + channelName))
                                    using (var writeStream = File.OpenWrite(bttvChannelEmotesCache))
                                    {
                                        readStream.CopyTo(writeStream);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                e.Message.Log("emotes");
                            }
                        }

                        using (var stream = File.OpenRead(bttvChannelEmotesCache))
                        {
                            dynamic json = parser.Parse(stream);
                            var template = "https:" + json["urlTemplate"]; //{{id}} {{image}}

                            foreach (dynamic e in json["emotes"])
                            {
                                string id = e["id"];
                                string code = e["code"];
                                string channel = e["channel"];

                                TwitchEmote emote;
                                if (Emotes.BttvChannelEmotesCache.TryGetValue(id, out emote))
                                {
                                    BttvChannelEmotes[code] = emote;
                                }
                                else
                                {
                                    string imageType = e["imageType"];
                                    string url = template.Replace("{{id}}", id).Replace("{{image}}", "1x");
                                    Emotes.BttvChannelEmotesCache[id] = BttvChannelEmotes[code] = new TwitchEmote { Name = code, Url = url, Tooltip = code + "\nBetterTTV Channel Emote\nChannel: " + channel };
                                }
                            }
                        }
                        updateEmoteNameList();
                    }
                    catch { }
                });

                // ffz channel emotes
                Task.Run(() =>
                {
                    try
                    {
                        JsonParser parser = new JsonParser();

                        //if (!File.Exists(ffzChannelEmotesCache))
                        {
                            try
                            {
                                if (Util.IsLinux)
                                {
                                    Util.LinuxDownloadFile("http://api.frankerfacez.com/v1/room/" + channelName, ffzChannelEmotesCache);
                                }
                                else
                                {
                                    using (var webClient = new WebClient())
                                    using (var readStream = webClient.OpenRead("http://api.frankerfacez.com/v1/room/" + channelName))
                                    using (var writeStream = File.OpenWrite(ffzChannelEmotesCache))
                                    {
                                        readStream.CopyTo(writeStream);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                e.Message.Log("emotes");
                            }
                        }

                        using (var stream = File.OpenRead(ffzChannelEmotesCache))
                        {
                            dynamic json = parser.Parse(stream);

                            dynamic room = json["room"];

                            try
                            {
                                object moderator;

                                if (room.TryGetValue("moderator_badge", out moderator))
                                {
                                    if (moderator != null && !string.IsNullOrWhiteSpace((string)moderator))
                                    {
                                        var url = "https:" + (moderator as string);
                                        ModeratorBadge = new TwitchEmote
                                        {
                                            Url = url,
                                            Tooltip = "custom moderator badge\nFFZ",
                                            LoadAction = () =>
                                            {
                                                try
                                                {
                                                    object img;

                                                    WebRequest request = WebRequest.Create(url);
                                                    using (var response = request.GetResponse())
                                                    using (var s = response.GetResponseStream())
                                                    {
                                                        img = GuiEngine.Current.ReadImageFromStream(s);
                                                    }

                                                    GuiEngine.Current.FreezeImage(img);

                                                    return GuiEngine.Current.DrawImageBackground(img, HSLColor.FromRGB(0x45A41E));
                                                }
                                                catch
                                                {
                                                    return null;
                                                }
                                            }
                                        };
                                    }
                                }
                            }
                            catch { }

                            dynamic sets = json["sets"];

                            foreach (dynamic set in sets.Values)
                            {
                                string title = set["title"];

                                dynamic emoticons = set["emoticons"];

                                foreach (dynamic emoticon in emoticons)
                                {
                                    string code = emoticon["name"];
                                    string id = emoticon["id"];
                                    dynamic owner = emoticon["owner"];
                                    string ownerName = owner["display_name"];

                                    dynamic urls = emoticon["urls"];

                                    string url = "http:" + urls["1"];

                                    TwitchEmote emote;
                                    if (Emotes.FfzChannelEmotesCache.TryGetValue(id, out emote))
                                    {
                                        FfzChannelEmotes[code] = emote;
                                    }
                                    else
                                    {
                                        Emotes.FfzChannelEmotesCache[id] = FfzChannelEmotes[code] = new TwitchEmote { Name = code, Url = url, Tooltip = code + "\nFFZ Channel Emote\nChannel: " + ownerName };
                                    }
                                }
                            }
                        }
                        updateEmoteNameList();
                    }
                    catch { }
                });

                // get chatters
                Task.Run(() =>
                {
                    try
                    {
                        var request = WebRequest.Create($"http://tmi.twitch.tv/group/user/{channelName}/chatters");
                        using (var response = request.GetResponse())
                        using (var stream = response.GetResponseStream())
                        {
                            JsonParser parser = new JsonParser();
                            dynamic json = parser.Parse(stream);
                            dynamic chatters = json["chatters"];
                            foreach (dynamic group in chatters)
                            {
                                foreach (string user in group.Value)
                                {
                                    Users[user.ToUpper()] = user;
                                }
                            }
                        }
                    }
                    catch { }
                });
            }

            Emotes.EmotesLoaded += Emotes_EmotesLoaded;
            IrcManager.Connected += IrcManager_Connected;
            IrcManager.Disconnected += IrcManager_Disconnected;
            IrcManager.NoticeAdded += IrcManager_NoticeAdded;
            AppSettings.MessageLimitChanged += AppSettings_MessageLimitChanged;
            AppSettings.FontChanged += AppSettings_FontChanged;
        }

        private void AppSettings_FontChanged(object sender, EventArgs e)
        {
            lock (MessageLock)
            {
                foreach (Message msg in Messages)
                {
                    msg.InvalidateTextMeasurements();
                }
            }
        }

        private void IrcManager_Connected(object sender, EventArgs e)
        {
            AddMessage(new Message("connected to chat", HSLColor.Gray, true));
        }

        private void IrcManager_Disconnected(object sender, EventArgs e)
        {
            AddMessage(new Message("disconnected from chat", HSLColor.Gray, true));
        }

        private void IrcManager_NoticeAdded(object sender, ValueEventArgs<string> e)
        {
            AddMessage(new Message(e.Value, HSLColor.Gray, true));
        }

        private void AppSettings_MessageLimitChanged(object sender, EventArgs e)
        {
            Message[] _messages = null;

            lock (MessageLock)
            {
                if (Messages.Length > AppSettings.ChatMessageLimit)
                {
                    _messages = new Message[Messages.Length - AppSettings.ChatMessageLimit];
                    Array.Copy(Messages, _messages, _messages.Length);

                    Message[] M = new Message[AppSettings.ChatMessageLimit];
                    Array.Copy(Messages, Messages.Length - AppSettings.ChatMessageLimit, M, 0, AppSettings.ChatMessageLimit);

                    Messages = M;
                }
            }

            if (_messages != null)
                MessagesRemovedAtStart?.Invoke(this, new ValueEventArgs<Message[]>(_messages));
        }

        private void Emotes_EmotesLoaded(object sender, EventArgs e)
        {
            updateEmoteNameList();
        }


        // Channels
        static TwitchChannel()
        {
            WhisperChannel?.AddMessage(new Message("Please note that chatterino can only read whispers while it is running!", null, true));
        }

        private static ConcurrentDictionary<string, TwitchChannel> channels = new ConcurrentDictionary<string, TwitchChannel>();
        public static IEnumerable<TwitchChannel> Channels { get { return channels.Values; } }

        public static TwitchChannel WhisperChannel { get; private set; } = new TwitchChannel("/whispers");

        public static TwitchChannel AddChannel(string channelName)
        {
            if (channelName.StartsWith("/"))
            {
                if (channelName == "/whispers")
                {
                    return WhisperChannel;
                }
            }

            return channels.AddOrUpdate((channelName ?? "").ToLower(), cname => new TwitchChannel(cname) { Uses = 1 }, (cname, c) => { c.Uses++; return c; });
        }

        public static void RemoveChannel(string channelName)
        {
            if (channelName == null)
                return;

            channelName = channelName.ToLower();

            TwitchChannel data;
            if (channels.TryGetValue(channelName ?? "", out data))
            {
                data.Uses--;
                if (data.Uses <= 0)
                {
                    data.Disconnect();
                    data.Dispose();
                    channels.TryRemove(channelName ?? "", out data);
                }
            }
        }

        public static TwitchChannel GetChannel(string channelName)
        {
            channelName = channelName.ToLower();

            TwitchChannel data;
            if (channels.TryGetValue(channelName ?? "", out data))
                return data;

            return null;
        }


        // Emote + Name Autocompletion
        public ConcurrentDictionary<string, string> Users = new ConcurrentDictionary<string, string>();

        List<KeyValuePair<string, string>> emoteNames = new List<KeyValuePair<string, string>>();

        void updateEmoteNameList()
        {
            List<KeyValuePair<string, string>> names = new List<KeyValuePair<string, string>>();

            names.AddRange(Emotes.TwitchEmotes.Keys.Select(x => new KeyValuePair<string, string>(x.ToUpper(), x)));
            names.AddRange(Emotes.BttvGlobalEmotes.Keys.Select(x => new KeyValuePair<string, string>(x.ToUpper(), x)));
            names.AddRange(Emotes.FfzGlobalEmotes.Keys.Select(x => new KeyValuePair<string, string>(x.ToUpper(), x)));
            names.AddRange(BttvChannelEmotes.Keys.Select(x => new KeyValuePair<string, string>(x.ToUpper(), x)));
            names.AddRange(FfzChannelEmotes.Keys.Select(x => new KeyValuePair<string, string>(x.ToUpper(), x)));
            names.AddRange(Emojis.ShortCodeToEmoji.Keys.Select(x => new KeyValuePair<string, string>(":" + x.ToUpper() + ":", ":" + x + ":")));

            emoteNames = names;
        }

        public IEnumerable<KeyValuePair<string, string>> GetCompletionItems(bool firstWord, bool allowAt)
        {
            var names = new List<KeyValuePair<string, string>>(emoteNames);

            string commaAtEnd = firstWord ? "," : "";

            if (AppSettings.ChatMentionUsersWithAt)
            {
                names.AddRange(Users.Select(x => new KeyValuePair<string, string>(x.Key, (allowAt ? "@" : "") + (!AppSettings.ChatTabLocalizedNames && !string.Equals(x.Value, x.Key, StringComparison.OrdinalIgnoreCase) ? x.Key : x.Value) + commaAtEnd)));
                names.AddRange(Users.Select(x => new KeyValuePair<string, string>((allowAt ? "@" : "") + x.Key, (allowAt ? "@" : "") + (!AppSettings.ChatTabLocalizedNames && !string.Equals(x.Value, x.Key, StringComparison.OrdinalIgnoreCase) ? x.Key : x.Value) + commaAtEnd)));
            }
            else
            {
                names.AddRange(Users.Select(x => new KeyValuePair<string, string>(x.Key, (!AppSettings.ChatTabLocalizedNames && !string.Equals(x.Value, x.Key, StringComparison.OrdinalIgnoreCase) ? x.Key : x.Value) + commaAtEnd)));
            }
            names.Sort((x1, x2) => x1.Key.CompareTo(x2.Key));

            return names;
        }

        public void Join()
        {
            IrcManager.Client?.Join("#" + Name);
        }

        public void Disconnect()
        {
            IrcManager.Client?.Part("#" + Name);
        }

        public void SendMessage(string text)
        {
            //if (Name == "/whispers")
            //    IrcManager.SendMessage("jtv", text, IsModOrBroadcaster);
            //else
            IrcManager.SendMessage(this, text, IsModOrBroadcaster);
        }


        // Messages
        public event EventHandler<ChatClearedEventArgs> ChatCleared;
        public event EventHandler<MessageAddedEventArgs> MessageAdded;
        public event EventHandler<ValueEventArgs<Message[]>> MessagesAddedAtStart;
        public event EventHandler<ValueEventArgs<Message[]>> MessagesRemovedAtStart;

        public int MessageCount { get; private set; } = 0;

        private Message[] _messages = new Message[0];

        public Message[] Messages
        {
            get { return _messages; }
            set { _messages = value; }
        }

        public Message[] CloneMessages()
        {
            Message[] M;
            lock (MessageLock)
            {
                M = new Message[_messages.Length];
                Array.Copy(_messages, M, M.Length);
            }
            return M;
        }

        public object MessageLock { get; private set; } = new object();

        public void ClearChat()
        {
            Message[] _messages = Messages;

            lock (MessageLock)
            {
                Messages = new Message[0];
            }

            MessagesRemovedAtStart?.Invoke(this, new ValueEventArgs<Message[]>(_messages));
        }

        public void ClearChat(string user, string reason, int duration)
        {
            lock (MessageLock)
            {
                foreach (Message msg in Messages)
                {
                    if (msg.Username == user)
                    {
                        msg.Disabled = true;
                    }
                }
            }

            AddMessage(new Message($"{user} was timed out for {duration} second{(duration != 1 ? "s" : "")}: \"{reason}\""));

            ChatCleared?.Invoke(this, new ChatClearedEventArgs(user, reason, duration));
        }

        public void AddMessage(Message message)
        {
            Message[] M;
            Message removedMessage = null;

            lock (MessageLock)
            {
                if (Messages.Length == maxMessages)
                {
                    removedMessage = Messages[0];
                    M = new Message[maxMessages];
                    Array.Copy(Messages, 1, M, 0, Messages.Length - 1);
                }
                else
                {
                    M = new Message[Messages.Length + 1];
                    Array.Copy(Messages, M, Messages.Length);
                }

                M[M.Length - 1] = message;
                Messages = M;
                MessageCount = M.Length;
            }

            MessageAdded?.Invoke(this, new MessageAddedEventArgs(message, removedMessage));
        }

        public void AddMessagesAtStart(Message[] messages)
        {
            Message[] M;

            lock (MessageLock)
            {
                if (Messages.Length == maxMessages)
                    return;

                if (messages.Length + Messages.Length <= maxMessages)
                {
                    M = new Message[messages.Length + Messages.Length];

                    Array.Copy(Messages, 0, M, messages.Length, Messages.Length);
                    Array.Copy(messages, 0, M, 0, messages.Length);
                }
                else
                {
                    M = new Message[maxMessages];

                    Array.Copy(Messages, 0, M, maxMessages - Messages.Length, Messages.Length);

                    Message[] _messages = new Message[maxMessages - Messages.Length];

                    Array.Copy(messages, messages.Length - maxMessages + Messages.Length, M, 0, maxMessages - Messages.Length);
                    Array.Copy(messages, messages.Length - maxMessages + Messages.Length, _messages, 0, maxMessages - Messages.Length);

                    messages = _messages;
                }
                Messages = M;
                MessageCount = M.Length;
            }

            MessagesAddedAtStart?.Invoke(this, new ValueEventArgs<Message[]>(messages));
        }

        public void Dispose()
        {
            Emotes.EmotesLoaded -= Emotes_EmotesLoaded;
            IrcManager.Connected -= IrcManager_Connected;
            IrcManager.Disconnected -= IrcManager_Disconnected;
            IrcManager.NoticeAdded -= IrcManager_NoticeAdded;
            AppSettings.MessageLimitChanged -= AppSettings_MessageLimitChanged;
        }
    }
}
