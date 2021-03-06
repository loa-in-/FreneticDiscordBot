using System;
using System.Text;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord.Net;
using Discord;
using Discord.WebSocket;
using System.Diagnostics;

public partial class Program
{
    public static Random random = new Random();

    public static DiscordSocketClient client;

    public static readonly string TOKEN = File.ReadAllText("./conf.txt");

    public const string POSITIVE_PREFIX = "+> ";

    public const string NEGATIVE_PREFIX = "-> ";

    public const string TODO_PREFIX = NEGATIVE_PREFIX + "// TODO: ";

    public static string[] Quotes = File.ReadAllText("./quotes.txt").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n\n", ((char)0x01).ToString()).Split((char)0x01);

    public static void Respond(SocketMessage message)
    {
        string[] mesdat = message.Content.Split(' ');
        StringBuilder resBuild = new StringBuilder(message.Content.Length);
        List<string> cmds = new List<string>();
        for (int i = 0; i < mesdat.Length; i++)
        {
            if (mesdat[i].Contains("<") && mesdat[i].Contains(">"))
            {
                continue;
            }
            resBuild.Append(mesdat[i]).Append(" ");
            if (mesdat[i].Length > 0)
            {
                cmds.Add(mesdat[i]);
            }
        }
        if (cmds.Count == 0)
        {
            Console.WriteLine("Empty input, ignoring: " + message.Author.Username);
            return;
        }
        string fullMsg = resBuild.ToString();
        Console.WriteLine("Found input from: (" + message.Author.Username + "), in channel: " + message.Channel.Name + ": " + fullMsg);
        string lowCmd = cmds[0].ToLowerInvariant();
        cmds.RemoveAt(0);
        if (CommonCmds.TryGetValue(lowCmd, out Action<string[], SocketMessage> acto))
        {
            acto.Invoke(cmds.ToArray(), message);
        }
        else
        {
            message.Channel.SendMessageAsync(NEGATIVE_PREFIX + "Unknown command. Consider the __**help**__ command?").Wait();
        }
    }

    public static Dictionary<string, Action<string[], SocketMessage>> CommonCmds = new Dictionary<string, Action<string[], SocketMessage>>(1024);

    public class QuoteSeen
    {
        public int QID;
        public DateTime Time;
    }

    public static List<QuoteSeen> QuotesSeen = new List<QuoteSeen>();

    public static bool QuoteWasSeen(int qid)
    {
        for (int i = 0; i < QuotesSeen.Count; i++)
        {
            if (QuotesSeen[i].QID == qid)
            {
                return true;
            }
        }
        return false;
    }

    static void CMD_ShowQuote(string[] cmds, SocketMessage message)
    {
        for (int i = QuotesSeen.Count - 1; i >= 0; i--)
        {
            if (DateTime.UtcNow.Subtract(QuotesSeen[i].Time).TotalMinutes >= 5)
            {
                QuotesSeen.RemoveAt(i);
            }
        }
        int qid = -1;
        if (cmds.Length == 0)
        {
            for (int i = 0; i < 15; i++)
            {
                qid = random.Next(Quotes.Length);
                if (!QuoteWasSeen(qid))
                {
                    break;
                }
            }
        }
        else if (int.TryParse(cmds[0], out qid))
        {
            qid--;
            if (qid < 0)
            {
                qid = 0;
            }
            if (qid >= Quotes.Length)
            {
                qid = Quotes.Length - 1;
            }
        }
        else
        {
            List<int> spots = new List<int>();
            string input_opt = string.Join(" ", cmds);
            for (int i = 0; i < Quotes.Length; i++)
            {
                if (Quotes[i].ToLowerInvariant().Contains(input_opt.ToLowerInvariant()))
                {
                    spots.Add(i);
                }
            }
            if (spots.Count == 0)
            {
                message.Channel.SendMessageAsync(NEGATIVE_PREFIX + "Unable to find that quote! Sorry :(").Wait();
                return;
            }
            for (int s = 0; s < 15; s++)
            {
                int temp = random.Next(spots.Count);
                qid = spots[temp];
                if (!QuoteWasSeen(qid))
                {
                    break;
                }
            }
        }
        if (qid >= 0 && qid < Quotes.Length)
        {
            QuotesSeen.Add(new QuoteSeen() { QID = qid, Time = DateTime.UtcNow });
            string quoteRes = POSITIVE_PREFIX + "Quote **" + (qid + 1) + "**:\n```xml\n" + Quotes[qid] + "\n```\n";
            message.Channel.SendMessageAsync(quoteRes).Wait();
        }
    }

