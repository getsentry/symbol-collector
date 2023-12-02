using System.Security.Cryptography;

namespace SymbolCollector.Core;

public class SuffixGenerator
{
    private const string Characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public string Generate()
    {
        const int keyLength = 6;
        return string.Create(keyLength, Characters, (buffer, charSet) => {
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = charSet[RandomNumberGenerator.GetInt32(charSet.Length)];
            }
        });
    }
}