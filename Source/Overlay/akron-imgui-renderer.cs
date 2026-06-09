using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ImGuiNET;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Monocle;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using NumericsVector2 = System.Numerics.Vector2;

namespace Celeste.Mod.Akron;

internal sealed class AkronImGuiRenderer : IDisposable {
    private const float WheelDelta = 120f;
    private const float FontSize = 18f;

    private static AkronImGuiRenderer instance;
    private static bool nativeResolverRegistered;
    private static IntPtr nativeLibraryHandle;

    private readonly GraphicsDevice graphicsDevice;
    private readonly Dictionary<IntPtr, Texture2D> loadedTextures = new Dictionary<IntPtr, Texture2D>();
    private readonly Dictionary<string, IntPtr> embeddedTextureIds = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
    private readonly Keys[] allKeys = Enum.GetValues(typeof(Keys)).Cast<Keys>().ToArray();
    private readonly byte[] fontBytes;
    private readonly GCHandle fontHandle;

    private BasicEffect effect;
    private RasterizerState rasterizerState;
    private byte[] vertexData;
    private DynamicVertexBuffer vertexBuffer;
    private int vertexBufferSize;
    private byte[] indexData;
    private DynamicIndexBuffer indexBuffer;
    private int indexBufferSize;
    private int textureId = 1;
    private IntPtr? fontTextureId;
    private int scrollWheelValue;
    private KeyboardState previousKeyboard;
    private bool disposed;
    private static bool initializationFailed;
    private static int initializationRetryFrames;
    private static string lastFailure = string.Empty;
    private static bool renderDiagnosticLogged;
    private static bool renderInProgress;
#if DEBUG
    private static bool poisonNextFrameForTest;
#endif

    private AkronImGuiRenderer(GraphicsDevice graphicsDevice) {
        this.graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));

        EnsureNativeResolverRegistered();
        ImGui.SetCurrentContext(ImGui.CreateContext());
        ImGui.GetIO().ConfigFlags &= ~ImGuiConfigFlags.NavEnableKeyboard;

        fontBytes = LoadEmbeddedResource("poppins.ttf");
        fontHandle = GCHandle.Alloc(fontBytes, GCHandleType.Pinned);
        unsafe {
            ImFontConfigPtr fontConfig = new ImFontConfigPtr(ImGuiNative.ImFontConfig_ImFontConfig());
            fontConfig.FontDataOwnedByAtlas = false;
            ImGui.GetIO().Fonts.AddFontFromMemoryTTF(fontHandle.AddrOfPinnedObject(), fontBytes.Length, FontSize, fontConfig);
            ImGuiNative.ImFontConfig_destroy(fontConfig.NativePtr);
        }

        rasterizerState = new RasterizerState {
            CullMode = CullMode.None,
            DepthBias = 0,
            FillMode = FillMode.Solid,
            MultiSampleAntiAlias = false,
            ScissorTestEnable = true,
            SlopeScaleDepthBias = 0
        };

        RebuildFontAtlas();
    }

    public static bool WantCaptureKeyboard { get; private set; }
    public static bool WantCaptureMouse { get; private set; }

    public static void EnsureNativeResolverRegistered() {
        RegisterNativeResolver();
    }

    public static string StatusSummary {
        get {
            if (initializationFailed) {
                return "failed: " + (string.IsNullOrWhiteSpace(lastFailure) ? "unknown" : lastFailure);
            }

            return instance == null ? "not-initialized" : "ready";
        }
    }

    public static bool Render(Action drawLayout) {
        if (Engine.Instance?.GraphicsDevice == null || drawLayout == null) {
            return false;
        }

        if (renderInProgress) {
            return false;
        }

        if (initializationFailed && initializationRetryFrames-- > 0) {
            return false;
        }

        try {
            renderInProgress = true;
            initializationFailed = false;
            instance ??= new AkronImGuiRenderer(Engine.Instance.GraphicsDevice);
            instance.RenderFrame(drawLayout);
            lastFailure = string.Empty;
            return true;
        } catch (Exception exception) {
            initializationFailed = true;
            initializationRetryFrames = 120;
            lastFailure = exception.GetType().Name + ": " + exception.Message;
            Logger.Log(LogLevel.Error, nameof(AkronImGuiRenderer), "Akron ImGui overlay render failed; retrying after a short cooldown: " + exception);
            Engine.Scene?.Add(new AkronToast("Akron ImGui overlay failed. Check Everest log."));
            return false;
        } finally {
            renderInProgress = false;
        }
    }

#if DEBUG
    internal static void PoisonNextFrameForTest() {
        poisonNextFrameForTest = true;
    }