    public static string CmdsHelp = 
            "`help`, `quote`, `hello`, `restart`, `listeninto`, `frenetic`, `whois`, "
            + "...";

    static void CMD_Help(string[] cmds, SocketMessage message)
    {
            message.Channel.SendMessageAsync(POSITIVE_PREFIX + "Available Commands:\n" + CmdsHelp).Wait();
    }

    static void CMD_Hello(string[] cmds, SocketMessage message)
    {
            message.Channel.SendMessageAsync(POSITIVE_PREFIX + "Hi! I'm a bot! Find my source code at https://github.com/FreneticLLC/FreneticDiscordBot").Wait();
    }

    static void CMD_SelfInfo(string[] cmds, SocketMessage message)
    {
        SocketUser user = message.Author;
        foreach (SocketUser tuser in message.MentionedUsers)
        {
            if (tuser.Id != client.CurrentUser.Id)
            {
                user = tuser;
                break;
            }
        }
        EmbedBuilder bed = new EmbedBuilder();
        EmbedAuthorBuilder auth = new EmbedAuthorBuilder();
        auth.Name = user.Username + "#" + user.Discriminator;
        auth.IconUrl = user.GetAvatarUrl();
        auth.Url = user.GetAvatarUrl();
        bed.Author = auth;
        bed.Color = new Color(0xC8, 0x74, 0x4B);
        bed.Title = "Who is " + auth.Name + "?";
        bed.Description = auth.Name + " is a Discord user!";
        bed.AddField((efb) => efb.WithName("When did they join Discord?").WithValue(FormatDT(user.CreatedAt)));
        bed.AddField((efb) => efb.WithName("What are they playing right now?").WithValue(user.Game.HasValue ? user.Game.Value.Name : "Nothing."));
        StringBuilder roleBuilder = new StringBuilder();
        foreach (SocketRole role in (user as SocketGuildUser).Roles) 
        {
            if (!role.IsEveryone)
            {
                roleBuilder.Append(", " + role.Name);
            }
        }
        bed.AddField((efb) => efb.WithName("What roles are they assigned here?").WithValue(roleBuilder.Length > 0 ? roleBuilder.ToString().Substring(2) : "None currently."));
        bed.Footer = new EmbedFooterBuilder().WithIconUrl(client.CurrentUser.GetAvatarUrl()).WithText("Info provided by FreneticDiscordBot, which is Copyright (C) Frenetic LLC");
        message.Channel.SendMessageAsync(POSITIVE_PREFIX, embed: bed.Build()).Wait();
    }

    static bool IsBotCommander(SocketUser usr)
    {
        return (usr as SocketGuildUser).Roles.Where((role) => role.Name.ToLowerInvariant() =="botcommander").FirstOrDefault() != null;
    }

    static void CMD_Restart(string[] cmds, SocketMessage message)
    {
        // NOTE: This implies a one-guild bot. A multi-guild bot probably shouldn't have this "BotCommander" role-based verification.
        // But under current scale, a true-admin confirmation isn't worth the bother.
        if (!IsBotCommander(message.Author))
        {
            message.Channel.SendMessageAsync(NEGATIVE_PREFIX + "Nope! That's not for you!").Wait();
            return;
        }
        if (!File.Exists("./start.sh"))
        {
            message.Channel.SendMessageAsync(NEGATIVE_PREFIX + "Nope! That's not valid for my current configuration!").Wait();
        }
        message.Channel.SendMessageAsync(POSITIVE_PREFIX + "Yes, boss. Restarting now...").Wait();
        Process.Start("sh", "./start.sh " + message.Channel.Id);
        Task.Factory.StartNew(() =>
        {
            Console.WriteLine("Shutdown start...");
            for (int i = 0; i < 15; i++)
            {
                Console.WriteLine("T Minus " + (15 - i));
                Task.Delay(1000).Wait();
            }
            Console.WriteLine("Shutdown!");
            Environment.Exit(0);
        });
        client.StopAsync().Wait();
    }

    static string Pad2(int num)
    {
        return num < 10 ? "0" + num : num.ToString();
    }

    static string AddPlus(double d)
    {
        return d < 0 ? d.ToString() : "+" + d;
    }

    static string FormatDT(DateTimeOffset dtoff)
    {
        return dtoff.Year + "/" + Pad2(dtoff.Month) + "/" + Pad2(dtoff.Day)
        + " " + Pad2(dtoff.Hour) + ":" + Pad2(dtoff.Minute) + ":" + Pad2(dtoff.Second)
         + " UTC" + AddPlus(dtoff.Offset.TotalHours);
    }

