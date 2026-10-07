namespace LmuCareer.Core;

/// <summary>Switches for features that depend on what LMU supports.</summary>
public static class Features
{
    /// <summary>
    /// Handing the car to an AI teammate mid-race. LMU removed its AI takeover toggle in March 2024
    /// and said proper AI teammate driving would replace it; until it does, the player drives every
    /// lap, there's no minimum drive time, and the briefing shows a pit plan instead of driver
    /// swaps. Turning this back on restores the swap stint plans and minimum drive time scoring;
    /// handovers are already read from the results files (ControlAndAids).
    /// </summary>
    public const bool AiDriverSwaps = false;
}
