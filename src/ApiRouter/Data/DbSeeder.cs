using ApiRouter.Auth;
using ApiRouter.Models;

namespace ApiRouter.Data;

/// <summary>
/// Seeds a demo user, two targets, and a permissive rule so the API is usable the
/// moment it starts. Because the rule engine is deny-by-default, the seeded allow
/// rule is what makes the wallpaper walkthrough in the README work out of the box.
/// </summary>
public static class DbSeeder
{
    public const string DemoApiKey = "demo-key-please-change";

    public static void Seed(RouterDbContext db, string adminApiKey)
    {
        if (db.Users.Any())
        {
            return;
        }

        var demo = new User
        {
            Name = "demo",
            ApiKeyHash = ApiKeyHasher.Hash(adminApiKey),
            IsActive = true,
            IsAdmin = true, // the seeded bootstrap admin; override the key via configuration in production
        };
        db.Users.Add(demo);
        db.SaveChanges();

        db.Targets.AddRange(
            new Target
            {
                Key = "httpbin",
                Name = "httpbin.org test API",
                Kind = TargetKind.Http,
                BaseUrl = "https://httpbin.org",
            },
            new Target
            {
                Key = "wallpaper",
                Name = "Set desktop wallpaper (local action)",
                Kind = TargetKind.Action,
                ActionName = SetWallpaperActionName,
            });

        // Deny-by-default: this single allow rule lets the demo user reach any
        // target with any method, rate-limited to 60 requests/minute per target.
        db.Rules.Add(new Rule
        {
            UserId = demo.Id,
            Name = "demo: allow all (rate limited)",
            Effect = RuleEffect.Allow,
            Priority = 100,
            TargetPattern = "*",
            MethodPattern = "*",
            MaxRequestsPerMinute = 60,
        });

        db.SaveChanges();
    }

    private const string SetWallpaperActionName = "set-wallpaper";
}