    static void CMD_WhatIsFrenetic(string[] cmds, SocketMessage message)
    {
        EmbedBuilder bed = new EmbedBuilder();
        EmbedAuthorBuilder auth = new EmbedAuthorBuilder();
        auth.Name = "Frenetic LLC";
        auth.IconUrl = client.CurrentUser.GetAvatarUrl();
        auth.Url = "https://freneticllc.com";
        bed.Author = auth;
        bed.Color = new Color(0xC8, 0x74, 0x4B);
        bed.Title = "What is Frenetic LLC?";
        bed.Description = "Frenetic LLC is a California registered limited liability company.";
        bed.AddField((efb) => efb.WithName("What does Frenetic LLC do?").WithValue("In short: We make games!"));
        bed.AddField((efb) => efb.WithName("Who is Frenetic LLC?").WithValue("We are an international team! Check out the #meet-the-team channel on the Frenetic LLC official Discord!"));
        bed.Footer = new EmbedFooterBuilder().WithIconUrl(auth.IconUrl).WithText("Copyright (C) Frenetic LLC");
        message.Channel.SendMessageAsync(POSITIVE_PREFIX, embed: bed.Build()).Wait();
    }

    static void CMD_ListenInto(string[] cmds, SocketMessage message)
    {
        if (!IsBotCommander(message.Author))
        {
            message.Channel.SendMessageAsync(NEGATIVE_PREFIX + "Nope! That's not for you!").Wait();
            return;
        }
        if (!ServersConfig.TryGetValue((message.Channel as IGuildChannel).Id, out KnownServer ks))
        {
            ks = new KnownServer();
            ServersConfig[(message.Channel as IGuildChannel).Guild.Id] = ks;
        }
        if (cmds.Length == 0)
        {
            message.Channel.SendMessageAsync(NEGATIVE_PREFIX + "Nope! Consult documentation!").Wait();
            return;
        }
        String goal = cmds[0].ToLowerInvariant();
        IEnumerable<ITextChannel> channels = (message.Channel as IGuildChannel)
                .Guild.GetTextChannelsAsync().Result.Where((tc) => tc.Name.ToLowerInvariant().Replace("#", "").Equals(goal));
        if (channels.Count() == 0)
        {
            message.Channel.SendMessageAsync(POSITIVE_PREFIX + "Disabling sending.").Wait();
            IEnumerable<ITextChannel> channels2 = (message.Channel as IGuildChannel).Guild.GetTextChannelsAsync().Result;
            StringBuilder sbRes = new StringBuilder();
            foreach (ITextChannel itc in channels2)
            {
                sbRes.Append("`").Append(itc.Name).Append("`, ");
            }
            message.Channel.SendMessageAsync(POSITIVE_PREFIX + "Given: `" + goal + "`, Available: " + sbRes.ToString()).Wait();
            goal = null;
        }
        else
        {
            message.Channel.SendMessageAsync(POSITIVE_PREFIX + "Listening into: " + goal).Wait();
        }
        ks.AllChannelsTo = goal;
        SaveChannelConfig();
    }
    
