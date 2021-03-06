﻿using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using PKHeX.Core;

namespace SysBot.Pokemon.Discord
{
    [Summary("Queues new Dudu trades")]
    public class DuduModule : ModuleBase<SocketCommandContext>
    {
        private static TradeQueueInfo<PK8> Info => SysCordInstance.Self.Hub.Queues.Info;

        [Command("seedCheck")]
        [Alias("dudu", "d", "sc")]
        [Summary("Checks the seed for a Pokémon.")]
        [RequireQueueRole(nameof(DiscordManager.RolesDudu))]
        public async Task SeedCheckAsync(int code)
        {
            var sudo = Context.User.GetIsSudo();
            await Context.AddToQueueAsync(code, Context.User.Username, sudo, new PK8(), PokeRoutineType.DuduBot, PokeTradeType.Dudu).ConfigureAwait(false);
        }

        [Command("seedCheck")]
        [Alias("dudu", "d", "sc")]
        [Summary("Checks the seed for a Pokémon.")]
        [RequireQueueRole(nameof(DiscordManager.RolesDudu))]
        public async Task SeedCheckAsync()
        {
            var code = Info.GetRandomTradeCode();
            await SeedCheckAsync(code).ConfigureAwait(false);
        }

        [Command("duduList")]
        [Alias("dl", "scq", "seedCheckQueue", "duduQueue", "seedList")]
        [Summary("Prints the users in the Seed Check queue.")]
        [RequireSudo]
        public async Task GetSeedListAsync()
        {
            string msg = Info.GetTradeList(PokeRoutineType.DuduBot);
            var embed = new EmbedBuilder();
            embed.AddField(x =>
            {
                x.Name = "Pending Trades";
                x.Value = msg;
                x.IsInline = false;
            });
            await ReplyAsync("These are the users who are currently waiting:", embed: embed.Build()).ConfigureAwait(false);
        }

        [Command("findFrame")]
        [Alias("ff", "getFrameData")]
        [Summary("Prints the next shiny frame from the provided seed.")]
        public async Task FindFrameAsync([Remainder]string seedString)
        {
            seedString = seedString.ToLower();
            if (seedString.StartsWith("0x"))
                seedString = seedString.Substring(2);

            var seed = Util.GetHexValue64(seedString);

            var r = new Z3SeedResult(Z3SearchResult.Success, seed, -1);
            var type = r.GetShinyType();
            var msg = r.ToString();

            var embed = new EmbedBuilder {Color = type == Shiny.AlwaysStar ? Color.Gold : Color.LighterGrey};

            embed.AddField(x =>
            {
                x.Name = "Seed Result";
                x.Value = msg;
                x.IsInline = false;
            });
            await ReplyAsync($"Here's your seed details for `{seed:X16}`:", embed: embed.Build()).ConfigureAwait(false);
        }
    }
}
