namespace DanceBridge.Core;

public record MatPanel(PadButton Source, string Symbol, string Label, int Row, int Column);

public static class WiiDdrLayout
{
    // Physical Konami Wii DDR layout. The center and bottom corners have no sensors.
    // Menu inputs follow GameCube conventions: Plus = Start, Minus = Z.
    public static IReadOnlyList<MatPanel> Panels { get; } = Array.AsReadOnly(new[] {
        new MatPanel(PadButton.Z, "−", "Minus", 0, 0),
        new MatPanel(PadButton.Start, "+", "Plus", 0, 2),
        new MatPanel(PadButton.B, "B", "Back", 1, 0),
        new MatPanel(PadButton.Up, "↑", "Up", 1, 1),
        new MatPanel(PadButton.A, "A", "Confirm", 1, 2),
        new MatPanel(PadButton.Left, "←", "Left", 2, 0),
        new MatPanel(PadButton.Right, "→", "Right", 2, 2),
        new MatPanel(PadButton.Down, "↓", "Down", 3, 1)
    });
    public static string Label(PadButton button) => button switch {
        PadButton.Start => "+ Plus", PadButton.Z => "− Minus", _ => button.ToString()
    };
}
