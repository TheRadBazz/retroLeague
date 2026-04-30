namespace RetrowaveRocket
{
    public interface IRarePowerUp
    {
        RetrowaveRarePowerUpType RarePowerUpType { get; }
        bool ActivateServer(RetrowavePlayerController owner);
    }
}
