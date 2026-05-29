using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronEntityInspector {
    private static List<Rectangle> ExactPixelRuns(Rectangle sampleBounds, System.Func<int, int, bool> collidesPixel, bool includeFill) {
        return ExactPixelRunSegments(sampleBounds.X, sampleBounds.Y, sampleBounds.Width, sampleBounds.Height, collidesPixel, includeFill)
            .Select(run => new Rectangle(run.X, run.Y, run.Width, 1))
            .ToList();
    }

    public static List<(int X, int Y, int Width)> ExactPixelRunSegments(int sampleX, int sampleY, int width, int height, System.Func<int, int, bool> collidesPixel, bool includeFill) {
        List<(int X, int Y, int Width)> runs = new List<(int X, int Y, int Width)>();
        int left = sampleX;
        int top = sampleY;
        int right = sampleX + width;
        int bottom = sampleY + height;
        for (int y = top; y < bottom; y++) {
            int runStart = -1;
            for (int x = left; x < right; x++) {
                bool collides = collidesPixel(x, y);
                bool shouldDraw = collides && (includeFill || IsExactEdgePixel(collidesPixel, x, y));
                if (shouldDraw) {
                    if (runStart < 0) {
                        runStart = x;
                    }
                    continue;
                }

                if (runStart >= 0) {
                    runs.Add((runStart, y, x - runStart));
                    runStart = -1;
                }
            }

            if (runStart >= 0) {
                runs.Add((runStart, y, right - runStart));
            }
        }

        return runs;
    }

    public static List<(int X, int Y, int Width)> PixelCircleRunSegments(float centerX, float centerY, float radius, bool includeFill) {
        int left = (int) System.Math.Floor(centerX - radius);
        int top = (int) System.Math.Floor(centerY - radius);
        int right = (int) System.Math.Ceiling(centerX + radius);
        int bottom = (int) System.Math.Ceiling(centerY + radius);
        float radiusSquared = radius * radius;

        return ExactPixelRunSegments(
            left,
            top,
            right - left,
            bottom - top,
            (x, y) => PixelCenterInsideCircle(x, y, centerX, centerY, radiusSquared),
            includeFill);
    }

    private static Rectangle ExactSampleBounds(Collider collider) {
        Rectangle bounds = ColliderWorldBounds(collider);
        bounds.Inflate(1, 1);
        return bounds;
    }

    private static bool IsExactEdgePixel(System.Func<int, int, bool> collidesPixel, int x, int y) {
        return !collidesPixel(x - 1, y) ||
               !collidesPixel(x + 1, y) ||
               !collidesPixel(x, y - 1) ||
               !collidesPixel(x, y + 1);
    }

    private static bool PixelCenterInsideCircle(int x, int y, float centerX, float centerY, float radiusSquared) {
        float dx = x + 0.5f - centerX;
        float dy = y + 0.5f - centerY;
        return dx * dx + dy * dy <= radiusSquared;
    }

    public static HashSet<(int X, int Y)> ExactPixelOutlineSamples(
        int sampleX,
        int sampleY,
        int width,
        int height,
        System.Func<int, int, bool> collidesPixel) {
        HashSet<(int X, int Y)> samples = new HashSet<(int X, int Y)>();
        foreach ((int x, int y, int runWidth) in ExactPixelRunSegments(sampleX, sampleY, width, height, collidesPixel, includeFill: false)) {
            for (int sample = x; sample < x + runWidth; sample++) {
                samples.Add((sample, y));
            }
        }

        return samples;
    }

    public static List<(int CellX, int CellY, GridEdge Edge)> GridOutlineEdges(int minCellX, int minCellY, int maxCellX, int maxCellY, System.Func<int, int, bool> cellFilled) {
        List<(int CellX, int CellY, GridEdge Edge)> edges = new List<(int CellX, int CellY, GridEdge Edge)>();
        for (int cellY = minCellY; cellY < maxCellY; cellY++) {
            for (int cellX = minCellX; cellX < maxCellX; cellX++) {
                if (!cellFilled(cellX, cellY)) {
                    continue;
                }

                if (!cellFilled(cellX, cellY - 1)) {
                    edges.Add((cellX, cellY, GridEdge.Top));
                }
                if (!cellFilled(cellX, cellY + 1)) {
                    edges.Add((cellX, cellY, GridEdge.Bottom));
                }
                if (!cellFilled(cellX - 1, cellY)) {
                    edges.Add((cellX, cellY, GridEdge.Left));
                }
                if (!cellFilled(cellX + 1, cellY)) {
                    edges.Add((cellX, cellY, GridEdge.Right));
                }
            }
        }

        return edges;
    }

    private static bool GridCellFilled(Grid grid, int cellX, int cellY) {
        return cellX >= 0 &&
               cellY >= 0 &&
               cellX < grid.CellsX &&
               cellY < grid.CellsY &&
               grid[cellX, cellY];
    }

    private static int ClampCellIndex(int cell, int cellCount) {
        if (cell < 0) {
            return 0;
        }

        return cell > cellCount ? cellCount : cell;
    }
}
