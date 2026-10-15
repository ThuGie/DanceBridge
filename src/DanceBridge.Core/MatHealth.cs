namespace DanceBridge.Core;

public sealed class MatHealth
{
    public static readonly PadButton[] Steps = [PadButton.Left, PadButton.Down, PadButton.Up, PadButton.Right, PadButton.B, PadButton.A, PadButton.Z, PadButton.Start, PadButton.Left | PadButton.Right, PadButton.Up | PadButton.Down];
    public int Step { get; private set; }
    public bool Complete => Step >= Steps.Length;
    public bool[] Passed { get; } = new bool[Steps.Length];
    public bool WaitingForRelease { get; private set; } = true;
    bool seen;
    int taps;
    PadButton previous;
    DateTimeOffset? holdStarted;
    DateTimeOffset? tapStarted;
    public string Prompt => Step < Steps.Length ? "Press and release " + string.Join(" + ", Enum.GetValues<PadButton>().Where(b=>b!=PadButton.None&&Steps[Step].HasFlag(b)).Select(WiiDdrLayout.Label)) : Step == Steps.Length ? $"Tap Left five times ({taps}/5)" : "Hold Up for two seconds, then release";
    public bool Finished => Step >= Steps.Length + 2;
    public bool RapidPassed { get; private set; }
    public bool HoldPassed { get; private set; }
    public double HoldProgress { get; private set; }
    public int TapCount => taps;
    public void Observe(PadButton buttons, DateTimeOffset? timestamp = null)
    {
        if (Finished) return;
        var now=timestamp??DateTimeOffset.UtcNow;
        if (WaitingForRelease) { if (buttons == PadButton.None) WaitingForRelease = false; return; }
        if(Step==Steps.Length){if(buttons==PadButton.Left&&previous==PadButton.None){if(tapStarted is null||now-tapStarted>TimeSpan.FromSeconds(2)){taps=0;tapStarted=now;}taps++;}previous=buttons;if(taps>=5&&buttons==PadButton.None){RapidPassed=true;Step++;WaitingForRelease=true;}return;}
        if(Step==Steps.Length+1){if(buttons==PadButton.Up){holdStarted??=now;HoldProgress=Math.Clamp((now-holdStarted.Value).TotalSeconds/2,0,1);}else if(buttons==PadButton.None&&holdStarted is {} start){if(now-start>=TimeSpan.FromSeconds(2)){HoldPassed=true;Step++;}holdStarted=null;HoldProgress=0;}else {holdStarted=null;HoldProgress=0;}return;}
        if (buttons == Steps[Step]) seen = true;
        if (seen && buttons == PadButton.None) { Passed[Step] = true; Step++; seen = false; WaitingForRelease = false; }
    }
    public void Skip() { if (!Finished) { Step++; seen = false; previous=PadButton.None;holdStarted=null;WaitingForRelease = true; } }
    public void ResetCapture(){seen=false;previous=PadButton.None;holdStarted=null;HoldProgress=0;tapStarted=null;taps=0;WaitingForRelease=true;}
}
