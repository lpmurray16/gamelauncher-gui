namespace GameLauncher.Contracts;

// JSON uses camelCase properties and these exact enum strings on REST and SignalR.
// Kotlin models mirror this wire contract; they do not reference the .NET assembly.
public enum GameStatus { Stopped, Starting, Running, Stopping }

public sealed record DeviceInfoDto(Guid DeviceId, string Name, string Version, int ProtocolVersion = 1);
public sealed record PairingRequest(string Code);
public sealed record PairingResponse(DeviceInfoDto Device, string Token);
public sealed record GameDto(Guid Id, string Name, string? CoverUrl, GameStatus Status, bool CanTrackStatus);
public sealed record GameStatusChangedDto(Guid GameId, GameStatus Status, long Revision);
public sealed record CommandResponse(string Message);
