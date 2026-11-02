namespace Balance.Domain;

public static class ProjectionPosition
{
    public static void RequireNext(long current, long incoming)
    {
        if (incoming != current + 1) throw new InvalidOperationException("Projection sequence gap; retry after missing events.");
    }
}
