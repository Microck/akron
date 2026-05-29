using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Celeste.Mod.Akron;

public static class AkronDebugSnapshot {
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo SceneRendererListField = typeof(Scene).GetField("<RendererList>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

    public static string Write(Level level, string tag) {
        string directory = Path.Combine(Everest.PathGame, "Saves", "AkronDebug");
        Directory.CreateDirectory(directory);
        string safeTag = SanitizeTag(tag);
        string path = Path.Combine(directory, "akron-debug-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + safeTag + ".json");
        File.WriteAllText(path, BuildJson(level, safeTag));
        return path;
    }

    public static string SanitizeTag(string tag) {
        string text = string.IsNullOrWhiteSpace(tag) ? "snapshot" : tag.Trim();
        StringBuilder builder = new StringBuilder(text.Length);
        foreach (char character in text) {
            builder.Append(char.IsLetterOrDigit(character) || character == '-' || character == '_' ? character : '-');
        }

        string sanitized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "snapshot" : sanitized;
    }

    private static string BuildJson(Level level, string tag) {
        Player player = level?.Tracker.GetEntity<Player>();
        AkronStartPos activeStartPos = AkronActions.GetActiveStartPos();
        StringBuilder builder = new StringBuilder(32768);
        JsonWriter json = new JsonWriter(builder);

        json.BeginObject();
        json.Property("schema", "akron-debug-snapshot-v1");
        json.Property("tag", tag);
        json.Property("utc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        json.Property("frame", Engine.FrameCounter);
        json.Property("sceneType", Engine.Scene?.GetType().FullName ?? "null");
        WriteEngine(json);
        WriteLevel(json, level);
        WritePlayer(json, player);
        WriteStartPos(json, level, activeStartPos);
        WriteRenderers(json, level);
        WriteMotionSmoothing(json);
        json.EndObject();
        return builder.ToString();
    }

    private static void WriteEngine(JsonWriter json) {
        json.BeginObject("engine");
        json.Property("width", Engine.Width);
        json.Property("height", Engine.Height);
        json.Property("viewWidth", Engine.ViewWidth);
        json.Property("viewHeight", Engine.ViewHeight);
        json.Property("viewport", FormatViewport(Engine.Viewport));
        json.Property("screenMatrix", FormatMatrix(Engine.ScreenMatrix));
        json.EndObject();
    }

    private static void WriteLevel(JsonWriter json, Level level) {
        json.BeginObject("level");
        if (level == null) {
            json.Property("present", false);
            json.EndObject();
            return;
        }

        json.Property("present", true);
        json.Property("sid", level.Session?.Area.GetSID() ?? string.Empty);
        json.Property("room", level.Session?.Level ?? string.Empty);
        json.Property("bounds", FormatRectangle(new Rectangle(level.Bounds.Left, level.Bounds.Top, level.Bounds.Width, level.Bounds.Height)));
        json.Property("timeActive", level.TimeActive);
        json.Property("rawTimeActive", level.RawTimeActive);
        json.Property("screenPadding", level.ScreenPadding);
        json.Property("shakeVector", FormatVector(level.ShakeVector));
        json.Property("zoom", level.Zoom);
        json.Property("zoomTarget", level.ZoomTarget);
        json.Property("zoomFocusPoint", FormatVector(level.ZoomFocusPoint));
        json.Property("cameraPosition", FormatVector(level.Camera.Position));
        json.Property("cameraX", level.Camera.X);
        json.Property("cameraY", level.Camera.Y);
        json.Property("cameraType", level.Camera.GetType().FullName);
        json.Property("cameraHash", ObjectHash(level.Camera));
        WriteObjectFields(json, "cameraFields", level.Camera, 2);
        json.Property("entityCount", level.Entities.Count);
        json.Property("playerCount", level.Tracker.GetEntities<Player>().Count);
        json.EndObject();
    }

    private static void WritePlayer(JsonWriter json, Player player) {
        json.BeginObject("player");
        if (player == null) {
            json.Property("present", false);
            json.EndObject();
            return;
        }

        json.Property("present", true);
        json.Property("hash", ObjectHash(player));
        json.Property("position", FormatVector(player.Position));
        json.Property("exactPosition", FormatVector(player.ExactPosition));
        json.Property("center", FormatVector(player.Center));
        json.Property("speed", FormatVector(player.Speed));
        json.Property("cameraTarget", FormatVector(player.CameraTarget));
        json.Property("cameraDeltaFromPosition", FormatVector(player.CameraTarget - player.Position));
        json.Property("depth", player.Depth);
        json.Property("active", player.Active);
        json.Property("visible", player.Visible);
        json.Property("collidable", player.Collidable);
        json.Property("dead", player.Dead);
        json.Property("dashes", player.Dashes);
        json.Property("stamina", player.Stamina);
        json.Property("state", player.StateMachine.State);
        json.Property("onGround", player.OnGround());
        json.Property("collidesSolid", player.CollideCheck<Solid>());
        json.Property("collidesSpikes", player.CollideCheck<Spikes>());
        if (Engine.Scene is Level level) {
            json.Property("screenFromPosition", FormatVector(WorldToScreen(level, player.Position)));
            json.Property("screenFromCenter", FormatVector(WorldToScreen(level, player.Center)));
        }
        json.EndObject();
    }

    private static void WriteStartPos(JsonWriter json, Level level, AkronStartPos startPos) {
        json.BeginObject("startPos");
        json.Property("activeSlot", AkronModule.Settings.ActiveStartPosSlot);
        json.Property("respawnAtStartPos", AkronModule.Settings.RespawnAtStartPos);
        json.Property("smartStartPos", AkronModule.Settings.SmartStartPos);
        json.Property("switcherIndex", level == null ? string.Empty : AkronActions.DescribeStartPosIndex(level));
        json.Property("hasActiveSlot", startPos != null);
        if (startPos != null) {
            json.Property("position", FormatVector(startPos.Position));
            json.Property("room", startPos.Room ?? string.Empty);
            json.Property("areaSid", startPos.AreaSid ?? string.Empty);
            json.Property("usesSpawnConfig", startPos.UsesSpawnConfig);
            json.Property("dashes", startPos.Dashes);
            json.Property("facing", startPos.Facing.ToString());
            json.Property("idle", startPos.Idle);
            json.Property("grab", startPos.Grab);
            json.Property("stateSlotName", startPos.StateSlotName ?? string.Empty);
            json.Property("hasState", AkronSaveLoadService.HasRuntimeState(startPos.StateSlotName));
            AkronSaveLoadSlot nativeState = AkronSaveLoadService.GetRuntimeStateForDebug(startPos.StateSlotName);
            if (nativeState?.SavedLevel != null) {
                json.BeginObject("savedLevel");
                json.Property("room", nativeState.SavedLevel.Session?.Level ?? string.Empty);
                json.Property("sid", nativeState.SavedLevel.Session?.Area.GetSID() ?? string.Empty);
                json.Property("cameraPosition", FormatVector(nativeState.SavedLevel.Camera.Position));
                json.Property("screenPadding", nativeState.SavedLevel.ScreenPadding);
                json.Property("zoom", nativeState.SavedLevel.Zoom);
                json.Property("zoomTarget", nativeState.SavedLevel.ZoomTarget);
                json.Property("zoomFocusPoint", FormatVector(nativeState.SavedLevel.ZoomFocusPoint));
                json.Property("rendererListHash", ObjectHash(SceneRendererListField?.GetValue(nativeState.SavedLevel)));
                json.EndObject();
            }
        }
        json.EndObject();
    }

    private static void WriteRenderers(JsonWriter json, Level level) {
        json.BeginObject("renderers");
        if (level == null) {
            json.Property("present", false);
            json.EndObject();
            return;
        }

        json.Property("present", true);
        object rendererList = SceneRendererListField?.GetValue(level);
        json.Property("rendererListHash", ObjectHash(rendererList));
        json.Property("rendererListType", rendererList?.GetType().FullName ?? "null");
        WriteRendererReference(json, "gameplayRenderer", level.GameplayRenderer);
        WriteRendererReference(json, "hudRenderer", level.HudRenderer);
        WriteRendererReference(json, "subHudRenderer", level.SubHudRenderer);
        WriteRendererReference(json, "background", level.Background);
        WriteRendererReference(json, "foreground", level.Foreground);
        WriteRendererReference(json, "lighting", level.Lighting);
        WriteRendererReference(json, "displacement", level.Displacement);
        WriteRendererReference(json, "bloom", level.Bloom);

        json.BeginArray("rendererListItems");
        if (rendererList != null) {
            foreach (object item in EnumerateRendererList(rendererList).Take(96)) {
                json.BeginObject();
                json.Property("type", item?.GetType().FullName ?? "null");
                json.Property("hash", ObjectHash(item));
                json.Property("visible", GetFieldOrPropertyValue(item, "Visible")?.ToString() ?? string.Empty);
                json.EndObject();
            }
        }
        json.EndArray();
        json.EndObject();
    }

    private static void WriteMotionSmoothing(JsonWriter json) {
        json.BeginObject("motionSmoothing");
        Type handler = FindType("Celeste.Mod.MotionSmoothing.Smoothing.MotionSmoothingHandler");
        Type module = FindType("Celeste.Mod.MotionSmoothing.MotionSmoothingModule");
        Type hires = FindType("Celeste.Mod.MotionSmoothing.Smoothing.Targets.HiresCameraSmoother");
        Type unlocked = FindType("Celeste.Mod.MotionSmoothing.Smoothing.Targets.UnlockedCameraSmoother");
        json.Property("handlerTypeFound", handler != null);
        json.Property("moduleTypeFound", module != null);
        json.Property("hiresTypeFound", hires != null);
        json.Property("unlockedTypeFound", unlocked != null);
        if (handler != null) {
            WriteStaticFields(json, "handlerStaticFields", handler);
            object instance = GetFieldOrPropertyValue(handler, "Instance");
            json.Property("handlerInstance", DescribeValue(instance));
            object player = GetFieldOrPropertyValue(instance, "Player");
            json.Property("handlerPlayerHash", ObjectHash(player));
            json.Property("handlerPlayerMatchesLevel", Engine.Scene is Level level && ReferenceEquals(player, level.Tracker.GetEntity<Player>()));
        }
        if (module != null) {
            WriteStaticFields(json, "moduleStaticFields", module);
        }
        if (hires != null) {
            WriteStaticFields(json, "hiresStaticFields", hires);
        }
        if (unlocked != null) {
            WriteStaticFields(json, "unlockedStaticFields", unlocked);
        }
        json.EndObject();
    }

    private static Type FindType(string name) {
        return Type.GetType(name + ", Akron") ?? AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name))
            .FirstOrDefault(type => type != null);
    }

    private static void WriteRendererReference(JsonWriter json, string name, object renderer) {
        json.BeginObject(name);
        json.Property("type", renderer?.GetType().FullName ?? "null");
        json.Property("hash", ObjectHash(renderer));
        json.Property("visible", GetFieldOrPropertyValue(renderer, "Visible")?.ToString() ?? string.Empty);
        object rendererCamera = GetFieldOrPropertyValue(renderer, "Camera");
        json.Property("cameraHash", ObjectHash(rendererCamera));
        json.Property("cameraMatchesLevel", Engine.Scene is Level level && ReferenceEquals(rendererCamera, level.Camera));
        WriteObjectFields(json, "fields", renderer, 1);
        json.EndObject();
    }

    private static IEnumerable<object> EnumerateRendererList(object rendererList) {
        foreach (FieldInfo field in rendererList.GetType().GetFields(InstanceFields)) {
            if (field.GetValue(rendererList) is IEnumerable enumerable && field.FieldType != typeof(string)) {
                foreach (object item in enumerable) {
                    yield return item;
                }
            }
        }
    }

    private static Vector2 WorldToScreen(Level level, Vector2 world) {
        Vector2 cameraRelative = world - level.Camera.Position;
        return Vector2.Transform(cameraRelative, Engine.ScreenMatrix);
    }

    private static void WriteObjectFields(JsonWriter json, string name, object value, int depth) {
        json.BeginObject(name);
        if (value == null || depth <= 0) {
            json.EndObject();
            return;
        }

        foreach (FieldInfo field in value.GetType().GetFields(InstanceFields).OrderBy(field => field.Name).Take(64)) {
            object fieldValue;
            try {
                fieldValue = field.GetValue(value);
            } catch {
                continue;
            }

            json.Property(field.Name, DescribeValue(fieldValue));
        }
        json.EndObject();
    }

    private static void WriteStaticFields(JsonWriter json, string name, Type type) {
        json.BeginObject(name);
        foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).OrderBy(field => field.Name).Take(96)) {
            object fieldValue;
            try {
                fieldValue = field.GetValue(null);
            } catch {
                continue;
            }

            json.Property(field.Name, DescribeValue(fieldValue));
        }
        json.EndObject();
    }

    private static object GetFieldOrPropertyValue(object value, string name) {
        if (value == null) {
            return null;
        }

        Type type = value as Type ?? value.GetType();
        BindingFlags flags = value is Type
            ? BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            : InstanceFields;
        object target = value is Type ? null : value;
        FieldInfo field = type.GetField(name, flags);
        if (field != null) {
            return field.GetValue(target);
        }

        PropertyInfo property = type.GetProperty(name, flags);
        return property != null && property.GetIndexParameters().Length == 0 ? property.GetValue(target) : null;
    }

    private static string DescribeValue(object value) {
        if (value == null) return "null";
        if (value is Vector2 vector) return FormatVector(vector);
        if (value is Rectangle rectangle) return FormatRectangle(rectangle);
        if (value is Matrix matrix) return FormatMatrix(matrix);
        if (value is Color color) return color.R + "," + color.G + "," + color.B + "," + color.A;
        if (value is bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or string) {
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
        return value.GetType().FullName + "#" + ObjectHash(value);
    }

    private static string ObjectHash(object value) {
        return value == null ? "null" : value.GetHashCode().ToString("X8", CultureInfo.InvariantCulture);
    }

    private static string FormatVector(Vector2 value) {
        return value.X.ToString("0.###", CultureInfo.InvariantCulture) + ", " + value.Y.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string FormatRectangle(Rectangle value) {
        return value.X.ToString(CultureInfo.InvariantCulture) + ", " + value.Y.ToString(CultureInfo.InvariantCulture) + ", " + value.Width.ToString(CultureInfo.InvariantCulture) + ", " + value.Height.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatViewport(Viewport value) {
        return value.X.ToString(CultureInfo.InvariantCulture) + ", " + value.Y.ToString(CultureInfo.InvariantCulture) + ", " + value.Width.ToString(CultureInfo.InvariantCulture) + ", " + value.Height.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatMatrix(Matrix value) {
        return string.Join(", ", new[] {
            value.M11, value.M12, value.M13, value.M14,
            value.M21, value.M22, value.M23, value.M24,
            value.M31, value.M32, value.M33, value.M34,
            value.M41, value.M42, value.M43, value.M44
        }.Select(component => component.ToString("0.###", CultureInfo.InvariantCulture)));
    }

    private sealed class JsonWriter {
        private readonly StringBuilder builder;
        private readonly Stack<bool> firstItem = new Stack<bool>();
        private int indent;

        public JsonWriter(StringBuilder builder) {
            this.builder = builder;
        }

        public void BeginObject(string name = null) {
            WriteName(name);
            builder.Append('{');
            firstItem.Push(true);
            indent++;
        }

        public void EndObject() {
            indent--;
            NewLine();
            builder.Append('}');
            firstItem.Pop();
        }

        public void BeginArray(string name) {
            WriteName(name);
            builder.Append('[');
            firstItem.Push(true);
            indent++;
        }

        public void EndArray() {
            indent--;
            NewLine();
            builder.Append(']');
            firstItem.Pop();
        }

        public void Property(string name, string value) {
            WriteName(name);
            builder.Append('"').Append(Escape(value ?? string.Empty)).Append('"');
        }

        public void Property(string name, int value) => PropertyRaw(name, value.ToString(CultureInfo.InvariantCulture));
        public void Property(string name, long value) => PropertyRaw(name, value.ToString(CultureInfo.InvariantCulture));
        public void Property(string name, ulong value) => PropertyRaw(name, value.ToString(CultureInfo.InvariantCulture));
        public void Property(string name, float value) => PropertyRaw(name, value.ToString("0.###", CultureInfo.InvariantCulture));
        public void Property(string name, bool value) => PropertyRaw(name, value ? "true" : "false");

        private void PropertyRaw(string name, string value) {
            WriteName(name);
            builder.Append(value);
        }

        private void WriteName(string name) {
            if (firstItem.Count > 0) {
                if (!firstItem.Pop()) {
                    builder.Append(',');
                }
                firstItem.Push(false);
                NewLine();
            }

            if (name != null) {
                builder.Append('"').Append(Escape(name)).Append("\": ");
            }
        }

        private void NewLine() {
            builder.AppendLine();
            builder.Append(' ', indent * 2);
        }

        private static string Escape(string value) {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
