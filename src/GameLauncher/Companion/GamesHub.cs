using Microsoft.AspNetCore.SignalR;

namespace GameLauncher.Companion;

// Receive-only: commands use REST and never accept executable paths.
public sealed class GamesHub(CompanionAccess access) : Hub
{
    public override Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        if (http?.Items[CompanionEndpoints.GenerationKey] is not long generation ||
            !access.RegisterConnection(Context.ConnectionId, generation, Context.Abort))
        {
            Context.Abort();
            return Task.CompletedTask;
        }
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        access.RemoveConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
