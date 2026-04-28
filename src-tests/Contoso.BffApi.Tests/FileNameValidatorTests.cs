using System.Reflection;
using FluentAssertions;

namespace Contoso.BffApi.Tests;

public class FileNameValidatorTests
{
    [Fact]
    public void IsSafeFileName_ValidName_ReturnsTrue()
    {
        InvokeIsSafeFileName("image.png").Should().BeTrue();
    }

    [Fact]
    public void IsSafeFileName_EmptyString_ReturnsFalse()
    {
        InvokeIsSafeFileName(string.Empty).Should().BeFalse();
    }

    [Fact]
    public void IsSafeFileName_InvalidChars_ReturnsFalse()
    {
        InvokeIsSafeFileName("bad:name.png").Should().BeFalse();
    }

    [Fact]
    public void IsSafeFileName_ForwardSlash_ReturnsFalse()
    {
        InvokeIsSafeFileName("dir/file.png").Should().BeFalse();
    }

    [Fact]
    public void IsSafeFileName_Backslash_ReturnsFalse()
    {
        InvokeIsSafeFileName("dir\\file.png").Should().BeFalse();
    }

    [Fact]
    public void IsSafeFileName_DoubleDot_ReturnsFalse()
    {
        InvokeIsSafeFileName("..file.png").Should().BeFalse();
    }

    private static bool InvokeIsSafeFileName(string filename)
    {
        var type = typeof(Program).Assembly.GetType("Contoso.BffApi.Services.FileNameValidator");
        type.Should().NotBeNull();
        var method = type!.GetMethod("IsSafeFileName", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (bool)method!.Invoke(null, new object?[] { filename })!;
    }
}