#endif

    public static void WarmUp() {
        if (Engine.Instance?.GraphicsDevice == null || initializationFailed) {
            return;
        }

        try {
            instance ??= new AkronImGuiRenderer(Engine.Instance.GraphicsDevice);
            instance.EnsureBufferCapacity(8192, 16384);
            instance.effect ??= new BasicEffect(instance.graphicsDevice);
        } catch (Exception exception) {
            initializationFailed = true;
            initializationRetryFrames = 120;
            lastFailure = exception.GetType().Name + ": " + exception.Message;
            Logger.Log(LogLevel.Warn, nameof(AkronImGuiRenderer), "Akron ImGui warmup failed; the overlay will retry later: " + exception.Message);
        }
    }

    public static IntPtr GetEmbeddedTextureId(string suffix) {
        if (Engine.Instance?.GraphicsDevice == null || string.IsNullOrWhiteSpace(suffix)) {
            return IntPtr.Zero;
        }

        instance ??= new AkronImGuiRenderer(Engine.Instance.GraphicsDevice);
        return instance.GetOrLoadEmbeddedTextureId(suffix);
    }

    public void Dispose() {
        if (disposed) {
            return;
        }

        disposed = true;
        if (fontHandle.IsAllocated) {
            fontHandle.Free();
        }

        foreach (Texture2D texture in loadedTextures.Values) {
            texture.Dispose();
        }
        loadedTextures.Clear();
        embeddedTextureIds.Clear();
        vertexBuffer?.Dispose();
        indexBuffer?.Dispose();
        effect?.Dispose();
        rasterizerState?.Dispose();
    }

    private unsafe void RebuildFontAtlas() {
        ImGuiIOPtr io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out byte* pixelData, out int width, out int height, out int bytesPerPixel);

        byte[] pixels = new byte[width * height * bytesPerPixel];
        Marshal.Copy(new IntPtr(pixelData), pixels, 0, pixels.Length);

        Texture2D texture = new Texture2D(graphicsDevice, width, height, false, SurfaceFormat.Color);
        texture.SetData(pixels);

        if (fontTextureId.HasValue) {
            UnbindTexture(fontTextureId.Value);
        }

        fontTextureId = BindTexture(texture);
        io.Fonts.SetTexID(fontTextureId.Value);
        io.Fonts.ClearTexData();
    }

    private IntPtr BindTexture(Texture2D texture) {
        IntPtr id = new IntPtr(textureId++);
        loadedTextures.Add(id, texture);
        return id;
    }

    private IntPtr GetOrLoadEmbeddedTextureId(string suffix) {
        if (embeddedTextureIds.TryGetValue(suffix, out IntPtr id)) {
            return id;
        }

        byte[] bytes = LoadEmbeddedResource(suffix);
        using MemoryStream stream = new MemoryStream(bytes);
        Texture2D texture = Texture2D.FromStream(graphicsDevice, stream);
        id = BindTexture(texture);
        embeddedTextureIds[suffix] = id;
        return id;
    }

    private void UnbindTexture(IntPtr textureId) {
        if (loadedTextures.TryGetValue(textureId, out Texture2D texture)) {
            texture.Dispose();
        }
        loadedTextures.Remove(textureId);
    }

    private void RenderFrame(Action drawLayout) {
        long frameStart = Stopwatch.GetTimestamp();
        UpdateInput();
        long inputEnd = Stopwatch.GetTimestamp();
        ApplyTheme();

        ImGui.NewFrame();
        drawLayout();
        long layoutEnd = Stopwatch.GetTimestamp();
        ImGui.Render();
        long imguiEnd = Stopwatch.GetTimestamp();

        WantCaptureKeyboard = ImGui.GetIO().WantCaptureKeyboard;
        WantCaptureMouse = ImGui.GetIO().WantCaptureMouse;
        if (!renderDiagnosticLogged) {
            renderDiagnosticLogged = true;
            ImDrawDataPtr diagnosticDrawData = ImGui.GetDrawData();
            Logger.Log(LogLevel.Info, nameof(AkronImGuiRenderer), "ImGui draw data: cmdLists=" + diagnosticDrawData.CmdListsCount + ", vertices=" + diagnosticDrawData.TotalVtxCount + ", indices=" + diagnosticDrawData.TotalIdxCount + ".");
        }
#if DEBUG
        if (poisonNextFrameForTest) {
            poisonNextFrameForTest = false;
            PoisonGraphicsStateForTest(graphicsDevice);
        }

        GraphicsDeviceStateSnapshot beforeState = GraphicsDeviceStateSnapshot.Capture(graphicsDevice);
#endif

        using (new GraphicsDeviceStateScope(graphicsDevice)) {
            unsafe {
                RenderDrawData(ImGui.GetDrawData());
            }
        }

#if DEBUG
        GraphicsDeviceStateSnapshot afterState = GraphicsDeviceStateSnapshot.Capture(graphicsDevice);
        if (!beforeState.EquivalentTo(afterState, out string diff)) {
            Logger.Log(LogLevel.Error, "AkronRenderState", "Graphics state leak after Akron overlay render:\n" + diff);
        }
#endif

        long drawEnd = Stopwatch.GetTimestamp();
        AkronPerformanceTelemetry.RecordOverlayRenderCost(
            ElapsedMilliseconds(frameStart, inputEnd),
            ElapsedMilliseconds(inputEnd, layoutEnd),
            ElapsedMilliseconds(layoutEnd, imguiEnd),
            ElapsedMilliseconds(imguiEnd, drawEnd));
    }

    private static double ElapsedMilliseconds(long start, long end) {
        return (end - start) * 1000.0 / Stopwatch.Frequency;
    }

