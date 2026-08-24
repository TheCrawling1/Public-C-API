using ApiRouter.Data;
using ApiRouter.Models;
using ApiRouter.Rules;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApiRouter.Tests;

public class RuleEngineTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RouterDbContext _db;
    private readonly RuleEngine _engine;

    public RuleEngineTests()
    {
        // A private, in-memory SQLite database kept alive by the open connection.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<RouterDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new RouterDbContext(options);
        _db.Database.EnsureCreated();
        _engine = new RuleEngine(_db);
    }

    private User SeedUser()
    {
        var user = new User { Name = "u", ApiKeyHash = "k" };
        _db.Users.Add(user);
        _db.SaveChanges();
        return user;
    }

    private static Target HttpTarget() =>
        new() { Key = "httpbin", Name = "httpbin", Kind = TargetKind.Http, BaseUrl = "https://httpbin.org" };

    [Fact]
    public async Task Denies_When_No_Rule_Matches()
    {
        var user = SeedUser();

        var decision = await _engine.EvaluateAsync(user, HttpTarget(), "GET", default);

        Assert.False(decision.Allowed);
        Assert.Contains("default deny", decision.Reason);
    }

    [Fact]
    public async Task Allows_When_A_Matching_Allow_Rule_Exists()
    {
        var user = SeedUser();
        _db.Rules.Add(new Rule
        {
            UserId = user.Id,
            Name = "allow all",
            Effect = RuleEffect.Allow,
            Priority = 100,
            TargetPattern = "*",
            MethodPattern = "*",
            MaxRequestsPerMinute = 30,
        });
        _db.SaveChanges();

        var decision = await _engine.EvaluateAsync(user, HttpTarget(), "GET", default);

        Assert.True(decision.Allowed);
        Assert.Equal(30, decision.RateLimitPerMinute);
    }

    [Fact]
    public async Task Lower_Priority_Number_Wins()
    {
        var user = SeedUser();
        _db.Rules.AddRange(
            new Rule { UserId = user.Id, Name = "deny", Effect = RuleEffect.Deny, Priority = 10, TargetPattern = "*" },
            new Rule { UserId = user.Id, Name = "allow", Effect = RuleEffect.Allow, Priority = 20, TargetPattern = "*" });
        _db.SaveChanges();

        var decision = await _engine.EvaluateAsync(user, HttpTarget(), "GET", default);

        Assert.False(decision.Allowed);
        Assert.Equal("deny", decision.MatchedRule);
    }

    [Fact]
    public async Task Method_Pattern_Is_Respected_For_Http()
    {
        var user = SeedUser();
        _db.Rules.Add(new Rule
        {
            UserId = user.Id,
            Name = "reads only",
            Effect = RuleEffect.Allow,
            Priority = 100,
            TargetPattern = "*",
            MethodPattern = "GET",
        });
        _db.SaveChanges();

        Assert.True((await _engine.EvaluateAsync(user, HttpTarget(), "GET", default)).Allowed);
        Assert.False((await _engine.EvaluateAsync(user, HttpTarget(), "POST", default)).Allowed);
    }

    [Fact]
    public async Task Global_Rule_Applies_To_Any_User()
    {
        var user = SeedUser();
        _db.Rules.Add(new Rule
        {
            UserId = null, // global
            Name = "global allow",
            Effect = RuleEffect.Allow,
            Priority = 100,
            TargetPattern = "*",
        });
        _db.SaveChanges();

        var decision = await _engine.EvaluateAsync(user, HttpTarget(), "GET", default);

        Assert.True(decision.Allowed);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
