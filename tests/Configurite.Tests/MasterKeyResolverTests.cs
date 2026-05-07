using Configurite.Encryption;
using FluentAssertions;

namespace Configurite.Tests;

[Collection("Environment")]
public sealed class MasterKeyResolverTests : IDisposable
{
    private readonly string? _originalEnv;

    public MasterKeyResolverTests()
    {
        _originalEnv = Environment.GetEnvironmentVariable(MasterKeyResolver.EnvironmentVariableName);
        Environment.SetEnvironmentVariable(MasterKeyResolver.EnvironmentVariableName, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(MasterKeyResolver.EnvironmentVariableName, _originalEnv);
    }

    [Fact]
    public void Resolve_ExplicitKey_TakesPrecedence()
    {
        Environment.SetEnvironmentVariable(MasterKeyResolver.EnvironmentVariableName, "from-env");

        var key = MasterKeyResolver.Resolve("explicit");

        key.Should().Be("explicit");
    }

    [Fact]
    public void Resolve_FallsBackToEnvironmentVariable()
    {
        Environment.SetEnvironmentVariable(MasterKeyResolver.EnvironmentVariableName, "from-env");

        var key = MasterKeyResolver.Resolve(null);

        key.Should().Be("from-env");
    }

    [Fact]
    public void Resolve_NoSource_ReturnsNull()
    {
        var key = MasterKeyResolver.Resolve(null);
        key.Should().BeNull();
    }

    [Fact]
    public void Require_NoSource_Throws()
    {
        var act = () => MasterKeyResolver.Require(null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*master key*");
    }
}
