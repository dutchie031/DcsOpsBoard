using System.Collections.Concurrent;
using System.Threading.Channels;
using DcsOpsBoard.Hubs.MissionSync;
using DcsOpsBoard.MissionEditing;
using DcsOpsBoard.Services.MissionSync;

public class MissionCommandQueue(IMissionCache _cache) : IHostedService
{
    private readonly ConcurrentDictionary<Guid, Channel<CommandQueueItem>> _missionChannels = [];
    private readonly ConcurrentDictionary<Guid, Task> _processorTasks = [];
    private readonly CancellationTokenSource _shutdownTokenSource = new();

    public record CommandQueueItem(
        IMissionCommand Command,
        TaskCompletionSource<CommandResult> ResultTcs
    );

    public async Task<CommandResult> EnqueueCommand(IMissionCommand command)
    {
        // Get or create channel for this mission
        var channel = _missionChannels.GetOrAdd(
            command.MissionId,
            _ => CreateChannelForMission(command.MissionId)
        );

        // Create result awaiter
        var resultTcs = new TaskCompletionSource<CommandResult>();

        // Enqueue with result tracking
        await channel.Writer.WriteAsync(new CommandQueueItem(command, resultTcs));

        // Wait for processing to complete
        return await resultTcs.Task;
    }

    private Channel<CommandQueueItem> CreateChannelForMission(Guid missionId)
    {
        var channel = Channel.CreateUnbounded<CommandQueueItem>(new UnboundedChannelOptions
        {
            SingleReader = true, // Only one processor per mission
            SingleWriter = false  // Multiple hub connections can write
        });

        // Start dedicated processor for this mission
        var processorTask = ProcessMissionCommands(missionId, channel);
        _processorTasks[missionId] = processorTask;

        return channel;
    }

    private async Task ProcessMissionCommands(
        Guid missionId,
        Channel<CommandQueueItem> channel)
    {
        await foreach (var item in channel.Reader.ReadAllAsync(_shutdownTokenSource.Token))
        {
            try
            {
                await _cache.UpdateMission(missionId, item.Command.MissionStateId, mission =>
                {
                    var result = item.Command.ApplyToMission(mission).GetAwaiter().GetResult();
                    item.ResultTcs.SetResult(result);
                });
            }
            catch (InvalidOperationException ex)
            {
                item.ResultTcs.SetResult(CommandResult.Failure($"Mission is not in sync: {ex.Message}"));
            }
            catch (KeyNotFoundException ex)
            {
                item.ResultTcs.SetResult(CommandResult.Failure($"Mission not found: {ex.Message}"));
            }
            catch (Exception ex)
            {
                item.ResultTcs.SetResult(CommandResult.Failure($"Unexpected error in mission update: {ex.Message}"));
            }
        }
    }

    public void RemoveMission(Guid missionId)
    {
        if (_missionChannels.TryRemove(missionId, out var channel))
        {
            // Signal no more commands coming
            channel.Writer.Complete();
        }
    }

    // IHostedService implementation
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {

        // Signal shutdown to all processors
        _shutdownTokenSource.Cancel();

        // Complete all channels
        foreach (var channel in _missionChannels.Values)
        {
            channel.Writer.Complete();
        }

        // Wait for all processors to finish
        await Task.WhenAll(_processorTasks.Values);
    }
}
