using System;
using System.Diagnostics;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

internal static class AkronDeloadSimulator {
    private const float MaxDelaySeconds = 3600f;

    public static float ClampDelaySeconds(float seconds) {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds)) {
            return 0f;
        }

        return Math.Min(MaxDelaySeconds, Math.Max(0f, seconds));
    }

    public static string Describe() {
        if (AkronModule.Settings == null) {
            return "Unavailable";
        }

        float delay = AkronModule.Settings.DeloadSpinnerDelaySeconds;
        return delay <= 0f ? "Now" : delay.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "s";
    }

    public static int Simulate(Level level, float beforeDeloadSeconds) {
        if (level == null) {
            return 0;
        }

        int steps = SimulationSteps(level, beforeDeloadSeconds);
        if (steps <= 0) {
            return 0;
        }

        Engine.FrameCounter += (ulong) steps;

        // This simulates visual timer drift only. Do not add the skipped
        // frames to level, session, or journal time; doing so corrupts player
        // stats by turning a visual helper into real playtime.
        Update(ref level.glitchTimer, steps);
        foreach (Backdrop backdrop in level.Foreground.Backdrops) {
            UpdateVanillaBackdrop(level, backdrop, steps);
        }

        foreach (Backdrop backdrop in level.Background.Backdrops) {
            UpdateVanillaBackdrop(level, backdrop, steps);
        }

        Update(ref level.Displacement.timer, steps);
        Update(ref level.WindSineTimer, steps);
        return steps;
    }

    public static int SimulationSteps(Level level, float beforeDeloadSeconds) {
        if (level == null) {
            return 0;
        }

        int steps = StepsToFreeze(level.TimeActive, Engine.DeltaTime);
        steps -= (int) (ClampDelaySeconds(beforeDeloadSeconds) / Engine.DeltaTime);
        return Math.Max(0, steps);
    }

    private static void Update(ref float field, int steps) {
        field = UpdateValue(field, Engine.DeltaTime, steps);
    }

    private static void UpdateVanillaBackdrop(Level level, Backdrop backdrop, int steps) {
        Stopwatch stopwatch = Stopwatch.StartNew();

        if (backdrop is DreamStars dreamStars) {
            for (int i = 0; i < dreamStars.stars.Length; i++) {
                var star = dreamStars.stars[i];
                dreamStars.stars[i].Position.X = UpdateValue(star.Position.X, (dreamStars.angle * star.Speed * Engine.DeltaTime).X, steps);
                dreamStars.stars[i].Position.Y = UpdateValue(star.Position.Y, (dreamStars.angle * star.Speed * Engine.DeltaTime).Y, steps);
            }
        } else if (backdrop is FinalBossStarfield finalBossStarfield) {
            for (int i = 0; i < finalBossStarfield.particles.Length; i++) {
                var particle = finalBossStarfield.particles[i];
                finalBossStarfield.particles[i].Position.X = UpdateValue(particle.Position.X, particle.Direction.X * particle.Speed * Engine.DeltaTime, steps);
                finalBossStarfield.particles[i].Position.Y = UpdateValue(particle.Position.Y, particle.Direction.Y * particle.Speed * Engine.DeltaTime, steps);
            }
        } else if (backdrop is NorthernLights northernLights) {
            if (northernLights.Visible) {
                northernLights.timer = UpdateValue(northernLights.timer, Engine.DeltaTime, steps);
                for (int i = 0; i < northernLights.particles.Length; i++) {
                    var particle = northernLights.particles[i];
                    northernLights.particles[i].Position.Y = UpdateValue(particle.Position.Y, particle.Speed * Engine.DeltaTime, steps);
                }
            }
        } else if (backdrop is Parallax parallax) {
            parallax.Position.X = UpdateValueMultipleAdditions(parallax.Position.X, steps, parallax.Speed.X * Engine.DeltaTime, parallax.WindMultiplier * level.Wind.X * Engine.DeltaTime);
            parallax.Position.Y = UpdateValueMultipleAdditions(parallax.Position.Y, steps, parallax.Speed.Y * Engine.DeltaTime, parallax.WindMultiplier * level.Wind.Y * Engine.DeltaTime);
        } else if (backdrop is Petals petals) {
            for (int i = 0; i < petals.particles.Length; i++) {
                var particle = petals.particles[i];
                petals.particles[i].Position.Y = UpdateValue(particle.Position.Y, particle.Speed * Engine.DeltaTime, steps);
                petals.particles[i].RotationCounter = UpdateValue(particle.RotationCounter, particle.Spin * Engine.DeltaTime, steps);
            }
        } else if (backdrop is RainFG rain) {
            for (int i = 0; i < rain.particles.Length; i++) {
                var particle = rain.particles[i];
                rain.particles[i].Position.Y = UpdateValue(particle.Position.Y, particle.Speed.Y * Engine.DeltaTime, steps);
                rain.particles[i].Position.X = UpdateValue(particle.Position.X, particle.Speed.X * Engine.DeltaTime, steps);
            }
        } else if (backdrop is Snow snow) {
            for (int i = 0; i < snow.particles.Length; i++) {
                var particle = snow.particles[i];
                snow.particles[i].Position.X = UpdateValue(particle.Position.X, particle.Speed * Engine.DeltaTime, steps);
                snow.particles[i].Sin = UpdateValue(particle.Sin, Engine.DeltaTime, steps);
            }
        } else if (backdrop is Starfield starfield) {
            for (int i = 0; i < starfield.Stars.Length; i++) {
                var star = starfield.Stars[i];
                starfield.Stars[i].Sine = UpdateValue(star.Sine, starfield.FlowSpeed * Engine.DeltaTime, steps);
            }
        } else if (backdrop is StarsBG starsBg) {
            if (starsBg.Visible) {
                for (int i = 0; i < starsBg.stars.Length; i++) {
                    var star = starsBg.stars[i];
                    starsBg.stars[i].Timer = UpdateValue(star.Timer, Engine.DeltaTime * star.Rate, steps);
                }

                if (level.Session.Dreaming) {
                    starsBg.falling = UpdateValue(starsBg.falling, Engine.DeltaTime * 12f, steps);
                }
            }
        } else if (backdrop is WindSnowFG windSnow) {
            Vector2 increment = level.Wind.Y != 0f
                ? new Vector2(0f, level.Wind.Y * 3f) * Engine.DeltaTime
                : new Vector2(level.Wind.X, 20f) * Engine.DeltaTime;
            for (int i = 0; i < windSnow.positions.Length; i++) {
                windSnow.positions[i].X = UpdateValue(windSnow.positions[i].X, increment.X, steps);
                windSnow.positions[i].Y = UpdateValue(windSnow.positions[i].Y, increment.Y, steps);
            }
        }

        if (stopwatch.ElapsedMilliseconds > 1) {
            Logger.Log(LogLevel.Verbose, "AkronDeloadSimulator", backdrop.GetType().Name + " simulation took " + stopwatch.ElapsedMilliseconds + " ms.");
        }
    }

    private static (float Value, int Steps) UpdateValueInternal(float value, float increment, int steps) {
        if (increment == 0f) {
            return (value, steps);
        }

        while (steps > 0) {
            if (Math.Abs(increment) >= Math.Abs(value) * 2f) {
                value += increment;
                steps--;
                continue;
            }

            if (Math.Sign(increment) != Math.Sign(value)) {
                float stepsToZero = -value / increment;
                if (stepsToZero > Math.Pow(2.0, 23.0)) {
                    return (value, steps);
                }

                if (stepsToZero >= steps) {
                    value += increment * steps;
                    return (value, 0);
                }

                value += increment * (float) Math.Ceiling(stepsToZero);
                steps -= (int) Math.Ceiling(stepsToZero);
                continue;
            }

            float nextValue = value + increment;
            float effectiveIncrement = nextValue - value;
            if (effectiveIncrement == 0f) {
                return (value, steps);
            }

            int exponentBits = (int) ((BitConverter.SingleToUInt32Bits(value) >> 23) & 0xFF);
            int exponent = exponentBits - 127;
            double exponentDistance = Math.Pow(2.0, exponent + 1) - Math.Abs(value);
            int batch = Math.Min(steps, (int) Math.Floor(Math.Abs(exponentDistance / effectiveIncrement)));
            value += effectiveIncrement * batch;
            steps -= batch;
            if (steps == 0) {
                return (value, steps);
            }

            value += increment;
            steps--;
        }

        return (value, steps);
    }

    private static float UpdateValueMultipleAdditions(float value, int steps, params float[] increments) {
        while (steps > 0) {
            float totalIncrement = 0f;
            foreach (float increment in increments) {
                totalIncrement += increment;
            }

            if (Math.Abs(totalIncrement) > Math.Abs(value)) {
                value += totalIncrement;
                steps--;
                continue;
            }

            if (Math.Sign(totalIncrement) != Math.Sign(value)) {
                float stepsToZero = -value / totalIncrement;
                if (stepsToZero > Math.Pow(2.0, 23.0)) {
                    return value;
                }

                if (stepsToZero >= steps) {
                    value += totalIncrement * steps;
                    return value;
                }

                value += totalIncrement * (float) Math.Ceiling(stepsToZero);
                steps -= (int) Math.Ceiling(stepsToZero);
                continue;
            }

            float nextValue = value;
            foreach (float increment in increments) {
                nextValue = value + increment;
            }

            float effectiveIncrement = nextValue - value;
            if (effectiveIncrement == 0f) {
                return value;
            }

            int exponentBits = (int) ((BitConverter.SingleToUInt32Bits(value) >> 23) & 0xFF);
            int exponent = exponentBits - 127;
            double exponentDistance = Math.Pow(2.0, exponent + 1) - Math.Abs(value);
            int batch = Math.Min(steps, (int) Math.Floor(Math.Abs(exponentDistance / effectiveIncrement)));
            value += effectiveIncrement * batch;
            steps -= batch;
            if (steps == 0) {
                return value;
            }

            foreach (float increment in increments) {
                value += increment;
            }

            steps--;
        }

        return value;
    }

    private static float UpdateValue(float value, float increment, int steps) {
        return UpdateValueInternal(value, increment, steps).Value;
    }

    private static int StepsToFreeze(float value, float increment) {
        return int.MaxValue - UpdateValueInternal(value, increment, int.MaxValue).Steps;
    }
}
