namespace HypeReborn.Hype.Player;

public sealed class HypeCharacterCommandBus
{
    private ICharacterCommandSource? _source;
    private HypeCharacterCommand? _injectedCommand;

    public void SetSource(ICharacterCommandSource? source)
    {
        _source = source;
    }

    public void PushInjectedCommand(HypeCharacterCommand command)
    {
        _injectedCommand = command;
    }

    public HypeCharacterCommand Poll()
    {
        if (_injectedCommand.HasValue)
        {
            var command = _injectedCommand.Value;
            _injectedCommand = null;
            return command;
        }

        return _source?.PollCommand() ?? HypeCharacterCommand.Empty;
    }
}
