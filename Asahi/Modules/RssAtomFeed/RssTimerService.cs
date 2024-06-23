﻿using Asahi.Database;
using Asahi.Database.Models.Rss;
using Asahi.Modules.RssAtomFeed.Models;
using CodeHollow.FeedReader;
using CodeHollow.FeedReader.Feeds;
using Discord.WebSocket;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Asahi.Modules.RssAtomFeed;

[Inject(ServiceLifetime.Singleton)]
public class RssTimerService(IHttpClientFactory clientFactory, DbService dbService, DiscordSocketClient client, ILogger<RssTimerService> logger)
{
    public enum FeedHandler
    {
        RssAtom,
        Danbooru
    }

    public Task? timerTask;

    private readonly Dictionary<int, HashSet<int>> hashedSeenArticles = [];

    public void StartBackgroundTask(CancellationToken token)
    {
        timerTask ??= Task.Run(() => TimerTask(token), token);
    }

    /// <remarks>Should only be one of these running!</remarks>
    private async Task TimerTask(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogTrace("RSS timer task started");
            try
            {
                await PollFeeds();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception in TimerTask! {message}", ex.Message);
            }
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await PollFeeds();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unhandled exception in TimerTask! {message}", ex.Message);
                }
            }
        }
        catch (TaskCanceledException) { }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            logger.LogCritical(e, "Unhandled exception in TimerTask! Except much worse because this was outside of the loop!!");
        }
    }

    public async Task PollFeeds()
    {
        await using var context = dbService.GetDbContext();

        var feeds = await context.RssFeedListeners.ToArrayAsync();

        var feedsGrouped = feeds.GroupBy(x => x.FeedUrl);
        using var http = clientFactory.CreateClient();
        http.MaxResponseContentBufferSize = 8000000;

        var channelsToPurgeFeedsOf = new List<ulong>();

        foreach (var feedGroup in feedsGrouped)
        {
            try
            {
                var url = feedGroup.Key;
                // doing hashes for memory reasons
                var urlHash = url.GetHashCode(StringComparison.OrdinalIgnoreCase);

                var feedHandler = FeedHandlerForUrl(url);

                var unseenUrl = false;
                //logger.LogTrace("processing {url}", url);
                if (!hashedSeenArticles.TryGetValue(urlHash, out var seenArticles))
                {
                    //logger.LogTrace("never seen the url {url} before", url);
                    unseenUrl = true;
                    seenArticles = [];
                    hashedSeenArticles.Add(urlHash, seenArticles);
                }

                using var req = await http.GetAsync(url);
                var reqContent = await req.Content.ReadAsStringAsync();

                var processedArticles = new HashSet<int>();

                IEmbedGenerator embedGenerator;
                try
                {
                    switch (feedHandler)
                    {
                        case FeedHandler.RssAtom:
                            {
                                var feed = FeedReader.ReadFromString(reqContent);

                                if (!ValidateFeed(feed))
                                    continue;

                                IEnumerable<FeedItem> feedsEnumerable = feed.Items;
                                if (feed.Items.All(x => x.PublishingDate.HasValue))
                                {
                                    feedsEnumerable = feedsEnumerable.OrderByDescending(x => x.PublishingDate);
                                }

                                var feedsArray = feedsEnumerable.ToArray();

                                embedGenerator = new RssFeedEmbedGenerator(feed, feedsArray);
                                break;
                            }
                        case FeedHandler.Danbooru:
                            {
                                var posts = JsonConvert.DeserializeObject<DanbooruPost[]>(reqContent);

                                if (posts == null)
                                    continue;

                                embedGenerator = new DanbooruEmbedGenerator(posts);
                                break;
                            }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to process feed {feedUrl}.", url);
                    continue;
                }

                //logger.LogTrace("seen articles is {seen}", string.Join(',', seenArticles.Select(x => x.ToString())));

                foreach (var feedListener in feedGroup)
                {
                    try
                    {
                        var guild = client.GetGuild(feedListener.GuildId);

                        if (guild.GetChannel(feedListener.ChannelId) is not ISocketMessageChannel channel)
                        {
                            channelsToPurgeFeedsOf.Add(feedListener.ChannelId);
                            continue;
                        }

                        var embeds = embedGenerator.GenerateFeedItemEmbeds(feedListener, seenArticles, processedArticles,
                            QuotingHelpers.GetUserRoleColorWithFallback(guild.CurrentUser, Color.Default), !unseenUrl).Take(10).ToArray();

                        if (embeds.Length != 0)
                            await channel.SendMessageAsync(embeds: embeds);

                        //foreach (var feedItem in feedsArray)
                        //{
                        //    if (unseenUrl || embeds.Count >= 10 ||
                        //        seenArticles.Contains(feedItem.Id.GetHashCode(StringComparison.Ordinal))) continue;

                        //    embeds.Add(GenerateFeedItemEmbed(feedItem, feed, feedListener, QuotingHelpers.GetUserRoleColorWithFallback(guild.CurrentUser, Color.Default)));
                        //}
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to send feed {url} to guild {guildId}, channel {channelId}",
                            feedGroup.Key, feedListener.GuildId, feedListener.ChannelId);
                    }
                }

                seenArticles.Clear();
                foreach (var article in processedArticles)
                {
                    seenArticles.Add(article);
                }

                //logger.LogTrace("seen articles is now {seen}",
                //    string.Join(',', seenArticles.Select(x => x.ToString())));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch feed {url}", feedGroup.Key);
            }
        }


        foreach (var channelId in channelsToPurgeFeedsOf)
        {
            await context.RssFeedListeners.Where(x => x.ChannelId == channelId).ExecuteDeleteAsync();
        }
    }

    public static FeedHandler FeedHandlerForUrl(string url)
    {
        return url.StartsWith("https://danbooru.donmai.us/posts.json") ? FeedHandler.Danbooru : FeedHandler.RssAtom;
    }

    public static bool ValidateFeed(Feed? feed)
    {
        return feed?.Type != FeedType.Unknown;
    }
}

