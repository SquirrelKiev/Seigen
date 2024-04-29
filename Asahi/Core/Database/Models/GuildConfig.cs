﻿using System.ComponentModel.DataAnnotations;

namespace Asahi.Database.Models;

public class GuildConfig
{
    public const int MaxPrefixLength = 8;
    public const string DefaultPrefix = "]";

    [Key]
    public required ulong GuildId { get; set; }

    [MaxLength(MaxPrefixLength)]
    public string Prefix { get; set; } = DefaultPrefix;

    public bool SpoilerBotAutoDeleteOriginal { get; set; } = true;

    public bool SpoilerBotAutoDeleteContextSetting { get; set; } = true;

    [MaxLength(100)]
    public string SpoilerReactionEmote { get; set; } = "";
}
