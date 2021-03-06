﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;
using PKHeX.Core;
using SysBot.Base;
using TwitchLib.Communication.Events;

namespace SysBot.Pokemon.Twitch
{
    public class TwitchBot
    {
        private static PokeTradeHub<PK8> Hub;
        internal static TradeQueueInfo<PK8> Info => Hub.Queues.Info;

        internal static readonly List<TwitchQueue> QueuePool = new List<TwitchQueue>();
        private readonly TwitchClient client;
        private readonly string Channel;
        private readonly TwitchSettings Settings;

        public TwitchBot(TwitchSettings settings, PokeTradeHub<PK8> hub)
        {
            Hub = hub;
            Settings = settings;

            var credentials = new ConnectionCredentials(settings.Username, settings.Token);
            AutoLegalityWrapper.EnsureInitialized(Hub.Config.Legality);

            var clientOptions = new ClientOptions
            {
                MessagesAllowedInPeriod = settings.ThrottleMessages,
                ThrottlingPeriod = TimeSpan.FromSeconds(settings.ThrottleSeconds),
            };

            Channel = settings.Channel;
            WebSocketClient customClient = new WebSocketClient(clientOptions);
            client = new TwitchClient(customClient);

            var cmd = settings.CommandPrefix;
            client.Initialize(credentials, Channel, cmd, cmd);

            client.OnLog += Client_OnLog;
            client.OnJoinedChannel += Client_OnJoinedChannel;
            client.OnMessageReceived += Client_OnMessageReceived;
            client.OnWhisperReceived += Client_OnWhisperReceived;
            client.OnChatCommandReceived += Client_OnChatCommandReceived;
            client.OnWhisperCommandReceived += Client_OnWhisperCommandReceived;
            client.OnNewSubscriber += Client_OnNewSubscriber;
            client.OnConnected += Client_OnConnected;
            client.OnDisconnected += Client_OnDisconnected;

            client.OnMessageThrottled += (_, e)
                => LogUtil.LogError($"Message Throttled: {e.Message}", "TwitchBot");
            client.OnWhisperThrottled += (_, e)
                => LogUtil.LogError($"Whisper Throttled: {e.Message}", "TwitchBot");

            client.OnError += (_, e) =>
                LogUtil.LogError(e.Exception.Message + Environment.NewLine + e.Exception.StackTrace, "TwitchBot");
            client.OnConnectionError += (_, e) =>
                LogUtil.LogError(e.BotUsername + Environment.NewLine + e.Error.Message, "TwitchBot");

            client.Connect();

            if (settings.GenerateAssets)
                AddAssetGeneration();

            EchoUtil.Forwarders.Add(msg => client.SendMessage(Channel, msg));
            Hub.Queues.Forwarders.Add((bot, detail) => client.SendMessage(Channel, $"{bot.Connection.Name} is now trading (ID {detail.ID}) {detail.Trainer.TrainerName}"));
        }

        private void AddAssetGeneration()
        {
            void Create(PokeTradeBot b, PokeTradeDetail<PK8> detail)
            {
                try
                {
                    var file = b.Connection.IP;
                    var name = $"(ID {detail.ID}) {detail.Trainer.TrainerName}";
                    File.WriteAllText($"{file}.txt", name);

                    var next = Hub.Queues.Info.GetUserList("(ID {0}) - {3}");
                    next = next.Skip(Settings.OnDeckCountSkip).Take(Settings.OnDeckCount); // filter down
                    var separator = Hub.Config.Twitch.OnDeckSeparator;
                    File.WriteAllText("ondeck.txt", string.Join(separator, next));
                }
                catch (Exception e)
                {
                    LogUtil.LogError(e.Message, "TwitchBot");
                }
            }
            Info.Hub.Queues.Forwarders.Add(Create);
        }

        public void StartingDistribution(string message)
        {
            Task.Run(async () =>
            {
                client.SendMessage(Channel, "5...");
                await Task.Delay(1_000).ConfigureAwait(false);
                client.SendMessage(Channel, "4...");
                await Task.Delay(1_000).ConfigureAwait(false);
                client.SendMessage(Channel, "3...");
                await Task.Delay(1_000).ConfigureAwait(false);
                client.SendMessage(Channel, "2...");
                await Task.Delay(1_000).ConfigureAwait(false);
                client.SendMessage(Channel, "1...");
                await Task.Delay(1_000).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(message))
                    client.SendMessage(Channel, message);
            });
        }

        private bool AddToTradeQueue(PK8 pk8, int code, OnWhisperReceivedArgs e, bool sudo, PokeRoutineType type, out string msg)
        {
            // var user = e.WhisperMessage.UserId;
            var userID = ulong.Parse(e.WhisperMessage.UserId);
            var name = e.WhisperMessage.DisplayName;

            var trainer = new PokeTradeTrainerInfo(name);
            var notifier = new TwitchTradeNotifier<PK8>(pk8, trainer, code, e.WhisperMessage.Username, client, Channel);
            var tt = type == PokeRoutineType.DuduBot ? PokeTradeType.Dudu : PokeTradeType.Specific;
            var detail = new PokeTradeDetail<PK8>(pk8, trainer, notifier, tt, code: code);
            var trade = new TradeEntry<PK8>(detail, userID, type, name);

            var added = Info.AddToTradeQueue(trade, userID, sudo);

            if (added == QueueResultAdd.AlreadyInQueue)
            {
                msg = "Sorry, you are already in the queue.";
                return false;
            }

            var position = Info.CheckPosition(userID, type);
            msg = $"{name} - Added to the {type} queue, unique ID: {detail.ID}. Current Position: {position.Position}";

            var botct = Info.Hub.Bots.Count;
            if (position.Position > botct)
            {
                var eta = Info.Hub.Config.Queues.EstimateDelay(position.Position, botct);
                msg += $". Estimated: {eta:F1} minutes.";
            }
            return true;
        }

