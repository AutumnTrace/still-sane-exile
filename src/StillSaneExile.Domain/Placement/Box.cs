namespace StillSaneExile.Domain.Placement;

/// <summary>A point on the screen, in physical pixels.</summary>
public readonly record struct Pos(double X, double Y)
{
    public double DistanceTo(Pos other) => Math.Sqrt(((X - other.X) * (X - other.X)) + ((Y - other.Y) * (Y - other.Y)));
}

/// <summary>A rectangle on the screen, in physical pixels: the game window, the game's tooltip, the card.</summary>
public readonly record struct Box(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public Pos Centre => new(X + (Width / 2), Y + (Height / 2));

    /// <summary>True when the two share any area; touching edges do not count.</summary>
    public bool Overlaps(Box other) => X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;

    public bool Within(Box area) => X >= area.X && Y >= area.Y && Right <= area.Right && Bottom <= area.Bottom;

    public Box MovedTo(double x, double y) => this with { X = x, Y = y };

    /// <summary>The point of the box nearest to <paramref name="p"/>: <paramref name="p"/> itself when inside.</summary>
    public Pos Nearest(Pos p) => new(Math.Clamp(p.X, X, Right), Math.Clamp(p.Y, Y, Bottom));

    /// <summary>Moved as little as it takes to lie inside <paramref name="area"/>; a box bigger than the area
    /// keeps its top or left edge on the area's.</summary>
    public Box KeptWithin(Box area) =>
        MovedTo(Math.Max(area.X, Math.Min(X, area.Right - Width)), Math.Max(area.Y, Math.Min(Y, area.Bottom - Height)));

    /// <summary>On whole pixels, where windows go.</summary>
    public Box Rounded() => new(Math.Round(X), Math.Round(Y), Math.Round(Width), Math.Round(Height));
}
