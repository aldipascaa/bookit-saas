using BookIt.Api.Domain;

namespace BookIt.Api.Application.Auth;

public interface ITokenService
{
    string CreateToken(User user);
}
