﻿using Microsoft.EntityFrameworkCore;
using Zatsuyou.Database.Models;

namespace Zatsuyou.Database;

public static class DbExtensions
{
    /// <param name="modifyFunc">Should only be used with Include(), if this filters anything then bad stuff will happen.</param>
    public static async Task<GuildConfig> GetGuildConfig(this BotDbContext context,
        ulong guildId,
        Func<IQueryable<GuildConfig>, IQueryable<GuildConfig>>? modifyFunc = null)
    {
        IQueryable<GuildConfig> configs = context.GuildConfigs;
        if (modifyFunc != null)
            configs = modifyFunc(context.GuildConfigs);

        var guildConfig = await configs.FirstOrDefaultAsync(x => x.GuildId == guildId);

        if (guildConfig != null) return guildConfig;

        guildConfig = new GuildConfig()
        {
            GuildId = guildId
        };

        context.Add(guildConfig);

        return guildConfig;
    }

    public static async Task<CustomCommand?> GetCustomCommand(this BotDbContext context, ulong guildId, string name)
    {
        var command = await context.CustomCommands.FirstOrDefaultAsync(x => x.GuildId == guildId && x.Name == name);

        return command;
    }
}