using System.Globalization;
using Ardalis.GuardClauses;
using Humanizer;
using LeaderBoard.Dtos;
using LeaderBoard.Infrastructure.Clients;
using LeaderBoard.Infrastructure.Data.EFContext;
using LeaderBoard.MessageContracts.PlayerScore;
using LeaderBoard.Models;
using LeaderBoard.SharedKernel.Redis;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace LeaderBoard.Services;

public class PlayerScoreService : IPlayerScoreService
{
    private readonly LeaderBoardDBContext _leaderBoardDbContext;
    private readonly IReadThroughClient _readThroughClient;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly LeaderBoardOptions _leaderboardOptions;
    private readonly IDatabase _redisDatabase;

    public PlayerScoreService(
        IConnectionMultiplexer redisConnection,
        LeaderBoardDBContext leaderBoardDbContext,
        IReadThroughClient readThroughClient,
        IPublishEndpoint publishEndpoint,
        IOptions<LeaderBoardOptions> leaderboardOptions
    )
    {
        _leaderBoardDbContext = leaderBoardDbContext;
        _readThroughClient = readThroughClient;
        _publishEndpoint = publishEndpoint;
        _leaderboardOptions = leaderboardOptions.Value;
        _redisDatabase = redisConnection.GetDatabase();
    }

    public async Task<bool> AddPlayerScore(
        PlayerScore playerScore,
        CancellationToken cancellationToken = default
    )
    {
        Guard.Against.Null(playerScore);

        var key = $"{nameof(PlayerScore).Underscore()}:{playerScore.PlayerId}";
        bool isDesc = true;

        // store the summary of our player-score in sortedset
        var res = await _redisDatabase.SortedSetAddAsync(
            playerScore.LeaderBoardName,
            key,
            playerScore.Score
        );
        if (res == false)
            return false;

        var rank = await _redisDatabase.SortedSetRankAsync(playerScore.LeaderBoardName, key);
        rank = isDesc ? rank + 1 : rank - 1;

        // recalculated rank by redis sortedset after adding new item
        playerScore.Rank = rank;

        // store detail of out score-player in hashset. it is related to its score information with their same unique identifier
        await _redisDatabase.HashSetAsync(
            playerScore.PlayerId,
            new HashEntry[]
            {
                new(nameof(PlayerScore.Country).Underscore(), playerScore.Country),
                new(nameof(PlayerScore.FirstName).Underscore(), playerScore.FirstName),
                new(nameof(PlayerScore.LastName).Underscore(), playerScore.LastName),
                new(
                    nameof(PlayerScore.UpdatedAt).Underscore(),
                    playerScore.UpdatedAt.ToString(CultureInfo.InvariantCulture)
                ),
                new(
                    nameof(PlayerScore.CreatedAt).Underscore(),
                    playerScore.CreatedAt.ToString(CultureInfo.InvariantCulture)
                )
            }
        );

        if (_leaderboardOptions.UseWriteCacheAside)
        {
            // we have to write to cache first for calculating redis rank correctly for our update
            await _leaderBoardDbContext.PlayerScores.AddAsync(playerScore, cancellationToken);
            await _leaderBoardDbContext.SaveChangesAsync(cancellationToken);
        }
        else if (_leaderboardOptions.UseWriteBehind)
        {
            HashEntry[] hashEntries =
            {
                new(nameof(PlayerScore.PlayerId).Underscore(), playerScore.PlayerId),
                new(nameof(PlayerScore.LeaderBoardName).Underscore(), playerScore.LeaderBoardName),
                new(nameof(PlayerScore.Score).Underscore(), playerScore.Score),
                new(nameof(PlayerScore.Rank).Underscore(), rank),
                new(nameof(PlayerScore.FirstName).Underscore(), playerScore?.FirstName),
                new(nameof(PlayerScore.LastName).Underscore(), playerScore?.LastName),
                new(nameof(PlayerScore.Country).Underscore(), playerScore?.Country),
            };

            var playerScoreAdded = new PlayerScoreAdded(
                playerScore!.PlayerId,
                playerScore.Score,
                playerScore.LeaderBoardName,
                playerScore.Rank,
                playerScore.Country,
                playerScore.FirstName,
                playerScore.LastName
            );

            // write behind strategy - will handle by caching provider(like redis gears) internally or out external library or azure function or background services
            // redis pub/sub
            // uses a CommandChannelValueMessage message internally on top of redis stream
            await _redisDatabase.PublishMessage(playerScoreAdded);

            // publish a stream to redis (both PublishMessage and StreamAddAsync use same streaming mechanism behind the scenes)
            // uses a CommandKeyValuesMessage message internally on top of redis stream
            await _redisDatabase.StreamAddAsync(
                GetStreamName<PlayerScoreAdded>(playerScore.PlayerId), //_player_score_added-stream-{key}
                hashEntries.Select(x => new NameValueEntry(x.Name, x.Value)).ToArray()
            );

            // Or publish message to broker
            await _publishEndpoint.Publish(playerScoreAdded, cancellationToken);
        }

        return true;
    }

