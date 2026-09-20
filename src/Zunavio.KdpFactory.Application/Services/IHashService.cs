using System.Text;
using System.Security.Cryptography;

namespace Zunavio.KdpFactory.Application.Services;

public interface IHashService
{
    string Sha256Hex(string input);
}

public sealed class Sha256HashService : IHashService
{
    public string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}