public class DanbooruEmbedGenerator(DanbooruPost[] posts) : IEmbedGenerator
{
    private static readonly HashSet<string> KnownImageExtensions = ["jpg", "jpeg", "png", "gif", "bmp", "webp"];

    public IEnumerable<Embed> GenerateFeedItemEmbeds(FeedListener feedListener, HashSet<int> seenArticles, HashSet<int> processedArticles,
        Color embedColor, bool shouldCreateEmbeds)
    {
        foreach (var post in posts)
        {
            processedArticles.Add(post.Id);

            if (seenArticles.Contains(post.Id)) continue;
            if (!shouldCreateEmbeds) continue;

            yield return GenerateFeedItemEmbed(feedListener, post, embedColor);
        }
    }

    private Embed GenerateFeedItemEmbed(FeedListener feedListener, DanbooruPost post, Color embedColor)
    {
        var eb = new EmbedBuilder();

        eb.WithColor(embedColor);
        if (!string.IsNullOrWhiteSpace(post.TagStringArtist))
        {
            eb.WithAuthor(post.TagStringArtist.Split(' ').Humanize());
        }
        if (!string.IsNullOrWhiteSpace(feedListener.FeedTitle))
        {
            eb.WithFooter(feedListener.FeedTitle, "https://danbooru.donmai.us/packs/static/danbooru-logo-128x128-ea111b6658173e847734.png");
        }
        eb.WithTimestamp(post.CreatedAt);

        if (!string.IsNullOrWhiteSpace(post.TagStringCharacter))
        {
            eb.WithTitle(post.TagStringCharacter.Split(' ').Select(x => x.Titleize()).Humanize());
        }

        eb.WithUrl($"https://danbooru.donmai.us/posts/{post.Id}/");

        var bestVariant = GetBestVariant(post.MediaAsset.Variants);
        if (bestVariant != null)
        {
            eb.WithImageUrl(bestVariant.Url);
        }

        eb.WithDescription($"{post.MediaAsset.FileExtension.ToUpperInvariant()} file | " +
                           $"embed is {bestVariant?.Type} quality{(bestVariant?.Type != "original" ? $" ({bestVariant?.FileExt.ToUpperInvariant()} file)" : "")}");

        return eb.Build();
    }

