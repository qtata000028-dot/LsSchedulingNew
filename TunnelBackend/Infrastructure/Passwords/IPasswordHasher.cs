namespace TunnelBackend.Infrastructure.Passwords;

public interface IPasswordHasher
{
    string Hash(string plain);
    bool Verify(string plain, string hashed);
}
