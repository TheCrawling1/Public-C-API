using ApiRouter.Models;

namespace ApiRouter.Dtos;

public record CreateUserRequest(string Name, bool IsAdmin = false);

public record UpdateUserRequest(string? Name, bool? IsActive, bool? IsAdmin);

/// <summary>Standard user representation. Never includes the API key (only its hash is stored).</summary>
public record UserResponse(int Id, string Name, bool IsActive, bool IsAdmin, DateTime CreatedAt)
{
    public static UserResponse From(User u) =>
        new(u.Id, u.Name, u.IsActive, u.IsAdmin, u.CreatedAt);
}

/// <summary>
/// Returned only when a user is created. Carries the raw API key exactly once —
/// it is not stored and cannot be retrieved again, so the caller must save it now.
/// </summary>
public record UserCreatedResponse(
    int Id, string Name, string ApiKey, bool IsActive, bool IsAdmin, DateTime CreatedAt)
{
    public static UserCreatedResponse From(User u, string rawApiKey) =>
        new(u.Id, u.Name, rawApiKey, u.IsActive, u.IsAdmin, u.CreatedAt);
}