#if DEBUG
    private static void PoisonGraphicsStateForTest(GraphicsDevice graphicsDevice) {
        graphicsDevice.ScissorRectangle = new XnaRectangle(17, 19, 123, 127);
        graphicsDevice.BlendState = BlendState.AlphaBlend;
        graphicsDevice.DepthStencilState = DepthStencilState.None;
        graphicsDevice.RasterizerState = RasterizerState.CullNone;
    }
#endif

    private void UpdateInput() {
        ImGuiIOPtr io = ImGui.GetIO();
        float elapsed = Engine.RawDeltaTime > 0f ? Engine.RawDeltaTime : 1f / 60f;
        io.DeltaTime = elapsed;
        io.DisplaySize = new NumericsVector2(
            graphicsDevice.PresentationParameters.BackBufferWidth,
            graphicsDevice.PresentationParameters.BackBufferHeight);
        io.DisplayFramebufferScale = new NumericsVector2(1f, 1f);

        MouseState mouse = Mouse.GetState();
        io.AddMousePosEvent(mouse.X, mouse.Y);
        io.AddMouseButtonEvent(0, mouse.LeftButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(1, mouse.RightButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(2, mouse.MiddleButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(3, mouse.XButton1 == ButtonState.Pressed);
        io.AddMouseButtonEvent(4, mouse.XButton2 == ButtonState.Pressed);
        io.AddMouseWheelEvent(0f, (mouse.ScrollWheelValue - scrollWheelValue) / WheelDelta);
        scrollWheelValue = mouse.ScrollWheelValue;

        KeyboardState keyboard = Keyboard.GetState();
        foreach (Keys key in allKeys) {
            if (TryMapKey(key, out ImGuiKey imguiKey)) {
                io.AddKeyEvent(imguiKey, keyboard.IsKeyDown(key));
            }

            if (keyboard.IsKeyDown(key) && !previousKeyboard.IsKeyDown(key) && TryGetInputCharacter(keyboard, key, out char character)) {
                io.AddInputCharacter(character);
            }
        }

        io.AddKeyEvent(ImGuiKey.ModCtrl, keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl));
        io.AddKeyEvent(ImGuiKey.ModShift, keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift));
        io.AddKeyEvent(ImGuiKey.ModAlt, keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt));
        previousKeyboard = keyboard;
    }

    private static void ApplyTheme() {
        ImGuiStylePtr style = ImGui.GetStyle();
        style.WindowPadding = new NumericsVector2(4f, 4f);
        style.FramePadding = new NumericsVector2(3f, 3f);
        style.ItemSpacing = new NumericsVector2(12f, 2f);
        style.ItemInnerSpacing = new NumericsVector2(8f, 6f);
        style.WindowRounding = 0f;
        style.FrameRounding = 4f;
        style.WindowBorderSize = 0f;
        style.ScrollbarSize = 10f;

        style.Colors[(int) ImGuiCol.WindowBg] = AkronImGuiTheme.Background;
        style.Colors[(int) ImGuiCol.TitleBg] = AkronImGuiTheme.Accent;
        style.Colors[(int) ImGuiCol.TitleBgActive] = AkronImGuiTheme.Accent;
        style.Colors[(int) ImGuiCol.TitleBgCollapsed] = AkronImGuiTheme.Accent;
        style.Colors[(int) ImGuiCol.Text] = AkronImGuiTheme.Foreground;
        style.Colors[(int) ImGuiCol.Button] = AkronImGuiTheme.Transparent;
        style.Colors[(int) ImGuiCol.ButtonHovered] = AkronImGuiTheme.ButtonHovered;
        style.Colors[(int) ImGuiCol.ButtonActive] = AkronImGuiTheme.ButtonActive;
        style.Colors[(int) ImGuiCol.FrameBg] = AkronImGuiTheme.FrameBackground;
        style.Colors[(int) ImGuiCol.FrameBgHovered] = AkronImGuiTheme.ButtonHovered;
        style.Colors[(int) ImGuiCol.FrameBgActive] = AkronImGuiTheme.ButtonActive;
        style.Colors[(int) ImGuiCol.PopupBg] = AkronImGuiTheme.Background;
        style.Colors[(int) ImGuiCol.Border] = AkronImGuiTheme.Transparent;
    }

    private Effect UpdateEffect(Texture2D texture) {
        effect ??= new BasicEffect(graphicsDevice);

        ImGuiIOPtr io = ImGui.GetIO();
        effect.World = Matrix.Identity;
        effect.View = Matrix.Identity;
        effect.Projection = Matrix.CreateOrthographicOffCenter(0f, io.DisplaySize.X, io.DisplaySize.Y, 0f, -1f, 1f);
        effect.TextureEnabled = true;
        effect.Texture = texture;
        effect.VertexColorEnabled = true;
        return effect;
    }

    private unsafe void RenderDrawData(ImDrawDataPtr drawData) {
        if (drawData.TotalVtxCount == 0) {
            return;
        }

        Viewport lastViewport = graphicsDevice.Viewport;
        XnaRectangle lastScissorBox = graphicsDevice.ScissorRectangle;
        RasterizerState lastRasterizer = graphicsDevice.RasterizerState;
        DepthStencilState lastDepthStencil = graphicsDevice.DepthStencilState;
        XnaColor lastBlendFactor = graphicsDevice.BlendFactor;
        BlendState lastBlendState = graphicsDevice.BlendState;
        IndexBuffer lastIndices = graphicsDevice.Indices;

        graphicsDevice.BlendFactor = XnaColor.White;
        graphicsDevice.BlendState = BlendState.NonPremultiplied;
        graphicsDevice.RasterizerState = rasterizerState;
        graphicsDevice.DepthStencilState = DepthStencilState.None;
        graphicsDevice.Viewport = new Viewport(0, 0, graphicsDevice.PresentationParameters.BackBufferWidth, graphicsDevice.PresentationParameters.BackBufferHeight);

        drawData.ScaleClipRects(ImGui.GetIO().DisplayFramebufferScale);
        UpdateBuffers(drawData);
        RenderCommandLists(drawData);

        graphicsDevice.Viewport = lastViewport;
        graphicsDevice.ScissorRectangle = lastScissorBox;
        graphicsDevice.RasterizerState = lastRasterizer;
        graphicsDevice.DepthStencilState = lastDepthStencil;
        graphicsDevice.BlendState = lastBlendState;
        graphicsDevice.BlendFactor = lastBlendFactor;
        graphicsDevice.Indices = lastIndices;
    }

    private unsafe void UpdateBuffers(ImDrawDataPtr drawData) {
        EnsureBufferCapacity(drawData.TotalVtxCount, drawData.TotalIdxCount);

        int vertexOffset = 0;
        int indexOffset = 0;
        for (int listIndex = 0; listIndex < drawData.CmdListsCount; listIndex++) {
            ImDrawListPtr commandList = drawData.CmdLists[listIndex];
            fixed (void* vertexDestination = &vertexData[vertexOffset * AkronImGuiDrawVertDeclaration.Size])
            fixed (void* indexDestination = &indexData[indexOffset * sizeof(ushort)]) {
                Buffer.MemoryCopy((void*) commandList.VtxBuffer.Data, vertexDestination, vertexData.Length, commandList.VtxBuffer.Size * AkronImGuiDrawVertDeclaration.Size);
                Buffer.MemoryCopy((void*) commandList.IdxBuffer.Data, indexDestination, indexData.Length, commandList.IdxBuffer.Size * sizeof(ushort));
            }

            vertexOffset += commandList.VtxBuffer.Size;
            indexOffset += commandList.IdxBuffer.Size;
        }

        // The menu geometry changes every frame as hover/input state changes.
        // Dynamic buffers with discard avoid stalling on the previous frame's
        // GPU reads, which is visible as a hard FPS drop when the menu opens.
        vertexBuffer.SetData(vertexData, 0, drawData.TotalVtxCount * AkronImGuiDrawVertDeclaration.Size, SetDataOptions.Discard);
        indexBuffer.SetData(indexData, 0, drawData.TotalIdxCount * sizeof(ushort), SetDataOptions.Discard);
    }

    private void EnsureBufferCapacity(int vertexCount, int indexCount) {
        if (vertexCount > vertexBufferSize) {
            vertexBuffer?.Dispose();
            vertexBufferSize = Math.Max(vertexCount, (int) (vertexCount * 1.5f));
            vertexBuffer = new DynamicVertexBuffer(graphicsDevice, AkronImGuiDrawVertDeclaration.Declaration, vertexBufferSize, BufferUsage.WriteOnly);
            vertexData = new byte[vertexBufferSize * AkronImGuiDrawVertDeclaration.Size];
        }

        if (indexCount > indexBufferSize) {
            indexBuffer?.Dispose();
            indexBufferSize = Math.Max(indexCount, (int) (indexCount * 1.5f));
            indexBuffer = new DynamicIndexBuffer(graphicsDevice, IndexElementSize.SixteenBits, indexBufferSize, BufferUsage.WriteOnly);
            indexData = new byte[indexBufferSize * sizeof(ushort)];
        }
    }

    private unsafe void RenderCommandLists(ImDrawDataPtr drawData) {
        graphicsDevice.SetVertexBuffer(vertexBuffer);
        graphicsDevice.Indices = indexBuffer;

        int vertexOffset = 0;
        int indexOffset = 0;
        for (int listIndex = 0; listIndex < drawData.CmdListsCount; listIndex++) {
            ImDrawListPtr commandList = drawData.CmdLists[listIndex];
            for (int commandIndex = 0; commandIndex < commandList.CmdBuffer.Size; commandIndex++) {
                ImDrawCmdPtr drawCommand = commandList.CmdBuffer[commandIndex];
                if (drawCommand.ElemCount == 0) {
                    continue;
                }

                if (!loadedTextures.TryGetValue(drawCommand.TextureId, out Texture2D texture)) {
                    continue;
                }

                graphicsDevice.ScissorRectangle = new XnaRectangle(
                    Math.Max(0, (int) drawCommand.ClipRect.X),
                    Math.Max(0, (int) drawCommand.ClipRect.Y),
                    Math.Max(0, (int) (drawCommand.ClipRect.Z - drawCommand.ClipRect.X)),
                    Math.Max(0, (int) (drawCommand.ClipRect.W - drawCommand.ClipRect.Y)));

                Effect activeEffect = UpdateEffect(texture);
                foreach (EffectPass pass in activeEffect.CurrentTechnique.Passes) {
                    pass.Apply();
#pragma warning disable CS0618
                    graphicsDevice.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        (int) drawCommand.VtxOffset + vertexOffset,
                        0,
                        commandList.VtxBuffer.Size,
                        (int) drawCommand.IdxOffset + indexOffset,
                        (int) drawCommand.ElemCount / 3);
#pragma warning restore CS0618
                }
            }

            vertexOffset += commandList.VtxBuffer.Size;
            indexOffset += commandList.IdxBuffer.Size;
        }
    }

    private static bool TryMapKey(Keys key, out ImGuiKey imguiKey) {
        imguiKey = key switch {
            Keys.Back => ImGuiKey.Backspace,
            Keys.Tab => ImGuiKey.Tab,
            Keys.Enter => ImGuiKey.Enter,
            Keys.Escape => ImGuiKey.Escape,
            Keys.Space => ImGuiKey.Space,
            Keys.PageUp => ImGuiKey.PageUp,
            Keys.PageDown => ImGuiKey.PageDown,
            Keys.End => ImGuiKey.End,
            Keys.Home => ImGuiKey.Home,
            Keys.Left => ImGuiKey.LeftArrow,
            Keys.Right => ImGuiKey.RightArrow,
            Keys.Up => ImGuiKey.UpArrow,
            Keys.Down => ImGuiKey.DownArrow,
            Keys.Insert => ImGuiKey.Insert,
            Keys.Delete => ImGuiKey.Delete,
            >= Keys.D0 and <= Keys.D9 => ImGuiKey._0 + (key - Keys.D0),
            >= Keys.A and <= Keys.Z => ImGuiKey.A + (key - Keys.A),
            >= Keys.NumPad0 and <= Keys.NumPad9 => ImGuiKey.Keypad0 + (key - Keys.NumPad0),
            >= Keys.F1 and <= Keys.F24 => ImGuiKey.F1 + (key - Keys.F1),
            Keys.OemSemicolon => ImGuiKey.Semicolon,
            Keys.OemPlus => ImGuiKey.Equal,
            Keys.OemComma => ImGuiKey.Comma,
            Keys.OemMinus => ImGuiKey.Minus,
            Keys.OemPeriod => ImGuiKey.Period,
            Keys.OemQuestion => ImGuiKey.Slash,
            Keys.OemTilde => ImGuiKey.GraveAccent,
            Keys.OemOpenBrackets => ImGuiKey.LeftBracket,
            Keys.OemCloseBrackets => ImGuiKey.RightBracket,
            Keys.OemPipe => ImGuiKey.Backslash,
            Keys.OemQuotes => ImGuiKey.Apostrophe,
            _ => ImGuiKey.None
        };
        return imguiKey != ImGuiKey.None;
    }

    private static bool TryGetInputCharacter(KeyboardState keyboard, Keys key, out char character) {
        bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        if (key >= Keys.A && key <= Keys.Z) {
            character = (char) ((shift ? 'A' : 'a') + (key - Keys.A));
            return true;
        }

        if (key >= Keys.D0 && key <= Keys.D9) {
            character = (char) ('0' + (key - Keys.D0));
            return true;
        }

        character = key switch {
            Keys.Space => ' ',
            Keys.OemMinus => shift ? '_' : '-',
            Keys.OemPlus => shift ? '+' : '=',
            Keys.OemComma => shift ? '<' : ',',
            Keys.OemPeriod => shift ? '>' : '.',
            Keys.OemQuestion => shift ? '?' : '/',
            Keys.OemOpenBrackets => shift ? '{' : '[',
            Keys.OemCloseBrackets => shift ? '}' : ']',
            Keys.OemPipe => shift ? '|' : '\\',
            Keys.OemSemicolon => shift ? ':' : ';',
            Keys.OemQuotes => shift ? '"' : '\'',
            _ => '\0'
        };
        return character != '\0';
    }

    private static byte[] LoadEmbeddedResource(string suffix) {
        Assembly assembly = typeof(AkronImGuiRenderer).Assembly;
        string resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (resourceName == null) {
            throw new InvalidOperationException("Missing embedded Akron ImGui resource: " + suffix);
        }

        using Stream stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) {
            throw new InvalidOperationException("Unable to open embedded Akron ImGui resource: " + resourceName);
        }

        using MemoryStream memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private sealed class GraphicsDeviceStateScope : IDisposable {
        private readonly GraphicsDevice graphicsDevice;
        private readonly RenderTargetBinding[] renderTargets;
        private readonly Viewport viewport;
        private readonly XnaRectangle scissorRectangle;
        private readonly RasterizerState rasterizerState;
        private readonly DepthStencilState depthStencilState;
        private readonly BlendState blendState;
        private readonly XnaColor blendFactor;
        private readonly IndexBuffer indices;
        private readonly VertexBufferBinding[] vertexBuffers;
        private readonly Texture texture0;
        private readonly SamplerState sampler0;

        public GraphicsDeviceStateScope(GraphicsDevice graphicsDevice) {
            this.graphicsDevice = graphicsDevice;
            renderTargets = graphicsDevice.GetRenderTargets();
            viewport = graphicsDevice.Viewport;
            scissorRectangle = graphicsDevice.ScissorRectangle;
            rasterizerState = graphicsDevice.RasterizerState;
            depthStencilState = graphicsDevice.DepthStencilState;
            blendState = graphicsDevice.BlendState;
            blendFactor = graphicsDevice.BlendFactor;
            indices = graphicsDevice.Indices;
            vertexBuffers = graphicsDevice.GetVertexBuffers();
            texture0 = graphicsDevice.Textures[0];
            sampler0 = graphicsDevice.SamplerStates[0];
        }

        public void Dispose() {
            if (renderTargets.Length == 0) {
                graphicsDevice.SetRenderTarget(null);
            } else {
                graphicsDevice.SetRenderTargets(renderTargets);
            }

            graphicsDevice.Viewport = viewport;
            graphicsDevice.ScissorRectangle = scissorRectangle;
            graphicsDevice.RasterizerState = rasterizerState;
            graphicsDevice.DepthStencilState = depthStencilState;
            graphicsDevice.BlendState = blendState;
            graphicsDevice.BlendFactor = blendFactor;
            graphicsDevice.Indices = indices;
            graphicsDevice.SetVertexBuffers(vertexBuffers);
            graphicsDevice.Textures[0] = texture0;
            graphicsDevice.SamplerStates[0] = sampler0;
        }
    }