    public async Task<bool> UpdateScore(
        string leaderBoardName,
        string playerId,
        double value,
        CancellationToken cancellationToken = default
    )
    {
        string key = $"{nameof(PlayerScore)}:{playerId}";
        bool isDesc = true;

        var res = await _redisDatabase.SortedSetAddAsync(leaderBoardName, key, value);
        if (res == false)
            return false;

        var score = await _redisDatabase.SortedSetScoreAsync(leaderBoardName, key);
        var rank = await _redisDatabase.SortedSetRankAsync(leaderBoardName, key);
        rank = isDesc ? rank + 1 : rank - 1;

        if (_leaderboardOptions.UseWriteCacheAside)
        {
            // we have to write to cache first for calculating redis rank correctly for our update
            var existPlayerScore = _leaderBoardDbContext.PlayerScores.SingleOrDefault(
                x => x.PlayerId == playerId && x.LeaderBoardName == leaderBoardName
            );
            if (existPlayerScore is { })
            {
                existPlayerScore.Rank = rank;
                existPlayerScore.Score = score ?? 0;
                await _leaderBoardDbContext.SaveChangesAsync(cancellationToken);
            }
        }
        else if (_leaderboardOptions.UseWriteBehind)
        {
            HashEntry[] hashEntries =
            {
                new(nameof(PlayerScore.PlayerId).Underscore(), playerId),
                new(nameof(PlayerScore.LeaderBoardName).Underscore(), leaderBoardName),
                new(nameof(PlayerScore.Score).Underscore(), score ?? 0),
                new(nameof(PlayerScore.Rank).Underscore(), rank),
            };
            var playerScoreUpdated = new PlayerScoreUpdated(
                playerId,
                score ?? 0,
                leaderBoardName,
                rank
            );
            // write behind strategy - will handle by caching provider(like redis gears) internally or out external library or azure function or background services
            // redis pub/sub message
            // uses a CommandChannelValueMessage message internally on top of redis stream
            await _redisDatabase.PublishMessage(playerScoreUpdated);

            // publish a stream to redis (both PublishMessage and StreamAddAsync use same streaming mechanism behind the scenes)
            // uses a CommandKeyValuesMessage message internally on top of redis stream
            await _redisDatabase.StreamAddAsync(
                GetStreamName<PlayerScoreUpdated>(playerId), //_player_score_updated-stream-{key}
                hashEntries.Select(x => new NameValueEntry(x.Name, x.Value)).ToArray()
            );

            // Or publish message to broker
            await _publishEndpoint.Publish(playerScoreUpdated, cancellationToken);
        }

        return true;
    }