    private DanbooruVariant? GetBestVariant(DanbooruVariant[] variants)
    {
        // we only want embeddable variants
        var validVariants = variants.Where(v => KnownImageExtensions.Contains(v.FileExt.ToLower())).ToList();

        //// sample is usually best compromise
        //var sampleVariant = validVariants.FirstOrDefault(v => v.Type == "sample");

        //if (sampleVariant != null)
        //{
        //    return sampleVariant;
        //}

        // sample doesn't exist oh god lets just hope the rest of the options are ok
        return validVariants.MaxBy(v => v.Width * v.Height);
    }
}

public class RssFeedEmbedGenerator(Feed genericFeed, FeedItem[] feedItems) : IEmbedGenerator
{
    public IEnumerable<Embed> GenerateFeedItemEmbeds(FeedListener feedListener, HashSet<int> seenArticles, HashSet<int> processedArticles, Color embedColor, bool shouldCreateEmbeds)
    {
        foreach (var feedItem in feedItems)
        {
            processedArticles.Add(feedItem.Id.GetHashCode(StringComparison.Ordinal));

            if (seenArticles.Contains(feedItem.Id.GetHashCode(StringComparison.Ordinal))) continue;
            if (!shouldCreateEmbeds) continue;

            yield return GenerateFeedItemEmbed(feedListener, feedItem, embedColor);
        }
    }

