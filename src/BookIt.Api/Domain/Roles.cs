namespace BookIt.Api.Domain;

/// <summary>Role names stored on <see cref="User.Role"/> and emitted as JWT role claims.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Customer = "Customer";
}