    public virtual async Task<List<PlayerScoreDto>?> GetRangeScoresAndRanks(
        string leaderBoardName,
        int start,
        int end,
        bool isDesc = true,
        CancellationToken cancellationToken = default
    )
    {
        if (_leaderboardOptions.UseReadThrough)
        {
            return await _readThroughClient.GetRangeScoresAndRanks(
                leaderBoardName,
                start,
                end,
                isDesc,
                cancellationToken
            );
        }

        var counter = isDesc ? 1 : -1;
        var playerScores = new List<PlayerScoreDto>();
        var results = await _redisDatabase.SortedSetRangeByRankWithScoresAsync(
            leaderBoardName,
            start,
            end,
            isDesc ? Order.Descending : Order.Ascending
        );

        if ((results == null || results.Length == 0) && _leaderboardOptions.UseReadCacheAside)
        {
            IQueryable<PlayerScore> postgresItems = isDesc
                ? _leaderBoardDbContext.PlayerScores.AsNoTracking().OrderByDescending(x => x.Score)
                : _leaderBoardDbContext.PlayerScores.AsNoTracking().OrderBy(x => x.Score);

            // for working ranks correctly we should load all items form primary database to cache for using sortedset for calculating ranks
            postgresItems = postgresItems.Where(x => x.LeaderBoardName == leaderBoardName);

            await PopulateCache(postgresItems.AsAsyncEnumerable());

            var data = await postgresItems
                .Skip(start)
                .Take(end + 1)
                .ToListAsync(cancellationToken: cancellationToken);

            if (data.Count == 0)
            {
                return null;
            }

            var startRank = isDesc ? start + 1 : data.Count;
            return data.Select(
                    (x, i) =>
                        new PlayerScoreDto(
                            x.PlayerId,
                            x.Score,
                            leaderBoardName,
                            Rank: i == 0 ? startRank : startRank += counter,
                            x.Country,
                            x.FirstName,
                            x.LeaderBoardName
                        )
                )
                .ToList();
        }

        if (results is null)
            return null;

        var sortedsetItems = results.ToList();
        var startRank2 = isDesc ? start + 1 : results.Length;

        foreach (var sortedsetItem in sortedsetItems)
        {
            string key = sortedsetItem.Element;
            var playerId = key.Split(":")[1];

            // get detail information about saved sortedset score-player
            var detail = await GetPlayerScoreDetail(
                leaderBoardName,
                key,
                isDesc,
                cancellationToken
            );

            var playerScore = new PlayerScoreDto(
                playerId,
                sortedsetItem.Score,
                leaderBoardName,
                startRank2,
                detail?.Country,
                detail?.FirstName,
                detail?.LastName
            );
            playerScores.Add(playerScore);

            // next rank for next item
            startRank2 += counter;
        }

        return playerScores;
    }

    public async Task<List<PlayerScoreDto>?> GetPlayerGroupScoresAndRanks(
        string leaderBoardName,
        IEnumerable<string> playerIds,
        bool isDesc = true,
        CancellationToken cancellationToken = default
    )
    {
        if (_leaderboardOptions.UseReadThrough)
        {
            return await _readThroughClient.GetPlayerGroupScoresAndRanks(
                leaderBoardName,
                playerIds,
                isDesc,
                cancellationToken
            );
        }

        var results = new List<PlayerScoreDto>();
        foreach (var playerId in playerIds)
        {
            var playerScore = await GetGlobalScoreAndRank(
                leaderBoardName,
                playerId,
                isDesc,
                cancellationToken
            );
            if (playerScore != null)
            {
                results.Add(playerScore);
            }
        }

        List<PlayerScoreDto> items = isDesc
            ? results.OrderByDescending(x => x.Score).ToList()
            : results.OrderBy(x => x.Score).ToList();
        var counter = isDesc ? 1 : -1;
        var startRank = isDesc ? 1 : items.Count;

        return items
            .Select((x, i) => x with { Rank = i == 0 ? startRank : counter + startRank })
            .ToList();
    }

    public virtual async Task<PlayerScoreDto?> GetGlobalScoreAndRank(
        string leaderBoardName,
        string playerId,
        bool isDesc = true,
        CancellationToken cancellationToken = default
    )
    {
        if (_leaderboardOptions.UseReadThrough)
        {
            return await _readThroughClient.GetGlobalScoreAndRank(
                leaderBoardName,
                playerId,
                isDesc,
                cancellationToken
            );
        }

        string key = $"{nameof(PlayerScore)}:{playerId}";

        var score = await _redisDatabase.SortedSetScoreAsync(leaderBoardName, playerId);
        var rank = await _redisDatabase.SortedSetRankAsync(
            leaderBoardName,
            playerId,
            isDesc ? Order.Descending : Order.Ascending
        );

        if ((score == null || rank == null) && _leaderboardOptions.UseReadCacheAside)
        {
            var playerScore = await _leaderBoardDbContext.PlayerScores.SingleOrDefaultAsync(
                x => x.PlayerId == playerId && x.LeaderBoardName == leaderBoardName,
                cancellationToken: cancellationToken
            );
            if (playerScore != null)
            {
                await PopulateCache(playerScore);

                return new PlayerScoreDto(
                    playerId,
                    playerScore.Score,
                    leaderBoardName,
                    playerScore.Rank ?? 1,
                    playerScore.Country,
                    playerScore.FirstName,
                    playerScore.LastName
                );
            }

            return null;
        }

        rank = isDesc ? rank + 1 : rank - 1;

        PlayerScoreDetailDto? detail = await GetPlayerScoreDetail(
            leaderBoardName,
            key,
            isDesc,
            cancellationToken
        );
        return new PlayerScoreDto(
            playerId,
            score ?? 0,
            leaderBoardName,
            rank ?? 1,
            detail?.Country,
            detail?.FirstName,
            detail?.LastName
        );
    }