    public static void SaveChannelConfig()
    {
        lock (reSaveLock)
        {
            Directory.CreateDirectory("./config/");
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<ulong, KnownServer> ksEnt in ServersConfig)
            {
                sb.Append(ksEnt.Key).Append("\n");
                if (ksEnt.Value.AllChannelsTo != null)
                {
                    sb.Append("\tall_channels_to:").Append(ksEnt.Value.AllChannelsTo).Append("\n");
                }
            }
            File.WriteAllText("./config/config.txt", sb.ToString());
        }
    }

    public static Object reSaveLock = new Object();

    static void DefaultCommands()
    {
        CommonCmds["quotes"] = CMD_ShowQuote;
        CommonCmds["quote"] = CMD_ShowQuote;
        CommonCmds["q"] = CMD_ShowQuote;
        CommonCmds["help"] = CMD_Help;
        CommonCmds["halp"] = CMD_Help;
        CommonCmds["helps"] = CMD_Help;
        CommonCmds["halps"] = CMD_Help;
        CommonCmds["hel"] = CMD_Help;
        CommonCmds["hal"] = CMD_Help;
        CommonCmds["h"] = CMD_Help;
        CommonCmds["hello"] = CMD_Hello;
        CommonCmds["hi"] = CMD_Hello;
        CommonCmds["hey"] = CMD_Hello;
        CommonCmds["source"] = CMD_Hello;
        CommonCmds["src"] = CMD_Hello;
        CommonCmds["github"] = CMD_Hello;
        CommonCmds["git"] = CMD_Hello;
        CommonCmds["hub"] = CMD_Hello;
        CommonCmds["who"] = CMD_WhatIsFrenetic;
        CommonCmds["what"] = CMD_WhatIsFrenetic;
        CommonCmds["where"] = CMD_WhatIsFrenetic;
        CommonCmds["why"] = CMD_WhatIsFrenetic;
        CommonCmds["frenetic"] = CMD_WhatIsFrenetic;
        CommonCmds["llc"] = CMD_WhatIsFrenetic;
        CommonCmds["freneticllc"] = CMD_WhatIsFrenetic;
        CommonCmds["website"] = CMD_WhatIsFrenetic;
        CommonCmds["team"] = CMD_WhatIsFrenetic;
        CommonCmds["company"] = CMD_WhatIsFrenetic;
        CommonCmds["business"] = CMD_WhatIsFrenetic;
        CommonCmds["restart"] = CMD_Restart;
        CommonCmds["selfinfo"] = CMD_SelfInfo;
        CommonCmds["whoami"] = CMD_SelfInfo;
        CommonCmds["whois"] = CMD_SelfInfo;
        CommonCmds["userinfo"] = CMD_SelfInfo;
        CommonCmds["userprofile"] = CMD_SelfInfo;
        CommonCmds["profile"] = CMD_SelfInfo;
        CommonCmds["prof"] = CMD_SelfInfo;
        CommonCmds["listeninto"] = CMD_ListenInto;
    }

    public static ConcurrentDictionary<ulong, KnownServer> ServersConfig = new ConcurrentDictionary<ulong, KnownServer>();

    public static Dictionary<string, string> PositiveChatResponses = new Dictionary<string, string> {
        {"yay", "YAY!!!"},
        {"woo!", "HOO!!"}
    };

    static void Main(string[] args)
    {
        Console.WriteLine("Preparing...");
        DefaultCommands();
        if (File.Exists("./config/config.txt"))
        {
            string fileDat = File.ReadAllText("./config/config.txt").Replace("\r", "");
            if (fileDat.Replace("\n", "").Length > 0)
            {
                string[] fdata = fileDat.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < fdata.Length; i++)
                {
                    string ln = fdata[i];
                    ulong ul = ulong.Parse(ln);
                    KnownServer ks = new KnownServer();
                    ServersConfig[ul] = ks;
                    int x;
                    for (x = i + 1; x < fdata.Length; x++)
                    {
                        if (!fdata[x].StartsWith(" ") && !fdata[x].StartsWith("\t"))
                        {
                            break;
                        }
                        string[] cln = fdata[x].Trim().Split(new char[] { ':' }, 2);
                        if (cln[0].ToLowerInvariant() == "all_channels_to")
                        {
                            ks.AllChannelsTo = cln[1];
                        }
                    }
                    i = x;
                }
            }
        }
        Console.WriteLine("Loading Discord...");
        DiscordSocketConfig config = new DiscordSocketConfig();
        config.MessageCacheSize = 1024 * 1024;
        client = new DiscordSocketClient(config);
        client.Ready += () =>
        {
            client.SetGameAsync("https://freneticllc.com").Wait();
            Console.WriteLine("Args: " + args.Length);
            if (args.Length > 0 && ulong.TryParse(args[0], out ulong a1))
            {
                ISocketMessageChannel chan = (client.GetChannel(a1) as ISocketMessageChannel);
                Console.WriteLine("Restarted as per request in channel: " + chan.Name);
                chan.SendMessageAsync(POSITIVE_PREFIX + "Connected and ready!").Wait();
            }
            return Task.CompletedTask;
        };
        client.MessageReceived += (message) =>
        {
            if (message.Author.Id == client.CurrentUser.Id)
            {
                return Task.CompletedTask;
            }
            if (message.Channel.Name.StartsWith("@") || !(message.Channel is SocketGuildChannel))
            {
                Console.WriteLine("Refused message from (" + message.Author.Username + "): (Invalid Channel: " + message.Channel.Name + "): " + message.Content);
                return Task.CompletedTask;
            }
            bool mentionedMe = false;
            foreach (SocketUser user in message.MentionedUsers)
            {
                if (user.Id == client.CurrentUser.Id)
                {
                    mentionedMe = true;
                    break;
                }
            }
            Console.WriteLine("Parsing message from (" + message.Author.Username + "), in channel: " + message.Channel.Name + ": " + message.Content);
            if (mentionedMe)
            {
                Respond(message);
            }
            else
            {
                String mesLow = message.Content.ToLowerInvariant();
                foreach (string key in PositiveChatResponses.Keys) {
                    if (mesLow.StartsWith(key))
                    {
                        message.Channel.SendMessageAsync(POSITIVE_PREFIX + PositiveChatResponses[key]).Wait();
                    }
                }
            }
            return Task.CompletedTask;
        };
        client.MessageDeleted += (m, c) =>
        {
            Console.WriteLine("A message was deleted!");
            IMessage mValue;
            if (!m.HasValue)
            {
                Console.WriteLine("But I don't see its data...");
                return Task.CompletedTask;
            }
            else
            {
                mValue = m.Value;
            }
            if (mValue.Author.Id == client.CurrentUser.Id)
            {
                Console.WriteLine("Wait, I did that!");
                return Task.CompletedTask;
            }
            if (!(c is IGuildChannel channel))
            {
                Console.WriteLine("But it was in a weird channel?");
                return Task.CompletedTask;
            }
            if (!ServersConfig.TryGetValue(channel.Guild.Id, out KnownServer ks))
            {
                Console.WriteLine("But it wasn't in a known guild.");
                return Task.CompletedTask;
            }
            if (ks.AllChannelsTo == null)
            {
                Console.WriteLine("But it wasn't in a listening zone.");
                return Task.CompletedTask;
            }
            IEnumerable<ITextChannel> channels = channel.Guild.GetTextChannelsAsync().Result.Where((tc) => tc.Name.ToLowerInvariant().Replace("#", "").Equals(ks.AllChannelsTo));
            if (channels.Count() == 0)
            {
                Console.WriteLine("Failed to match a channel: " + ks.AllChannelsTo);
                return Task.CompletedTask;
            }
            Console.WriteLine("Outputted!");
            ITextChannel outputter = channels.First();
            outputter.SendMessageAsync(POSITIVE_PREFIX + "Message deleted (`"  + mValue.Channel.Name + "`)... message from: `"
                    + mValue.Author.Username + "#" + mValue.Author.Discriminator 
                    + "`: ```\n" + mValue.Content.Replace('`', '\'') + "\n```").Wait();
            return Task.CompletedTask;
        };
        client.MessageUpdated += (m, mNew, c) =>
        {
            Console.WriteLine("A message was edited!");
            IMessage mValue;
            if (!m.HasValue)
            {
                Console.WriteLine("But I don't see its data...");
                return Task.CompletedTask;
            }
            else
            {
                mValue = m.Value;
            }
            if (mValue.Author.Id == client.CurrentUser.Id)
            {
                return Task.CompletedTask;
            }
            if (!(c is IGuildChannel channel))
            {
                return Task.CompletedTask;
            }
            if (!ServersConfig.TryGetValue(channel.Guild.Id, out KnownServer ks))
            {
                return Task.CompletedTask;
            }
            if (ks.AllChannelsTo == null)
            {
                return Task.CompletedTask;
            }
            IEnumerable<ITextChannel> channels = channel.Guild.GetTextChannelsAsync().Result.Where((tc) => tc.Name.ToLowerInvariant().Replace("#", "").Equals(ks.AllChannelsTo));
            if (channels.Count() == 0)
            {
                Console.WriteLine("Failed to match a channel: " + ks.AllChannelsTo);
                return Task.CompletedTask;
            }
            if (mNew.Content == mValue.Content)
            {
                return Task.CompletedTask;
            }
            ITextChannel outputter = channels.First();
            outputter.SendMessageAsync(POSITIVE_PREFIX + "Message edited(`"  + mValue.Channel.Name + "`)... message from: `"
                    + mValue.Author.Username + "#" + mValue.Author.Discriminator 
                    + "`: ```\n" + mValue.Content.Replace('`', '\'') + "\n```\nBecame:\n```"
                    + mNew.Content.Replace('`', '\'') + "\n```");
            return Task.CompletedTask;
        };
        Console.WriteLine("Logging in to Discord...");
        client.LoginAsync(TokenType.Bot, TOKEN).Wait();
        Console.WriteLine("Connecting to Discord...");
        client.StartAsync().Wait();
        Console.WriteLine("Running Discord!");
        while (true)
        {
            string read = Console.ReadLine();
            string[] dats = read.Split(new char[] { ' ' }, 2);
            string cmd = dats[0].ToLowerInvariant();
            if (cmd == "quit" || cmd == "stop" || cmd == "exit")
            {
                client.StopAsync().Wait();
                Environment.Exit(0);
            }
        }
    }

    public class KnownServer
    {
        public string AllChannelsTo = null;
    }
}