    public Embed GenerateFeedItemEmbed(FeedListener feedListener, FeedItem genericItem, Color embedColor)
    {
        var eb = new EmbedBuilder();

        switch (genericFeed.Type)
        {
            case FeedType.Atom:
                {
                    var feed = (AtomFeed)genericFeed.SpecificFeed;
                    var item = (AtomFeedItem)genericItem.SpecificItem;

                    var footer = new EmbedFooterBuilder();

                    if (item.Author != null)
                    {
                        eb.WithAuthor(item.Author.ToString(), url: !string.IsNullOrEmpty(item.Author.Uri) ? item.Author.Uri : null);
                    }

                    if (!string.IsNullOrWhiteSpace(item.Summary))
                    {
                        eb.WithDescription(item.Summary);
                    }

                    if (!string.IsNullOrWhiteSpace(item.Title))
                    {
                        eb.WithTitle(item.Title);
                    }

                    if (!string.IsNullOrWhiteSpace(item.Link))
                    {
                        eb.WithUrl(item.Link);
                    }

                    if (item.PublishedDate != null)
                    {
                        eb.WithTimestamp(item.PublishedDate.Value);
                    }
                    else if (item.UpdatedDate != null)
                    {
                        eb.WithTimestamp(item.UpdatedDate.Value);
                    }

                    // general feed stuff
                    if (!string.IsNullOrWhiteSpace(feed.Icon))
                    {
                        footer.IconUrl = feed.Icon;

                        // stupid ass bug
                        if (footer.IconUrl == "https://www.redditstatic.com/icon.png/")
                        {
                            footer.IconUrl = "https://www.redditstatic.com/icon.png";
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(feedListener.FeedTitle))
                    {
                        footer.Text = $"{feedListener.FeedTitle} • {item.Id}";
                    }
                    else if (!string.IsNullOrWhiteSpace(feed.Title))
                    {
                        footer.Text = $"{feed.Title} • {item.Id}";
                    }

                    eb.WithFooter(footer);

                    break;
                }
            case FeedType.Rss_1_0:
            case FeedType.Rss_2_0:
            case FeedType.MediaRss:
            case FeedType.Rss:
            case FeedType.Rss_0_91:
            case FeedType.Rss_0_92:
                {
                    var footer = new EmbedFooterBuilder();

                    if (!string.IsNullOrWhiteSpace(genericItem.Author))
                    {
                        eb.WithAuthor(genericItem.Author);
                    }

                    if (!string.IsNullOrWhiteSpace(genericItem.Description))
                    {
                        eb.WithDescription(genericItem.Description);
                    }

                    if (!string.IsNullOrWhiteSpace(genericItem.Title))
                    {
                        eb.WithTitle(genericItem.Title);
                    }

                    if (!string.IsNullOrWhiteSpace(genericItem.Link))
                    {
                        eb.WithUrl(genericItem.Link);
                    }

                    if (genericItem.PublishingDate.HasValue)
                    {
                        eb.WithTimestamp(genericItem.PublishingDate.Value);
                    }

                    // general feed stuff
                    if (!string.IsNullOrWhiteSpace(genericFeed.ImageUrl))
                    {
                        eb.WithThumbnailUrl(genericFeed.ImageUrl);
                    }

                    if (!string.IsNullOrWhiteSpace(feedListener.FeedTitle))
                    {
                        footer.Text = $"{feedListener.FeedTitle} • {genericItem.Id}";
                    }
                    else if (!string.IsNullOrWhiteSpace(genericFeed.Title))
                    {
                        footer.Text = $"{genericFeed.Title} • {genericItem.Id}";
                    }

                    eb.WithFooter(footer);

                    break;
                }
            case FeedType.Unknown:
            default:
                throw new NotSupportedException();
        }

        var thumbnail = genericItem.SpecificItem.Element.Descendants().FirstOrDefault(x =>
                x.Name.LocalName == "content" && x.Attribute("type")?.Value == "xhtml")?
            .Descendants().FirstOrDefault(x => x.Name.LocalName == "img")?
            .Attributes().FirstOrDefault(x => x.Name == "src")?.Value;

        thumbnail ??=
            genericItem.SpecificItem.Element.Descendants().FirstOrDefault(x => x.Name.LocalName.Contains("thumbnail", StringComparison.InvariantCultureIgnoreCase))?
                .Attribute("url")?.Value;

        if (!string.IsNullOrWhiteSpace(thumbnail))
        {
            //const string garbageQualityUrl = "https://cdn.donmai.us/360x360/";
            //const string goodQualityUrl = "https://cdn.donmai.us/original/";

            //if (thumbnail.StartsWith(garbageQualityUrl))
            //{
            //    logger.LogTrace(thumbnail);
            //    // CA said it was better to do this? guess spans are better for this stuff?
            //    thumbnail = string.Concat(goodQualityUrl, thumbnail.AsSpan(garbageQualityUrl.Length));
            //}

            eb.WithImageUrl(thumbnail);
        }

        eb.WithColor(embedColor);

        if (!string.IsNullOrWhiteSpace(eb.Title))
            eb.Title = StringExtensions.Truncate(eb.Title, 200);

        if (!string.IsNullOrWhiteSpace(eb.Description))
            eb.Description = StringExtensions.Truncate(eb.Description, 400);

        return eb.Build();
    }
}

public interface IEmbedGenerator
{
    /// <summary>
    /// Returns an IEnumerable of all the embeds for that feed's items.
    /// </summary>
    /// <param name="feedListener">The listener.</param>
    /// <param name="seenArticles">The previously seen articles from the last run. Will not be edited.</param>
    /// <param name="processedArticles">The current work in progress articles that have been processed. Will be edited.</param>
    /// <param name="embedColor">The color to use for the embed.</param>
    /// <returns></returns>
    public IEnumerable<Embed> GenerateFeedItemEmbeds(FeedListener feedListener, HashSet<int> seenArticles, HashSet<int> processedArticles, Color embedColor, bool shouldCreateEmbeds);
}