    /// <summary>
    ///  Get details information about saved sortedset player score, because sortedset is limited for saving all informations
    /// </summary>
    private async Task<PlayerScoreDetailDto?> GetPlayerScoreDetail(
        string leaderboardName,
        string playerIdKey,
        bool isDesc = true,
        CancellationToken cancellationToken = default
    )
    {
        if (_leaderboardOptions.UseReadThrough)
        {
            var res = await _readThroughClient.GetGlobalScoreAndRank(
                leaderboardName,
                playerIdKey,
                isDesc,
                cancellationToken
            );
            return res is null
                ? null
                : new PlayerScoreDetailDto(res.Country, res.FirstName, res.LastName);
        }

        HashEntry[] item = await _redisDatabase.HashGetAllAsync(playerIdKey);
        if (item.Length == 0 && _leaderboardOptions.UseReadCacheAside)
        {
            string playerId = playerIdKey.Split(":")[1];

            var playerScore = await _leaderBoardDbContext.PlayerScores.SingleOrDefaultAsync(
                x => x.PlayerId == playerId && x.LeaderBoardName == leaderboardName,
                cancellationToken: cancellationToken
            );
            if (playerScore != null)
            {
                await PopulateCache(playerScore);
                return new PlayerScoreDetailDto(
                    playerScore.Country,
                    playerScore.FirstName,
                    playerScore.LastName
                );
            }

            return null;
        }

        var firstName = item.SingleOrDefault(
            x => x.Name == nameof(PlayerScore.FirstName).Underscore()
        );
        var lastName = item.SingleOrDefault(
            x => x.Name == nameof(PlayerScore.LastName).Underscore()
        );
        var country = item.SingleOrDefault(x => x.Name == nameof(PlayerScore.Country).Underscore());

        return new PlayerScoreDetailDto(country.Value, firstName.Value, lastName.Value);
    }

    private string GetStreamName(string messageType, string key)
    {
        return $"_{messageType}-stream-{key}";
    }

    private string GetStreamName<T>(string key)
    {
        return $"_{typeof(T).Name.Underscore()}-stream-{key}";
    }

    private async Task PopulateCache(IAsyncEnumerable<PlayerScore> playerScores)
    {
        await foreach (var playerScore in playerScores)
        {
            await PopulateCache(playerScore);
        }
    }

    private async Task PopulateCache(PlayerScore playerScore)
    {
        var key = $"{nameof(PlayerScore).Underscore()}:{playerScore.PlayerId}";

        // store the summary of our player-score in sortedset, it cannot store all information
        await _redisDatabase.SortedSetAddAsync(playerScore.LeaderBoardName, key, playerScore.Score);

        // store detail of out score-player in hashset. it is related to its score information with their same unique identifier
        await _redisDatabase.HashSetAsync(
            key,
            new HashEntry[]
            {
                new(nameof(PlayerScore.Country).Underscore(), playerScore.Country),
                new(nameof(PlayerScore.FirstName).Underscore(), playerScore.FirstName),
                new(nameof(PlayerScore.LastName).Underscore(), playerScore.LastName),
                new(
                    nameof(PlayerScore.UpdatedAt).Underscore(),
                    playerScore.UpdatedAt.ToString(CultureInfo.InvariantCulture)
                ),
                new(
                    nameof(PlayerScore.CreatedAt).Underscore(),
                    playerScore.CreatedAt.ToString(CultureInfo.InvariantCulture)
                )
            }
        );
    }
}
