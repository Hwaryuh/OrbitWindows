namespace Orbit.Core.Display;

/// <summary>A rectangle in physical pixels of the virtual screen (the primary monitor's top left is 0, 0).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    public PixelRect Intersect(PixelRect other)
    {
        int left = Math.Max(X, other.X), top = Math.Max(Y, other.Y);
        int right = Math.Min(Right, other.Right), bottom = Math.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top ? default : new PixelRect(left, top, right - left, bottom - top);
    }

    /// <summary>The same rectangle relative to <paramref name="origin"/>'s top left.</summary>
    public PixelRect RelativeTo(PixelRect origin) => this with { X = X - origin.X, Y = Y - origin.Y };

    /// <summary>The rectangle spanned by two corners in any order, as a drag gives them.</summary>
    public static PixelRect FromCorners(int x1, int y1, int x2, int y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));

    public override string ToString() => $"({X},{Y} {Width}x{Height})";
}
