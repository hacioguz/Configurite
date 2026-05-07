using Configurite.Postgres;
using Configurite.Storage;
using FluentAssertions;

namespace Configurite.Tests;

/// <summary>
/// EN: Architectural tests for the Postgres backend. Real database round-trips would require
///     Testcontainers (out of scope here); these tests verify that the package implements
///     <see cref="IConfiguriteStore"/> correctly and that the constructor validates inputs.
/// TR: Postgres arka ucu için mimari testler. Gerçek veritabanı roundtrip'i Testcontainers
///     gerektirirdi (kapsam dışı); bu testler paketin <see cref="IConfiguriteStore"/> arayüzünü
///     doğru implement ettiğini ve constructor'ın girdileri doğruladığını kontrol eder.
/// </summary>
public sealed class PostgresStoreTests
{
    [Fact]
    public void PostgresConfiguriteStore_ImplementsIConfiguriteStore()
    {
        // EN: A dummy connection string is fine — we never call Open().
        // TR: Sahte bağlantı dizesi yeterli — Open() çağırmıyoruz.
        var store = new PostgresConfiguriteStore("Host=localhost;Database=test;Username=u;Password=p");

        // The point of the test: this must compile, and the polymorphism must hold.
        // Testin amacı: bu derlenmeli ve polimorfizm tutmalı.
        IConfiguriteStore polymorphic = store;
        polymorphic.Should().BeAssignableTo<IConfiguriteStore>();
    }

    [Fact]
    public void Constructor_RejectsEmptyConnectionString()
    {
        Action act = () => _ = new PostgresConfiguriteStore("");
        act.Should().Throw<ArgumentException>().WithMessage("*connectionString*");
    }

    [Fact]
    public void Constructor_RejectsEmptySchemaName()
    {
        Action act = () => _ = new PostgresConfiguriteStore("Host=h;Database=d;Username=u;Password=p", schemaName: "");
        act.Should().Throw<ArgumentException>().WithMessage("*schemaName*");
    }

    [Fact]
    public void Constructor_AcceptsCustomSchemaName()
    {
        // No throw is the assertion.
        // Hata fırlatmaması assertion'dır.
        var act = () => _ = new PostgresConfiguriteStore("Host=h;Database=d;Username=u;Password=p", "tenant42");
        act.Should().NotThrow();
    }
}