#if DEBUG
    private sealed class GraphicsDeviceStateSnapshot {
        private readonly RenderTargetBinding[] renderTargets;
        private readonly Viewport viewport;
        private readonly XnaRectangle scissorRectangle;
        private readonly RasterizerState rasterizerState;
        private readonly DepthStencilState depthStencilState;
        private readonly BlendState blendState;
        private readonly XnaColor blendFactor;
        private readonly IndexBuffer indices;
        private readonly VertexBufferBinding[] vertexBuffers;
        private readonly Texture texture0;
        private readonly SamplerState sampler0;

        private GraphicsDeviceStateSnapshot(GraphicsDevice graphicsDevice) {
            renderTargets = graphicsDevice.GetRenderTargets();
            viewport = graphicsDevice.Viewport;
            scissorRectangle = graphicsDevice.ScissorRectangle;
            rasterizerState = graphicsDevice.RasterizerState;
            depthStencilState = graphicsDevice.DepthStencilState;
            blendState = graphicsDevice.BlendState;
            blendFactor = graphicsDevice.BlendFactor;
            indices = graphicsDevice.Indices;
            vertexBuffers = graphicsDevice.GetVertexBuffers();
            texture0 = graphicsDevice.Textures[0];
            sampler0 = graphicsDevice.SamplerStates[0];
        }

        public static GraphicsDeviceStateSnapshot Capture(GraphicsDevice graphicsDevice) {
            return new GraphicsDeviceStateSnapshot(graphicsDevice);
        }

        public bool EquivalentTo(GraphicsDeviceStateSnapshot other, out string diff) {
            StringBuilder builder = new StringBuilder();
            AppendDiff(builder, "RenderTargets", RenderTargetsEqual(renderTargets, other.renderTargets));
            AppendDiff(builder, "Viewport", viewport.Equals(other.viewport));
            AppendDiff(builder, "ScissorRectangle", scissorRectangle.Equals(other.scissorRectangle));
            AppendDiff(builder, "BlendState", ReferenceEquals(blendState, other.blendState));
            AppendDiff(builder, "DepthStencilState", ReferenceEquals(depthStencilState, other.depthStencilState));
            AppendDiff(builder, "RasterizerState", ReferenceEquals(rasterizerState, other.rasterizerState));
            AppendDiff(builder, "SamplerStates[0]", ReferenceEquals(sampler0, other.sampler0));
            AppendDiff(builder, "Textures[0]", ReferenceEquals(texture0, other.texture0));
            AppendDiff(builder, "Indices", ReferenceEquals(indices, other.indices));
            AppendDiff(builder, "VertexBuffers", VertexBuffersEqual(vertexBuffers, other.vertexBuffers));
            AppendDiff(builder, "BlendFactor", blendFactor.Equals(other.blendFactor));
            diff = builder.ToString().TrimEnd();
            return diff.Length == 0;
        }

        private static void AppendDiff(StringBuilder builder, string name, bool equal) {
            if (!equal) {
                builder.AppendLine(name);
            }
        }

        private static bool RenderTargetsEqual(RenderTargetBinding[] left, RenderTargetBinding[] right) {
            if (left.Length != right.Length) {
                return false;
            }

            for (int index = 0; index < left.Length; index++) {
                if (!EqualityComparer<RenderTargetBinding>.Default.Equals(left[index], right[index])) {
                    return false;
                }
            }

            return true;
        }

        private static bool VertexBuffersEqual(VertexBufferBinding[] left, VertexBufferBinding[] right) {
            if (left.Length != right.Length) {
                return false;
            }

            for (int index = 0; index < left.Length; index++) {
                if (!EqualityComparer<VertexBufferBinding>.Default.Equals(left[index], right[index])) {
                    return false;
                }
            }

            return true;
        }
    }
