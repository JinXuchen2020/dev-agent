using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Aggregates.ApiKeys;
using AgentPlatform.Domain.Aggregates.AuditLogs;
using AgentPlatform.Domain.Repositories;
using AgentPlatform.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AgentPlatform.Application.Tests.Security;

/// <summary>
/// Verifies the full API-key lifecycle assembly: rotation advances the version and
/// emits a <see cref="AuditActionType.KeyRotation"/> entry, and revocation deactivates
/// the key and emits a <see cref="AuditActionType.KeyRevoked"/> entry. These tests give
/// the aggregate's Rotate()/Revoke() behavior real coverage (previously dead code).
/// </summary>
public sealed class KeyRotationServiceTests
{
    private readonly IApiKeyRepository _repo = Substitute.For<IApiKeyRepository>();
    private readonly IApiKeyEncryptionService _encryption = Substitute.For<IApiKeyEncryptionService>();
    private readonly IAuditLogRepository _audit = Substitute.For<IAuditLogRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ILogger<KeyRotationService> _logger = Substitute.For<ILogger<KeyRotationService>>();

    private KeyRotationService CreateSut() =>
        new(_repo, _encryption, _audit, _uow, _logger);

    private static ApiKey NewKey() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "enc-original", "ak_abc12", "Test Key", "Viewer");

    [Fact]
    public async Task RotateKeyAsync_AdvancesVersion_AndEmitsRotationAudit()
    {
        var key = NewKey();
        _repo.GetByIdAsync(key.Id, Arg.Any<CancellationToken>()).Returns(key);
        _encryption.EncryptKey(Arg.Any<string>()).Returns(("enc-rotated", "ak_new12"));
        var sut = CreateSut();

        await sut.RotateKeyAsync(key.Id);

        Assert.Equal(2, key.KeyVersion);
        Assert.Equal("enc-rotated", key.EncryptedKeyHash);
        await _repo.Received(1).UpdateAsync(key, Arg.Any<CancellationToken>());
        _audit.Received(1).Add(Arg.Is<AuditLog>(a => a.Action == AuditActionType.KeyRotation));
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeKeyAsync_DeactivatesKey_AndEmitsRevokedAudit()
    {
        var key = NewKey();
        _repo.GetByIdAsync(key.Id, Arg.Any<CancellationToken>()).Returns(key);
        var sut = CreateSut();

        await sut.RevokeKeyAsync(key.Id);

        Assert.False(key.IsActive);
        Assert.NotNull(key.RevokedAt);
        await _repo.Received(1).UpdateAsync(key, Arg.Any<CancellationToken>());
        _audit.Received(1).Add(Arg.Is<AuditLog>(a => a.Action == AuditActionType.KeyRevoked));
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeKeyAsync_IsIdempotent_ForAlreadyInactiveKey()
    {
        var key = NewKey();
        key.Revoke(); // already inactive
        _repo.GetByIdAsync(key.Id, Arg.Any<CancellationToken>()).Returns(key);
        var sut = CreateSut();

        await sut.RevokeKeyAsync(key.Id);

        // No second write / audit for an already-revoked key.
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<ApiKey>(), Arg.Any<CancellationToken>());
        _audit.DidNotReceive().Add(Arg.Any<AuditLog>());
    }

    [Fact]
    public async Task RotateKeyAsync_MissingKey_IsSafeNoOp()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ApiKey?)null);
        var sut = CreateSut();

        await sut.RotateKeyAsync(Guid.NewGuid());

        await _repo.DidNotReceive().UpdateAsync(Arg.Any<ApiKey>(), Arg.Any<CancellationToken>());
        _audit.DidNotReceive().Add(Arg.Any<AuditLog>());
    }
}
