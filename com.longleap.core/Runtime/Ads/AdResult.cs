namespace LongLeap.Core.Ads
{
    // How an ad show ended.
    //
    // There is deliberately no separate Dismissed: portals (GamePix among them)
    // only report success or failure, so "the player closed the ad" cannot be
    // told apart from "there was no ad". Reporting a distinction the data does
    // not have would mislead the caller.
    public enum AdResult
    {
        // The ad was watched to the end. Only this result may grant a reward.
        Completed,

        // The show did not happen or was interrupted: closed, failed to load, timed out.
        Failed,

        // There are no ads at all: not a portal build, or a null provider.
        Unavailable
    }
}
