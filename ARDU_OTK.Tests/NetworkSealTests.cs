using System.Text.Json;
using ARDU_OTK.Shared.Network;

namespace ARDU_OTK.Tests;

public sealed class NetworkSealTests
{
    private const string Package = "{\"format\":\"ardu-otk-network\",\"payload\":\"…\"}";

    [Fact]
    public void Generated_code_is_valid_and_formatted()
    {
        var code = NetworkSeal.GenerateCode();

        Assert.Matches("^[A-Z2-9]{4}(-[A-Z2-9]{4}){5}$", code);
        Assert.NotNull(NetworkSeal.NormalizeCode(code.ToLowerInvariant().Replace("-", " ")));
        Assert.NotEqual(code, NetworkSeal.GenerateCode());
    }

    [Fact]
    public void Sealed_package_opens_with_its_code_only()
    {
        var code = NetworkSeal.GenerateCode();
        var sealedText = NetworkSeal.Seal(Package, code);

        Assert.DoesNotContain("ardu-otk-network\"", sealedText.Replace("ardu-otk-network-sealed", string.Empty));
        Assert.Equal(Package, NetworkSeal.TryOpen(sealedText, code.ToLowerInvariant(), out _));

        Assert.Null(NetworkSeal.TryOpen(sealedText, NetworkSeal.GenerateCode(), out var error));
        Assert.Contains("Код сети", error);
    }

    [Fact]
    public void Tampered_ciphertext_is_rejected()
    {
        var code = NetworkSeal.GenerateCode();
        var envelope = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(NetworkSeal.Seal(Package, code))!;
        var cipher = Convert.FromBase64String(envelope["ciphertext"].GetString()!);
        cipher[0] ^= 1;
        var tampered = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["format"] = envelope["format"].GetString()!,
            ["formatVersion"] = envelope["formatVersion"].GetInt32(),
            ["codeId"] = envelope["codeId"].GetString()!,
            ["nonce"] = envelope["nonce"].GetString()!,
            ["ciphertext"] = Convert.ToBase64String(cipher),
            ["tag"] = envelope["tag"].GetString()!,
        });

        Assert.Null(NetworkSeal.TryOpen(tampered, code, out var error));
        Assert.Contains("повреждён", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCD-EFGH")]
    [InlineData("ABCD-EFGH-IJKL-MNOP-QRST-UVW0")]
    public void Malformed_code_is_rejected(string code) => Assert.Null(NetworkSeal.NormalizeCode(code));
}