#endif

    private static void RegisterNativeResolver() {
        if (nativeResolverRegistered) {
            return;
        }

        try {
            NativeLibrary.SetDllImportResolver(typeof(ImGui).Assembly, ResolveCimguiLibrary);
            Logger.Log(LogLevel.Info, nameof(AkronImGuiRenderer), "Registered ImGui.NET cimgui resolver.");
        } catch (InvalidOperationException) {
            // Another mod or an earlier Akron load can own the resolver. In that
            // case the explicit preload still gives the default resolver a real
            // loaded cimgui handle before ImGui.NET invokes its first P/Invoke.
            Logger.Log(LogLevel.Warn, nameof(AkronImGuiRenderer), "ImGui.NET cimgui resolver was already registered; using explicit native preload fallback.");
        }

        nativeResolverRegistered = true;
        TryPreloadNativeLibrary();
    }

    private static IntPtr ResolveCimguiLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath) {
        if (!libraryName.Contains("cimgui", StringComparison.OrdinalIgnoreCase)) {
            return IntPtr.Zero;
        }

        if (nativeLibraryHandle != IntPtr.Zero) {
            return nativeLibraryHandle;
        }

        string baseDirectory = Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory;
        foreach (string candidate in EnumerateNativeCandidates(baseDirectory)) {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out IntPtr handle)) {
                nativeLibraryHandle = handle;
                Logger.Log(LogLevel.Info, nameof(AkronImGuiRenderer), "Resolved cimgui from " + candidate);
                return handle;
            }
        }

        string extractedPath = ExtractEmbeddedNativeLibrary();
        if (!string.IsNullOrWhiteSpace(extractedPath) && NativeLibrary.TryLoad(extractedPath, out IntPtr extractedHandle)) {
            nativeLibraryHandle = extractedHandle;
            Logger.Log(LogLevel.Info, nameof(AkronImGuiRenderer), "Resolved embedded cimgui from " + extractedPath);
            return extractedHandle;
        }

        Logger.Log(LogLevel.Error, nameof(AkronImGuiRenderer), "Failed to resolve cimgui for ImGui.NET.");
        return IntPtr.Zero;
    }

    private static void TryPreloadNativeLibrary() {
        if (nativeLibraryHandle != IntPtr.Zero) {
            return;
        }

        string baseDirectory = Path.GetDirectoryName(typeof(ImGui).Assembly.Location) ?? AppContext.BaseDirectory;
        foreach (string candidate in EnumerateNativeCandidates(baseDirectory)) {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out IntPtr handle)) {
                nativeLibraryHandle = handle;
                Logger.Log(LogLevel.Info, nameof(AkronImGuiRenderer), "Preloaded cimgui from " + candidate);
                return;
            }
        }

        foreach (string extractedPath in ExtractEmbeddedNativeLibraryCopies()) {
            if (File.Exists(extractedPath) && NativeLibrary.TryLoad(extractedPath, out IntPtr handle)) {
                nativeLibraryHandle = handle;
                Logger.Log(LogLevel.Info, nameof(AkronImGuiRenderer), "Preloaded embedded cimgui from " + extractedPath);
                return;
            }
        }

        Logger.Log(LogLevel.Error, nameof(AkronImGuiRenderer), "Unable to preload cimgui. ImGui overlay will stay disabled if native loading fails.");
    }

    private static IEnumerable<string> EnumerateNativeCandidates(string baseDirectory) {
        string runtimeId = GetRuntimeId();
        string nativeFileName = GetNativeFileName();
        yield return Path.Combine(baseDirectory, "runtimes", runtimeId, "native", nativeFileName);
        yield return Path.Combine(baseDirectory, nativeFileName);
    }

    private static string GetRuntimeId() {
        string os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "win"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "osx"
                : "linux";
        string architecture = RuntimeInformation.ProcessArchitecture switch {
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            _ => "x64"
        };

        return os == "osx" ? os : os + "-" + architecture;
    }

    private static string GetNativeFileName() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return "cimgui.dll";
        }

        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "libcimgui.dylib" : "libcimgui.so";
    }

    private static string ExtractEmbeddedNativeLibrary() {
        return ExtractEmbeddedNativeLibraryCopies().FirstOrDefault(File.Exists) ?? string.Empty;
    }

    private static IEnumerable<string> ExtractEmbeddedNativeLibraryCopies() {
        string runtimeId = GetRuntimeId();
        string nativeFileName = GetNativeFileName();
        Assembly assembly = typeof(AkronImGuiRenderer).Assembly;
        string resourceRuntimeId = runtimeId.Replace('-', '_');
        string resourceSuffix = ".Resources.Native." + resourceRuntimeId + "." + nativeFileName;
        string resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase));
        if (resourceName == null) {
            Logger.Log(LogLevel.Error, nameof(AkronImGuiRenderer), "Missing embedded native resource suffix " + resourceSuffix);
            yield break;
        }

        byte[] nativeBytes;
        using (Stream source = assembly.GetManifestResourceStream(resourceName)) {
            if (source == null) {
                Logger.Log(LogLevel.Error, nameof(AkronImGuiRenderer), "Unable to open embedded native resource " + resourceName);
                yield break;
            }

            using MemoryStream memory = new MemoryStream();
            source.CopyTo(memory);
            nativeBytes = memory.ToArray();
        }

        foreach (string directory in EnumerateNativeExtractionDirectories(runtimeId)) {
            string outputPath = Path.Combine(directory, nativeFileName);
            if (TryWriteNativeLibrary(outputPath, nativeBytes)) {
                yield return outputPath;
            }
        }
    }

    private static IEnumerable<string> EnumerateNativeExtractionDirectories(string runtimeId) {
        yield return Path.Combine(Everest.PathGame, "Saves", "AkronNative", runtimeId);
        yield return AppContext.BaseDirectory;
        yield return Everest.PathGame;

        string runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        if (!string.IsNullOrWhiteSpace(runtimeDirectory)) {
            yield return runtimeDirectory;
        }
    }

    private static bool TryWriteNativeLibrary(string outputPath, byte[] nativeBytes) {
        try {
            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory)) {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length != nativeBytes.Length) {
                File.WriteAllBytes(outputPath, nativeBytes);
            }

            return true;
        } catch (Exception exception) {
            Logger.Log(LogLevel.Warn, nameof(AkronImGuiRenderer), "Could not write cimgui native library to " + outputPath + ": " + exception.Message);
            return false;
        }
    }
}