        private void Client_OnLog(object sender, OnLogArgs e)
        {
            LogUtil.LogText($"[{client.TwitchUsername}] -[{e.BotUsername}] {e.Data}");
        }

        private void Client_OnConnected(object sender, OnConnectedArgs e)
        {
            LogUtil.LogText($"[{client.TwitchUsername}] - Connected {e.AutoJoinChannel} as {e.BotUsername}");
        }

        private void Client_OnDisconnected(object sender, OnDisconnectedEventArgs e)
        {
            LogUtil.LogText($"[{client.TwitchUsername}] - Disconnected.");
        }

        private void Client_OnJoinedChannel(object sender, OnJoinedChannelArgs e)
        {
            LogUtil.LogInfo($"Joined {e.Channel}", e.BotUsername);
            client.SendMessage(e.Channel, "Connected!");
        }

        private void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            LogUtil.LogText($"[{client.TwitchUsername}] - @{e.ChatMessage.Username}: {e.ChatMessage.Message}");
        }

        private void Client_OnChatCommandReceived(object sender, OnChatCommandReceivedArgs e)
        {
            if (!Hub.Config.Twitch.AllowCommandsViaChannel || Hub.Config.Twitch.UserBlacklist.Contains(e.Command.ChatMessage.Username))
                return;

            var msg = e.Command.ChatMessage;
            var c = e.Command.CommandText.ToLower();
            var args = e.Command.ArgumentsAsString;
            var response = HandleCommand(msg, c, args);
            if (response == null)
                return;

            var channel = e.Command.ChatMessage.Channel;
            client.SendMessage(channel, response);
        }

        private void Client_OnWhisperCommandReceived(object sender, OnWhisperCommandReceivedArgs e)
        {
            if (!Hub.Config.Twitch.AllowCommandsViaWhisper || Hub.Config.Twitch.UserBlacklist.Contains(e.Command.WhisperMessage.Username))
                return;

            var msg = e.Command.WhisperMessage;
            var c = e.Command.CommandText.ToLower();
            var args = e.Command.ArgumentsAsString;
            var response = HandleCommand(msg, c, args);
            if (response == null)
                return;

            client.SendWhisper(msg.Username, response);
        }

        private static bool IsSubscriber(ChatMessage c) => c.IsSubscriber || IsFounder(c);
        private static bool IsFounder(ChatMessage c) => c.BadgeInfo.Any(kvp => kvp.Key == "founder");

        private string HandleCommand(TwitchLibMessage m, string c, string args)
        {
            bool sudo() => m is ChatMessage ch && (ch.IsBroadcaster || Settings.IsSudo(m.Username));
            bool disallowed() => Settings.SubOnlyBot && !((m is ChatMessage ch && IsSubscriber(ch)) || sudo());

            switch (c)
            {
                // User Usable Commands
                case "trade" when !disallowed():
                    var _ = TwitchCommandsHelper.AddToWaitingList(args, m.DisplayName, m.Username, out string msg);
                    return msg;
                case "ts" when !disallowed():
                    return Info.GetPositionString(ulong.Parse(m.UserId));
                case "tc" when !disallowed():
                    return TwitchCommandsHelper.ClearTrade(ulong.Parse(m.UserId));

                // Sudo Only Commands
                case "tca" when !sudo():
                case "pr" when !sudo():
                case "pc" when !sudo():
                case "tt" when !sudo():
                case "tcu" when !sudo():
                    return "This command is locked for sudo users only!";

                case "tca":
                    Info.ClearAllQueues();
                    return "Cleared all queues!";

                case "pr":
                    return Info.Hub.Ledy.Pool.Reload() ? $"Reloaded from folder. Pool count: {Info.Hub.Ledy.Pool.Count}" : "Failed to reload from folder.";

                case "pc":
                    return $"The pool count is: {Info.Hub.Ledy.Pool.Count}";

                case "tt":
                    return Info.Hub.Queues.Info.ToggleQueue()
                        ? "Users are now able to join the trade queue."
                        : "Changed queue settings: **Users CANNOT join the queue until it is turned back on.**";

                case "tcu":
                    return TwitchCommandsHelper.ClearTrade(args);

                default: return null;
            }
        }

        private void Client_OnWhisperReceived(object sender, OnWhisperReceivedArgs e)
        {
            LogUtil.LogText($"[{client.TwitchUsername}] - @{e.WhisperMessage.Username}: {e.WhisperMessage.Message}");
            if (QueuePool.Count > 100)
            {
                var removed = QueuePool[0];
                QueuePool.RemoveAt(0); // First in, first out
                client.SendMessage(Channel, $"Removed {removed.DisplayName} from the waiting list. (list exceeded maximum count)");
            }

            var user = QueuePool.Find(q => q.UserName == e.WhisperMessage.Username);
            if (user == null)
                return;
            QueuePool.Remove(user);
            var msg = e.WhisperMessage.Message;
            try
            {
                int code = int.Parse(msg);
                var _ = AddToTradeQueue(user.Pokemon, code, e, Settings.IsSudo(user.UserName), PokeRoutineType.LinkTrade, out string message);
                client.SendMessage(Channel, message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
        }

        private void Client_OnNewSubscriber(object sender, OnNewSubscriberArgs e)
        {
        }
    }
}