internal static class AkronImGuiTheme {
    private static ulong cachedThemeFrame = ulong.MaxValue;
    private static AkronOverlayThemeDefinition cachedTheme;

    public static System.Numerics.Vector4 Background => ThemeColor(CurrentTheme().WindowColor, 0xFF);
    public static System.Numerics.Vector4 FrameBackground => ThemeColor(CurrentTheme().FrameColor, 0x24);
    public static System.Numerics.Vector4 Foreground => ThemeColor(CurrentTheme().TextColor, 0xFF);
    public static System.Numerics.Vector4 Muted => ThemeColor(CurrentTheme().MutedColor, 0xFF);
    public static System.Numerics.Vector4 DisabledText => ThemeColor(CurrentTheme().DisabledColor, 0xFF);
    public static System.Numerics.Vector4 Accent => ThemeColor(CurrentTheme().HeaderColor, 0xFF);
    public static System.Numerics.Vector4 AccentHovered => ThemeColor(CurrentTheme().HeaderHoverColor, 0xFF);
    public static System.Numerics.Vector4 ButtonHovered => ThemeColor(CurrentTheme().HeaderHoverColor, 0x52);
    public static System.Numerics.Vector4 ButtonActive => ThemeColor(CurrentTheme().HeaderHoverColor, 0x7A);
    public static readonly System.Numerics.Vector4 PopupOutline = Color(0xFF, 0xFF, 0xFF, 0x2E);
    public static readonly System.Numerics.Vector4 Transparent = Color(0x00, 0x00, 0x00, 0x00);

    public static uint ToU32(System.Numerics.Vector4 color) {
        return ImGui.ColorConvertFloat4ToU32(color);
    }

    private static System.Numerics.Vector4 Color(byte red, byte green, byte blue, byte alpha) {
        return new System.Numerics.Vector4(red / 255f, green / 255f, blue / 255f, alpha / 255f);
    }

    private static AkronOverlayThemeDefinition CurrentTheme() {
        if (cachedTheme == null || cachedThemeFrame != Engine.FrameCounter) {
            cachedThemeFrame = Engine.FrameCounter;
            cachedTheme = AkronOverlayThemes.CurrentDefinition();
        }

        return cachedTheme;
    }

    private static System.Numerics.Vector4 ThemeColor(int rgb, byte alpha) {
        int clamped = AkronModuleSettings.ClampRgb(rgb);
        return Color(
            (byte) ((clamped >> 16) & 0xFF),
            (byte) ((clamped >> 8) & 0xFF),
            (byte) (clamped & 0xFF),
            alpha);
    }
}

internal static class AkronImGuiDrawVertDeclaration {
    public static readonly VertexDeclaration Declaration;
    public static readonly int Size;

    static AkronImGuiDrawVertDeclaration() {
        unsafe {
            Size = sizeof(ImDrawVert);
        }

        Declaration = new VertexDeclaration(
            Size,
            new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
            new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
            new VertexElement(16, VertexElementFormat.Color, VertexElementUsage.Color, 0));
    }
}
