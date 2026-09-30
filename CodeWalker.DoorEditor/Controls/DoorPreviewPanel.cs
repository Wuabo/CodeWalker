using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CodeWalker.DoorEditor.Preview;
using CodeWalker.DoorEditor.Theme;
using CodeWalker.GameFiles;

namespace CodeWalker.DoorEditor.Controls
{
    public enum PreviewViewMode
    {
        TopXY,
        SideXZ,
        FrontYZ,
        Iso
    }

    /// <summary>
    /// Door preview: YDR mesh + AutoOpenVolumeOffset / TriggerBoxMinMax in door-local space.
    /// Iso = orbit camera (not classic isometric), auto-framed to mesh + gizmos.
    /// </summary>
    public sealed class DoorPreviewPanel : Panel
    {
        private DoorTuningParams? _tuning;
        private YdrMeshData? _mesh;
        private string _specialAttribute = "7";
        private PreviewViewMode _mode = PreviewViewMode.Iso;
        private float _yaw = 0.85f;
        private float _pitch = 0.45f;
        private float _userZoom = 1f;
        private Point _lastMouse;
        private bool _dragging;
        private readonly System.Windows.Forms.Timer _animTimer;
        private float _pulse;
        private float _motionAmount; // 0..1 how far open
        private float _motionSign = 1f; // +1 / -1 swing direction
        private float _bothFlip = 1f;   // alternates when StdDoorOpenBothDir
        private int _motionPhase; // 0 closed-hold, 1 opening, 2 open-hold, 3 closing
        private float _phaseHold;
        private bool _animateOpen = true;
        private bool _showPed = true;
        private string _statusText = "Drop a .ydr or use Load YDR…";

        /// <summary>Approximate standing ped height in metres (GTA door-local Z-up).</summary>
        public const float PedHeight = 1.80f;
        public const float PedHalfWidth = 0.22f;
        public const float PedHalfDepth = 0.14f;

        public PreviewViewMode ViewMode
        {
            get => _mode;
            set
            {
                _mode = value;
                if (_mode != PreviewViewMode.Iso)
                {
                    // orthographic presets — reset orbit so Top/Side/Front stay readable
                    _yaw = 0f;
                    _pitch = 0f;
                }
                else
                {
                    _yaw = 0.85f;
                    _pitch = 0.45f;
                }
                Invalidate();
            }
        }

        public bool AnimateOpen
        {
            get => _animateOpen;
            set { _animateOpen = value; Invalidate(); }
        }

        public bool ShowPed
        {
            get => _showPed;
            set { _showPed = value; Invalidate(); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value ?? ""; Invalidate(); }
        }

        public DoorPreviewPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(16, 18, 24);
            Cursor = Cursors.Hand;
            AllowDrop = true;

            _animTimer = new System.Windows.Forms.Timer { Interval = 40 };
            _animTimer.Tick += (_, _) =>
            {
                const float dt = 0.04f;
                _pulse = (_pulse + 0.05f) % (MathF.PI * 2f);
                // Motion preview for mechanical / hinged door types.
                if (_animateOpen && !_dragging && UsesMotionPreview(_specialAttribute))
                    TickMotion(dt);
                else
                    _motionAmount = 0f;
                Invalidate();
            };
            _animTimer.Start();

            MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left && _mode == PreviewViewMode.Iso)
                {
                    _dragging = true;
                    _lastMouse = e.Location;
                }
            };
            MouseUp += (_, _) => _dragging = false;
            MouseLeave += (_, _) => _dragging = false;
            MouseMove += (_, e) =>
            {
                if (!_dragging || _mode != PreviewViewMode.Iso) return;
                _yaw += (e.X - _lastMouse.X) * 0.012f;
                _pitch = Math.Clamp(_pitch + (e.Y - _lastMouse.Y) * 0.01f, 0.05f, 1.35f);
                _lastMouse = e.Location;
                Invalidate();
            };
            MouseWheel += (_, e) =>
            {
                // Zoom only via scroll — drag never changes distance.
                float factor = e.Delta > 0 ? 1.15f : 1f / 1.15f;
                _userZoom = Math.Clamp(_userZoom * factor, 0.2f, 8f);
                Invalidate();
            };
            MouseDoubleClick += (_, _) =>
            {
                _userZoom = 1f;
                if (_mode == PreviewViewMode.Iso)
                {
                    _yaw = 0.85f;
                    _pitch = 0.45f;
                }
                Invalidate();
            };

            DragEnter += (_, e) =>
            {
                if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                    e.Effect = DragDropEffects.Copy;
            };
            DragDrop += (_, e) =>
            {
                if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files) return;
                foreach (var f in files)
                {
                    if (f.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase))
                    {
                        TryLoadYdr(f);
                        break;
                    }
                }
            };
        }

        public void SetTuning(DoorTuningParams? tuning)
        {
            _tuning = tuning;
            // Keep phase; live edits to rate/torque/angle apply on next tick.
            Invalidate();
        }

        public void SetSpecialAttribute(string attr)
        {
            var next = string.IsNullOrWhiteSpace(attr) ? "7" : attr.Trim();
            if (next != _specialAttribute)
            {
                _specialAttribute = next;
                _motionPhase = 0;
                _phaseHold = 0f;
                _motionAmount = 0f;
            }
            if (!UsesMotionPreview(_specialAttribute))
                _motionAmount = 0f;
            if (_mesh != null && !string.IsNullOrEmpty(_mesh.Name))
            {
                var file = System.IO.Path.GetFileName(_mesh.Name);
                _statusText = $"{file}  ·  {_mesh.Triangles.Count} tris  ·  type {_specialAttribute}";
            }
            Invalidate();
        }

        public void ClearMesh()
        {
            _mesh = null;
            _statusText = "Drop a .ydr or use Load YDR…";
            Invalidate();
        }

        public bool TryLoadMesh(YdrMeshData mesh, string? specialAttribute = null)
        {
            _mesh = mesh;
            _userZoom = 1f;
            if (!string.IsNullOrWhiteSpace(specialAttribute))
                _specialAttribute = specialAttribute.Trim();
            else
                _specialAttribute = GuessSpecialAttribute(System.IO.Path.GetFileNameWithoutExtension(mesh.Name));
            if (!UsesMotionPreview(_specialAttribute))
                _motionAmount = 0f;
            _statusText = mesh.Triangles.Count == 0
                ? $"Loaded {mesh.Name} (no triangles)"
                : $"{mesh.Name}  ·  {mesh.Triangles.Count} tris  ·  type {_specialAttribute}";
            Invalidate();
            return true;
        }

        public bool TryLoadYdr(string path)
        {
            try
            {
                return TryLoadMesh(YdrMeshExtractor.LoadFromFile(path));
            }
            catch (Exception ex)
            {
                _statusText = "YDR load failed: " + ex.Message;
                Invalidate();
                return false;
            }
        }

        public bool TryLoadYdrBytes(byte[] bytes, string name)
        {
            try
            {
                return TryLoadMesh(YdrMeshExtractor.FromBytes(bytes, name));
            }
            catch (Exception ex)
            {
                _statusText = "YDR load failed: " + ex.Message;
                Invalidate();
                return false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _animTimer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            using (var brush = new LinearGradientBrush(ClientRectangle,
                       Color.FromArgb(20, 24, 32), Color.FromArgb(10, 12, 16), 90f))
                g.FillRectangle(brush, ClientRectangle);

            ComputeFrame(out float cx, out float cy, out float scale, out float midX, out float midY, out float midZ);

            DrawGroundGrid(g, cx, cy, scale, midX, midY, midZ);
            DrawAxes(g, cx, cy, scale, midX, midY, midZ);

            if (_mesh != null && _mesh.Triangles.Count > 0)
                DrawMesh(g, cx, cy, scale, midX, midY, midZ);

            if (_tuning != null)
            {
                DrawAutoOpenVolume(g, cx, cy, scale, midX, midY, midZ, _tuning);
                if (_tuning.CustomTriggerBox && HasTriggerBoxData(_tuning))
                    DrawTriggerBox(g, cx, cy, scale, midX, midY, midZ, _tuning);
            }

            if (_showPed)
                DrawReferencePed(g, cx, cy, scale, midX, midY, midZ);

            DrawLegend(g);
            DrawHud(g);
        }

        /// <summary>
        /// Frame camera around mesh + gizmos in door-local space (origin = hinge).
        /// Scale is based on world-space bounds only — orbit/drag never changes zoom.
        /// </summary>
        private void ComputeFrame(out float cx, out float cy, out float scale, out float midX, out float midY, out float midZ)
        {
            cx = Width * 0.5f;
            cy = Height * 0.52f;

            float minX = -0.5f, minY = -0.5f, minZ = 0f;
            float maxX = 0.5f, maxY = 0.5f, maxZ = 2.2f;
            bool any = false;

            void Expand(float x, float y, float z)
            {
                if (!any)
                {
                    minX = maxX = x;
                    minY = maxY = y;
                    minZ = maxZ = z;
                    any = true;
                    return;
                }
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
                if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
            }

            if (_mesh != null && _mesh.Triangles.Count > 0)
            {
                Expand(_mesh.MinX, _mesh.MinY, _mesh.MinZ);
                Expand(_mesh.MaxX, _mesh.MaxY, _mesh.MaxZ);
            }
            else
            {
                Expand(0, -0.5f, 0);
                Expand(0, 0.5f, 2.2f);
            }

            if (_tuning != null)
            {
                Expand(_tuning.AutoOpenVolumeOffsetX, _tuning.AutoOpenVolumeOffsetY, _tuning.AutoOpenVolumeOffsetZ);
                float r = Math.Max(0.35f, 0.9f * Math.Abs(_tuning.AutoOpenRadiusModifier <= 0 ? 1f : _tuning.AutoOpenRadiusModifier));
                Expand(_tuning.AutoOpenVolumeOffsetX - r, _tuning.AutoOpenVolumeOffsetY - r, _tuning.AutoOpenVolumeOffsetZ);
                Expand(_tuning.AutoOpenVolumeOffsetX + r, _tuning.AutoOpenVolumeOffsetY + r, _tuning.AutoOpenVolumeOffsetZ);

                if (_tuning.UseAutoOpenTriggerBox || _tuning.CustomTriggerBox)
                {
                    GetTriggerBounds(_tuning, out float tMinX, out float tMinY, out float tMinZ, out float tMaxX, out float tMaxY, out float tMaxZ);
                    Expand(tMinX, tMinY, tMinZ);
                    Expand(tMaxX, tMaxY, tMaxZ);
                }
            }

            if (_showPed)
            {
                GetPedPose(out float px, out float py, out float pz);
                Expand(px - PedHalfWidth, py - PedHalfDepth, pz);
                Expand(px + PedHalfWidth, py + PedHalfDepth, pz + PedHeight);
            }

            float pad = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ)) * 0.08f + 0.15f;
            minX -= pad; maxX += pad;
            minY -= pad; maxY += pad;
            minZ -= pad; maxZ += pad;

            midX = (minX + maxX) * 0.5f;
            midY = (minY + maxY) * 0.5f;
            midZ = (minZ + maxZ) * 0.5f;

            // Orbit-independent fit: use bounding-sphere radius so drag never zooms.
            float dx = maxX - minX, dy = maxY - minY, dz = maxZ - minZ;
            float radius = 0.5f * MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (radius < 0.01f) radius = 1f;
            float view = Math.Min(Width - 56, Height - 110);
            if (view < 80) view = 80;
            float fitScale = view * 0.78f / radius;
            scale = Math.Clamp(fitScale * _userZoom, 4f, 800f);
        }

        private static bool HasTriggerBoxData(DoorTuningParams t) =>
            Math.Abs(t.TriggerBoxMaxX - t.TriggerBoxMinX) > 0.001f
            || Math.Abs(t.TriggerBoxMaxY - t.TriggerBoxMinY) > 0.001f
            || Math.Abs(t.TriggerBoxMaxZ - t.TriggerBoxMinZ) > 0.001f;

        private static void GetTriggerBounds(DoorTuningParams t,
            out float minX, out float minY, out float minZ,
            out float maxX, out float maxY, out float maxZ)
        {
            minX = t.TriggerBoxMinX; minY = t.TriggerBoxMinY; minZ = t.TriggerBoxMinZ;
            maxX = t.TriggerBoxMaxX; maxY = t.TriggerBoxMaxY; maxZ = t.TriggerBoxMaxZ;
            if (!HasTriggerBoxData(t))
            {
                minX = -0.5f; minY = -0.5f; minZ = 0f;
                maxX = 0.5f; maxY = 0.5f; maxZ = 2.2f;
            }
        }

        private static void GetTriggerBoundsRaw(DoorTuningParams t,
            out float minX, out float minY, out float minZ,
            out float maxX, out float maxY, out float maxZ)
        {
            minX = t.TriggerBoxMinX; minY = t.TriggerBoxMinY; minZ = t.TriggerBoxMinZ;
            maxX = t.TriggerBoxMaxX; maxY = t.TriggerBoxMaxY; maxZ = t.TriggerBoxMaxZ;
        }

        /// <summary>Orbit camera: yaw around Z, pitch looking down. Screen: X right, Z up, Y depth.</summary>
        private void CameraProject(float x, float y, float z, out float sx, out float sy, out float depth)
        {
            if (_mode == PreviewViewMode.TopXY)
            {
                sx = x; sy = -y; depth = -z;
                return;
            }
            if (_mode == PreviewViewMode.SideXZ)
            {
                sx = x; sy = -z; depth = y;
                return;
            }
            if (_mode == PreviewViewMode.FrontYZ)
            {
                sx = y; sy = -z; depth = -x;
                return;
            }

            // Iso / Orbit
            float cy = MathF.Cos(_yaw), syaw = MathF.Sin(_yaw);
            float rx = x * cy - y * syaw;
            float ry = x * syaw + y * cy;
            float cp = MathF.Cos(_pitch), sp = MathF.Sin(_pitch);
            // pitch around X: (ry,z) -> screen
            float ry2 = ry * cp - z * sp;
            float rz2 = ry * sp + z * cp;
            sx = rx;
            sy = -rz2;
            depth = ry2;
        }

        private PointF Project(float x, float y, float z, float cx, float cy, float scale, float midX, float midY, float midZ)
        {
            CameraProject(x - midX, y - midY, z - midZ, out float sx, out float sy, out _);
            return new PointF(cx + sx * scale, cy + sy * scale);
        }

        private float Depth(float x, float y, float z, float midX, float midY, float midZ)
        {
            CameraProject(x - midX, y - midY, z - midZ, out _, out _, out float d);
            return d;
        }

        /// <summary>
        /// Door types whose open path is worth looping in the preview.
        /// </summary>
        public static bool UsesMotionPreview(string? attr) => attr switch
        {
            "5" => true,  // garage — tip up
            "7" => true,  // normal hinged (preview swing; in-game is physics/push)
            "8" => true,  // sliding horizontal
            "9" => true,  // barrier arm
            "10" => true, // sliding vertical
            "12" => true, // rail crossing
            _ => false
        };

        private float OpenAngleRad()
        {
            // Engine: non-zero RotationLimitAngle is degrees → radians (Door.cpp GetLimitAngle).
            float limitDeg = _tuning?.RotationLimitAngle ?? 0f;
            if (limitDeg <= 0f)
            {
                // Type 7 default ~90°; garage tip also uses ~π/2 when unset.
                return MathF.PI / 2f;
            }
            return Math.Clamp(limitDeg, 0f, 180f) * (MathF.PI / 180f);
        }

        private bool IsBothDir()
        {
            var dir = (_tuning?.StdDoorRotDir ?? "").ToLowerInvariant();
            return dir.Contains("both") || string.IsNullOrEmpty(dir);
        }

        private void UpdateMotionSign()
        {
            var dir = (_tuning?.StdDoorRotDir ?? "").ToLowerInvariant();
            if (dir.Contains("both") || string.IsNullOrEmpty(dir))
                _motionSign = _bothFlip;
            else if (dir.Contains("neg"))
                _motionSign = -1f;
            else
                _motionSign = 1f; // StdDoorOpenPosDir / unknown
        }

        private float OpenHoldSeconds()
        {
            // DelayDoorClosingForPlayer keeps the door open longer after a push / auto-open.
            if (_tuning?.Flags != null)
            {
                foreach (var f in _tuning.Flags)
                {
                    if (f.Contains("DelayDoorClosing", StringComparison.OrdinalIgnoreCase))
                        return 1.35f;
                }
            }
            return 0.55f;
        }

        /// <summary>
        /// Open → hold → close cycle driven by AutoOpenRate / taper / torque / latch / mass / rot dir.
        /// </summary>
        private void TickMotion(float dt)
        {
            UpdateMotionSign();

            float rate = _tuning?.AutoOpenRate ?? 0.5f;
            if (rate < 0.001f) rate = 0.001f;

            float torque = _tuning?.TorqueAngularVelocityLimit ?? 5f;
            // Engine clamps angular velocity with this; 0 freezes motion.
            if (torque <= 0f)
                return;

            bool taper = _tuning?.AutoOpenCloseRateTaper ?? false;
            bool latch = _tuning?.ShouldLatchShut ?? false;
            float mass = Math.Max(0.01f, _tuning?.MassMultiplier ?? 1f);
            // Mass does not change auto-open rate in engine; light preview influence only.
            float massScale = 1f / MathF.Sqrt(Math.Clamp(mass, 0.25f, 4f));

            float effectiveRate = rate * massScale;
            // Taper near full open (opening) and near shut (closing) — Door.cpp window 0.8→0.99
            if (taper)
            {
                float a = _motionAmount;
                if (a > 0.8f && a < 0.99f)
                {
                    float taperRatio = Math.Max((0.99f - a) / (0.99f - 0.8f), 0.01f);
                    effectiveRate *= taperRatio;
                }
                else if (a > 0.01f && a < 0.2f && _motionPhase == 3)
                {
                    float taperRatio = Math.Max(a / 0.2f, 0.01f);
                    effectiveRate *= taperRatio;
                }
            }

            // Torque 5 ≈ unrestricted relative to tuning rate; lower slows; higher allows faster catch-up.
            float torqueScale = Math.Clamp(torque / 5f, 0.05f, 2f);
            float step = effectiveRate * torqueScale * dt;

            switch (_motionPhase)
            {
                case 0: // closed hold
                    if (latch) _motionAmount = 0f;
                    _phaseHold += dt;
                    if (_phaseHold >= 0.4f)
                    {
                        if (IsBothDir())
                            _bothFlip = -_bothFlip;
                        UpdateMotionSign();
                        _motionPhase = 1;
                        _phaseHold = 0f;
                    }
                    break;
                case 1: // opening → 1
                    _motionAmount = Math.Min(1f, _motionAmount + step);
                    if (_motionAmount >= 0.999f)
                    {
                        _motionAmount = 1f;
                        _motionPhase = 2;
                        _phaseHold = 0f;
                    }
                    break;
                case 2: // open hold
                    _phaseHold += dt;
                    if (_phaseHold >= OpenHoldSeconds())
                    {
                        _motionPhase = 3;
                        _phaseHold = 0f;
                    }
                    break;
                default: // closing → 0
                    _motionAmount = Math.Max(0f, _motionAmount - step);
                    if (_motionAmount <= 0.001f)
                    {
                        _motionAmount = 0f;
                        _motionPhase = 0;
                        _phaseHold = 0f;
                    }
                    else if (latch && _motionAmount < 0.05f)
                    {
                        // Latch snap near shut
                        _motionAmount = 0f;
                        _motionPhase = 0;
                        _phaseHold = 0f;
                    }
                    break;
            }
        }

        private void GetMeshExtents(out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ)
        {
            if (_mesh != null && _mesh.Triangles.Count > 0)
            {
                minX = _mesh.MinX; minY = _mesh.MinY; minZ = _mesh.MinZ;
                maxX = _mesh.MaxX; maxY = _mesh.MaxY; maxZ = _mesh.MaxZ;
                return;
            }
            minX = 0; maxX = 0;
            minY = -0.5f; maxY = 0.5f;
            minZ = 0; maxZ = 2.2f;
        }

        /// <summary>
        /// Door motions in door-local space (GTA axes; preview maps to screen).
        /// Vertices stay GTA Z-up; convert ↔ Y-up for the preview apply step.
        /// GTA (x,y,z) → Three (x, z, -y).
        /// </summary>
        private void ApplyDoorMotion(ref float x, ref float y, ref float z)
        {
            float amount = _motionAmount;
            if (amount <= 0.0001f) return;

            GetMeshExtents(out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ);

            // Extents in Three Y-up
            float tMinX = minX, tMaxX = maxX;
            float tMinY = minZ, tMaxY = maxZ;
            float tMinZ = -maxY, tMaxZ = -minY;
            float sizeX = Math.Max(tMaxX - tMinX, 1e-4f);
            float sizeY = Math.Max(tMaxY - tMinY, 1e-4f);
            float sizeZ = Math.Max(tMaxZ - tMinZ, 1e-4f);

            // Vertex → Three
            float tx = x, ty = z, tz = -y;
            float sign = _motionSign;

            switch (_specialAttribute)
            {
                case "5": // Garage — bottom hinge, tip up
                {
                    float H = sizeY;
                    float t = amount * Math.Min(OpenAngleRad(), MathF.PI / 2f);
                    float lift = H * 0.58f * (1f - MathF.Cos(Math.Min(t, MathF.PI / 2f)));
                    float hx = (tMinX + tMaxX) * 0.5f;
                    float hy = tMinY;
                    float hz = (tMinZ + tMaxZ) * 0.5f;
                    bool alongX = sizeX >= sizeZ;
                    float theta = sign * (alongX ? -t : t);
                    float c = MathF.Cos(theta), s = MathF.Sin(theta);
                    float cy = hy + lift;
                    if (alongX)
                    {
                        float posY = cy - (hy * c - hz * s);
                        float posZ = hz - (hy * s + hz * c);
                        float ty1 = ty * c - tz * s;
                        float tz1 = ty * s + tz * c;
                        ty = ty1 + posY;
                        tz = tz1 + posZ;
                    }
                    else
                    {
                        float posX = hx - (hx * c - hy * s);
                        float posY = cy - (hx * s + hy * c);
                        float tx1 = tx * c - ty * s;
                        float ty1 = tx * s + ty * c;
                        tx = tx1 + posX;
                        ty = ty1 + posY;
                    }
                    break;
                }
                case "8": // Sliding — along Three X (= GTA X)
                {
                    float travel = SlideTowardOrigin(tMinX, tMaxX);
                    tx += sign * amount * travel;
                    break;
                }
                case "10": // Vertical slide — along Three Y (= GTA Z)
                {
                    float pos = Math.Max(0f, tMaxY);
                    float neg = Math.Max(0f, -tMinY);
                    float travel = pos > 1e-4f ? pos : (neg > 1e-4f ? neg : sizeY);
                    ty += amount * travel;
                    break;
                }
                case "9": // Barrier
                case "12": // Rail crossing
                {
                    // Rz in Y-up preview space (maps from GTA local)
                    float ang = -sign * amount * OpenAngleRad();
                    float c = MathF.Cos(ang), s = MathF.Sin(ang);
                    float tx1 = tx * c - ty * s;
                    float ty1 = tx * s + ty * c;
                    tx = tx1;
                    ty = ty1;
                    break;
                }
                case "7": // Normal hinged — swing around GTA Z (Three Y)
                {
                    float ang = -sign * amount * OpenAngleRad();
                    float c = MathF.Cos(ang), s = MathF.Sin(ang);
                    // Rotate in GTA XY → Three (tx, tz) with tz = -y
                    float tx1 = tx * c - tz * s;
                    float tz1 = tx * s + tz * c;
                    tx = tx1;
                    tz = tz1;
                    break;
                }
                default:
                    return;
            }

            // Three → GTA
            x = tx;
            y = -tz;
            z = ty;
        }

        private static float SlideTowardOrigin(float min, float max)
        {
            float mid = (min + max) * 0.5f;
            if (mid >= 0)
            {
                float pos = Math.Max(0f, max);
                return pos > 1e-4f ? -pos : Math.Max(0f, -min);
            }
            float neg = Math.Max(0f, -min);
            return neg > 1e-4f ? neg : -Math.Max(0f, max);
        }

        /// <summary>Guess door type from a model / file name when YTYP attr is unknown.</summary>
        public static string GuessSpecialAttribute(string? name)
        {
            var n = (name ?? "").ToLowerInvariant();
            // Avoid matching "gar" inside "storage"
            if (n.Contains("garage") || n.Contains("gar_") || n.Contains("_gar")
                || n.Contains("shutter") || n.Contains("roll") || n.Contains("g_door") || n.Contains("gdoor"))
                return "5";
            if (n.Contains("slide") && (n.Contains("vert") || n.Contains("up") || n.Contains("down")))
                return "10";
            if (n.Contains("slide") || n.Contains("sliding"))
                return "8";
            if (n.Contains("rail") || n.Contains("crossing"))
                return "12";
            if (n.Contains("barrier") || n.Contains("gate_arm") || n.Contains("boom"))
                return "9";
            return "7";
        }

        public string SpecialAttribute => _specialAttribute;

        private void DrawMesh(Graphics g, float cx, float cy, float scale, float midX, float midY, float midZ)
        {
            var tris = _mesh!.Triangles;
            var drawn = new List<(float depth, PointF[] pts, bool front)>(Math.Min(tris.Count, 8000));
            int step = tris.Count > 5000 ? Math.Max(1, tris.Count / 5000) : 1;

            for (int i = 0; i < tris.Count; i += step)
            {
                var t = tris[i];
                float ax = t.Ax, ay = t.Ay, az = t.Az;
                float bx = t.Bx, by = t.By, bz = t.Bz;
                float cx3 = t.Cx, cy3 = t.Cy, cz3 = t.Cz;
                ApplyDoorMotion(ref ax, ref ay, ref az);
                ApplyDoorMotion(ref bx, ref by, ref bz);
                ApplyDoorMotion(ref cx3, ref cy3, ref cz3);

                var p0 = Project(ax, ay, az, cx, cy, scale, midX, midY, midZ);
                var p1 = Project(bx, by, bz, cx, cy, scale, midX, midY, midZ);
                var p2 = Project(cx3, cy3, cz3, cx, cy, scale, midX, midY, midZ);

                // screen-space facing
                float cross = (p1.X - p0.X) * (p2.Y - p0.Y) - (p1.Y - p0.Y) * (p2.X - p0.X);
                float depth = Depth(ax, ay, az, midX, midY, midZ)
                              + Depth(bx, by, bz, midX, midY, midZ)
                              + Depth(cx3, cy3, cz3, midX, midY, midZ);
                drawn.Add((depth, new[] { p0, p1, p2 }, cross < 0));
            }

            drawn.Sort((a, b) => b.depth.CompareTo(a.depth));
            using var frontFill = new SolidBrush(Color.FromArgb(210, 110, 130, 175));
            using var backFill = new SolidBrush(Color.FromArgb(90, 60, 70, 95));
            using var edge = new Pen(Color.FromArgb(220, 200, 210, 230), 0.9f);
            foreach (var (_, pts, front) in drawn)
            {
                g.FillPolygon(front ? frontFill : backFill, pts);
                if (front) g.DrawPolygon(edge, pts);
            }

            // hinge / origin marker — for garage show bottom hinge, else local origin
            float hx = 0, hy = 0, hz = 0;
            if (_specialAttribute == "5" && _mesh != null)
            {
                hx = (_mesh.MinX + _mesh.MaxX) * 0.5f;
                hy = (_mesh.MinY + _mesh.MaxY) * 0.5f;
                hz = _mesh.MinZ;
            }
            var hinge = Project(hx, hy, hz, cx, cy, scale, midX, midY, midZ);
            using var hingeBrush = new SolidBrush(AppTheme.Warning);
            g.FillEllipse(hingeBrush, hinge.X - 4, hinge.Y - 4, 8, 8);
            using var hingeText = new SolidBrush(AppTheme.Warning);
            g.DrawString(_specialAttribute == "5" ? "garage hinge" : "hinge", AppTheme.SmallFont, hingeText, hinge.X + 6, hinge.Y - 8);
        }

        private void DrawDoorPlaceholder(Graphics g, float cx, float cy, float scale, float midX, float midY, float midZ)
        {
            float hy = 0.5f, hz = 1.1f;
            float x0 = 0, y0 = -hy, z0 = 0; ApplyDoorMotion(ref x0, ref y0, ref z0);
            float ax = 0, ay = hy, az = 0; ApplyDoorMotion(ref ax, ref ay, ref az);
            float bx = 0, by = hy, bz = hz * 2; ApplyDoorMotion(ref bx, ref by, ref bz);
            float cx2 = 0, cy2 = -hy, cz2 = hz * 2; ApplyDoorMotion(ref cx2, ref cy2, ref cz2);

            var corners = new[]
            {
                Project(x0, y0, z0, cx, cy, scale, midX, midY, midZ),
                Project(ax, ay, az, cx, cy, scale, midX, midY, midZ),
                Project(bx, by, bz, cx, cy, scale, midX, midY, midZ),
                Project(cx2, cy2, cz2, cx, cy, scale, midX, midY, midZ),
            };
            using var fill = new SolidBrush(Color.FromArgb(180, AppTheme.DoorFill));
            using var edge = new Pen(AppTheme.DoorEdge, 1.8f);
            g.FillPolygon(fill, corners);
            g.DrawPolygon(edge, corners);

            var hinge = Project(0, 0, 0, cx, cy, scale, midX, midY, midZ);
            using var hingeBrush = new SolidBrush(AppTheme.Warning);
            g.FillEllipse(hingeBrush, hinge.X - 4, hinge.Y - 4, 8, 8);
        }

        private void DrawGroundGrid(Graphics g, float cx, float cy, float scale, float midX, float midY, float midZ)
        {
            using var pen = new Pen(Color.FromArgb(36, AppTheme.Line), 1f);
            using var axisPen = new Pen(Color.FromArgb(70, AppTheme.Line), 1.2f);
            const int n = 6;
            for (int i = -n; i <= n; i++)
            {
                var p = (i == 0) ? axisPen : pen;
                g.DrawLine(p,
                    Project(i, -n, 0, cx, cy, scale, midX, midY, midZ),
                    Project(i, n, 0, cx, cy, scale, midX, midY, midZ));
                g.DrawLine(p,
                    Project(-n, i, 0, cx, cy, scale, midX, midY, midZ),
                    Project(n, i, 0, cx, cy, scale, midX, midY, midZ));
            }
        }

        private void DrawAxes(Graphics g, float cx, float cy, float scale, float midX, float midY, float midZ)
        {
            var o = Project(0, 0, 0, cx, cy, scale, midX, midY, midZ);
            float len = 1.2f;
            DrawAxis(g, o, Project(len, 0, 0, cx, cy, scale, midX, midY, midZ), Color.FromArgb(220, 90, 90), "X");
            DrawAxis(g, o, Project(0, len, 0, cx, cy, scale, midX, midY, midZ), Color.FromArgb(90, 200, 110), "Y");
            DrawAxis(g, o, Project(0, 0, len, cx, cy, scale, midX, midY, midZ), Color.FromArgb(90, 140, 240), "Z");
        }

        private static void DrawAxis(Graphics g, PointF o, PointF tip, Color color, string label)
        {
            using var pen = new Pen(color, 2f);
            g.DrawLine(pen, o, tip);
            using var brush = new SolidBrush(color);
            g.DrawString(label, AppTheme.SmallFont, brush, tip.X + 3, tip.Y - 10);
        }

        private void DrawAutoOpenVolume(Graphics g, float cx, float cy, float scale, float midX, float midY, float midZ, DoorTuningParams t)
        {
            var oxf = t.AutoOpenVolumeOffsetX;
            var oyf = t.AutoOpenVolumeOffsetY;
            var ozf = t.AutoOpenVolumeOffsetZ;

            var origin = Project(0, 0, 0, cx, cy, scale, midX, midY, midZ);
            var offsetPt = Project(oxf, oyf, ozf, cx, cy, scale, midX, midY, midZ);

            using (var dash = new Pen(Color.FromArgb(200, AppTheme.OffsetGizmo), 1.6f) { DashStyle = DashStyle.Dash })
                g.DrawLine(dash, origin, offsetPt);

            float pulse = 4f + MathF.Sin(_pulse) * 1.5f;
            using (var glow = new SolidBrush(Color.FromArgb(50, AppTheme.OffsetGizmo)))
                g.FillEllipse(glow, offsetPt.X - pulse * 2, offsetPt.Y - pulse * 2, pulse * 4, pulse * 4);
            using (var core = new SolidBrush(AppTheme.OffsetGizmo))
                g.FillEllipse(core, offsetPt.X - 5, offsetPt.Y - 5, 10, 10);

            float radius = Math.Max(0.35f, 0.9f * Math.Abs(t.AutoOpenRadiusModifier <= 0 ? 1f : t.AutoOpenRadiusModifier));
            // When using a custom trigger box as the auto-open volume, dim the sphere.
            int sphereAlpha = t.UseAutoOpenTriggerBox && t.CustomTriggerBox && HasTriggerBoxData(t) ? 70 : 160;
            DrawCircleXY(g, oxf, oyf, ozf, radius, cx, cy, scale, midX, midY, midZ, Color.FromArgb(sphereAlpha, AppTheme.OffsetGizmo));

            // Cosine cone: threshold = cos(half-angle from forward). -1 = full circle (no wedge).
            float cosTh = t.AutoOpenCosineAngleBetweenThreshold;
            if (cosTh > -0.999f)
            {
                float half = MathF.Acos(Math.Clamp(cosTh, -1f, 1f));
                DrawCosineWedge(g, oxf, oyf, ozf, radius, half, cx, cy, scale, midX, midY, midZ);
            }

            using var labelBrush = new SolidBrush(AppTheme.OffsetGizmo);
            using var bg = new SolidBrush(Color.FromArgb(180, 12, 14, 20));
            string line1 = "Offset";
            string line2 = $"({oxf:0.##}, {oyf:0.##}, {ozf:0.##})";
            string line3 = $"r×{t.AutoOpenRadiusModifier:0.##}  cos {cosTh:0.##}";
            var sz = g.MeasureString(line3, AppTheme.MonoFont);
            g.FillRectangle(bg, offsetPt.X + 8, offsetPt.Y - 16, Math.Max(sz.Width, 48) + 8, 40);
            g.DrawString(line1, AppTheme.SmallFont, labelBrush, offsetPt.X + 10, offsetPt.Y - 14);
            g.DrawString(line2, AppTheme.MonoFont, labelBrush, offsetPt.X + 10, offsetPt.Y);
            g.DrawString(line3, AppTheme.MonoFont, labelBrush, offsetPt.X + 10, offsetPt.Y + 12);
        }

        /// <summary>
        /// Vehicle approach cone in door XY (forward = +Y), half-angle from AutoOpenCosineAngleBetweenThreshold.
        /// </summary>
        private void DrawCosineWedge(Graphics g, float ox, float oy, float oz, float radius, float halfAngle,
            float cx, float cy, float scale, float midX, float midY, float midZ)
        {
            // Forward +Y; wedge from π/2−half to π/2+half in XY polar (0 = +X).
            float mid = MathF.PI / 2f;
            const int segs = 24;
            var pts = new PointF[segs + 2];
            pts[0] = Project(ox, oy, oz, cx, cy, scale, midX, midY, midZ);
            for (int i = 0; i <= segs; i++)
            {
                float a = mid - halfAngle + (2f * halfAngle) * (i / (float)segs);
                pts[i + 1] = Project(
                    ox + MathF.Cos(a) * radius,
                    oy + MathF.Sin(a) * radius,
                    oz, cx, cy, scale, midX, midY, midZ);
            }
            using var fill = new SolidBrush(Color.FromArgb(36, AppTheme.OffsetGizmo));
            using var pen = new Pen(Color.FromArgb(200, AppTheme.OffsetGizmo), 1.4f);
            g.FillPolygon(fill, pts);
            g.DrawPolygon(pen, pts);
        }

        private void DrawTriggerBox(Graphics g, float cx, float cy, float scale, float midX, float midY, float midZ, DoorTuningParams t)
        {
            GetTriggerBoundsRaw(t, out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ);

            // corner order: bit0=X, bit1=Y, bit2=Z
            var corners = new PointF[8];
            for (int i = 0; i < 8; i++)
            {
                float x = (i & 1) == 0 ? minX : maxX;
                float y = (i & 2) == 0 ? minY : maxY;
                float z = (i & 4) == 0 ? minZ : maxZ;
                corners[i] = Project(x, y, z, cx, cy, scale, midX, midY, midZ);
            }

            using var pen = new Pen(Color.FromArgb(210, AppTheme.TriggerGizmo), 1.7f);
            // edges along X
            DrawEdge(g, pen, corners, 0, 1); DrawEdge(g, pen, corners, 2, 3);
            DrawEdge(g, pen, corners, 4, 5); DrawEdge(g, pen, corners, 6, 7);
            // edges along Y
            DrawEdge(g, pen, corners, 0, 2); DrawEdge(g, pen, corners, 1, 3);
            DrawEdge(g, pen, corners, 4, 6); DrawEdge(g, pen, corners, 5, 7);
            // edges along Z
            DrawEdge(g, pen, corners, 0, 4); DrawEdge(g, pen, corners, 1, 5);
            DrawEdge(g, pen, corners, 2, 6); DrawEdge(g, pen, corners, 3, 7);

            // subtle face fill on bottom only
            using var fill = new SolidBrush(Color.FromArgb(22, AppTheme.TriggerGizmo));
            g.FillPolygon(fill, new[] { corners[0], corners[1], corners[3], corners[2] });

            var labelAt = corners[7];
            using var brush = new SolidBrush(AppTheme.TriggerGizmo);
            using var bg = new SolidBrush(Color.FromArgb(180, 12, 14, 20));
            string line1 = "TriggerBox";
            string line2 = $"min({minX:0.#},{minY:0.#},{minZ:0.#})";
            string line3 = $"max({maxX:0.#},{maxY:0.#},{maxZ:0.#})";
            g.FillRectangle(bg, labelAt.X + 6, labelAt.Y - 14, 130, 40);
            g.DrawString(line1, AppTheme.SmallFont, brush, labelAt.X + 8, labelAt.Y - 12);
            g.DrawString(line2, AppTheme.MonoFont, brush, labelAt.X + 8, labelAt.Y + 2);
            g.DrawString(line3, AppTheme.MonoFont, brush, labelAt.X + 8, labelAt.Y + 14);
        }

        /// <summary>
        /// Stand a ~1.8 m ped in front of the door (or at the near face of the custom trigger box)
        /// so TriggerBox Z height can be judged against a real player.
        /// </summary>
        private void GetPedPose(out float x, out float y, out float z)
        {
            x = 0f;
            y = -1.2f;
            z = 0f; // feet on door-local ground

            if (_tuning != null && _tuning.CustomTriggerBox && HasTriggerBoxData(_tuning))
            {
                GetTriggerBoundsRaw(_tuning,
                    out float minX, out float minY, out float minZ,
                    out float maxX, out float maxY, out float maxZ);
                x = (minX + maxX) * 0.5f;
                // Prefer the side closer to -Y (typical approach); fall back to box centre.
                float nearY = Math.Abs(minY) <= Math.Abs(maxY) ? minY : maxY;
                // Stand slightly inside the box along Y so "inside trigger" is obvious.
                float midY = (minY + maxY) * 0.5f;
                y = nearY + Math.Sign(midY - nearY + 0.0001f) * Math.Min(0.35f, Math.Abs(maxY - minY) * 0.25f);
                // Feet at max(0, box floor) so ped sits on ground / box bottom.
                z = Math.Max(0f, Math.Min(minZ, maxZ));
            }
        }

        private void DrawReferencePed(Graphics g, float cx, float cy, float scale, float midX, float midY, float midZ)
        {
            GetPedPose(out float px, out float py, out float pz);
            float headR = 0.12f;
            float shoulderZ = pz + 1.45f;
            float hipZ = pz + 0.95f;
            float headZ = pz + PedHeight - headR;
            float footZ = pz;

            // Capsule body (torso box in local space)
            float hw = PedHalfWidth;
            float hd = PedHalfDepth;
            var torso = new[]
            {
                Project(px - hw, py - hd, hipZ, cx, cy, scale, midX, midY, midZ),
                Project(px + hw, py - hd, hipZ, cx, cy, scale, midX, midY, midZ),
                Project(px + hw, py + hd, hipZ, cx, cy, scale, midX, midY, midZ),
                Project(px - hw, py + hd, hipZ, cx, cy, scale, midX, midY, midZ),
                Project(px - hw, py - hd, shoulderZ, cx, cy, scale, midX, midY, midZ),
                Project(px + hw, py - hd, shoulderZ, cx, cy, scale, midX, midY, midZ),
                Project(px + hw, py + hd, shoulderZ, cx, cy, scale, midX, midY, midZ),
                Project(px - hw, py + hd, shoulderZ, cx, cy, scale, midX, midY, midZ),
            };

            using var fill = new SolidBrush(Color.FromArgb(55, AppTheme.PedGizmo));
            using var pen = new Pen(Color.FromArgb(220, AppTheme.PedGizmo), 1.6f);
            // bottom + top + sides (simple)
            g.FillPolygon(fill, new[] { torso[0], torso[1], torso[5], torso[4] });
            g.FillPolygon(fill, new[] { torso[4], torso[5], torso[6], torso[7] });
            DrawEdge(g, pen, torso, 0, 1); DrawEdge(g, pen, torso, 1, 2);
            DrawEdge(g, pen, torso, 2, 3); DrawEdge(g, pen, torso, 3, 0);
            DrawEdge(g, pen, torso, 4, 5); DrawEdge(g, pen, torso, 5, 6);
            DrawEdge(g, pen, torso, 6, 7); DrawEdge(g, pen, torso, 7, 4);
            DrawEdge(g, pen, torso, 0, 4); DrawEdge(g, pen, torso, 1, 5);
            DrawEdge(g, pen, torso, 2, 6); DrawEdge(g, pen, torso, 3, 7);

            // Head
            var head = Project(px, py, headZ, cx, cy, scale, midX, midY, midZ);
            float headPx = Math.Max(3f, headR * scale);
            using (var headFill = new SolidBrush(Color.FromArgb(90, AppTheme.PedGizmo)))
                g.FillEllipse(headFill, head.X - headPx, head.Y - headPx, headPx * 2, headPx * 2);
            g.DrawEllipse(pen, head.X - headPx, head.Y - headPx, headPx * 2, headPx * 2);

            // Legs
            var hipL = Project(px - hw * 0.45f, py, hipZ, cx, cy, scale, midX, midY, midZ);
            var hipR = Project(px + hw * 0.45f, py, hipZ, cx, cy, scale, midX, midY, midZ);
            var footL = Project(px - hw * 0.45f, py, footZ, cx, cy, scale, midX, midY, midZ);
            var footR = Project(px + hw * 0.45f, py, footZ, cx, cy, scale, midX, midY, midZ);
            g.DrawLine(pen, hipL, footL);
            g.DrawLine(pen, hipR, footR);

            // Height ruler (feet → head) so Z max of trigger is obvious
            var feet = Project(px + hw + 0.15f, py, footZ, cx, cy, scale, midX, midY, midZ);
            var top = Project(px + hw + 0.15f, py, footZ + PedHeight, cx, cy, scale, midX, midY, midZ);
            using (var ruler = new Pen(Color.FromArgb(180, AppTheme.PedGizmo), 1f) { DashStyle = DashStyle.Dot })
                g.DrawLine(ruler, feet, top);

            using var labelBrush = new SolidBrush(AppTheme.PedGizmo);
            using var bg = new SolidBrush(Color.FromArgb(180, 12, 14, 20));
            string label = $"Ped {PedHeight:0.0}m";
            bool insideBox = IsPedInsideTrigger(px, py, pz);
            bool insideVol = IsPedInsideAutoOpenVolume(px, py, pz);
            if (_tuning != null && _tuning.CustomTriggerBox && HasTriggerBoxData(_tuning))
                label += insideBox ? " · IN box" : " · OUT box";
            else if (_tuning != null)
                label += insideVol ? " · IN vol" : " · OUT vol";
            var sz = g.MeasureString(label, AppTheme.SmallFont);
            g.FillRectangle(bg, head.X + 10, head.Y - 18, sz.Width + 8, 16);
            g.DrawString(label, AppTheme.SmallFont, labelBrush, head.X + 12, head.Y - 16);
        }

        private bool IsPedInsideAutoOpenVolume(float px, float py, float pz)
        {
            if (_tuning == null) return false;
            float ox = _tuning.AutoOpenVolumeOffsetX;
            float oy = _tuning.AutoOpenVolumeOffsetY;
            float oz = _tuning.AutoOpenVolumeOffsetZ;
            float r = Math.Max(0.35f, 0.9f * Math.Abs(_tuning.AutoOpenRadiusModifier <= 0 ? 1f : _tuning.AutoOpenRadiusModifier));
            float dx = px - ox, dy = py - oy, dz = (pz + PedHeight * 0.5f) - oz;
            return dx * dx + dy * dy + dz * dz <= r * r;
        }

        private bool IsPedInsideTrigger(float px, float py, float pz)
        {
            if (_tuning == null || !_tuning.CustomTriggerBox || !HasTriggerBoxData(_tuning))
                return false;
            GetTriggerBoundsRaw(_tuning,
                out float minX, out float minY, out float minZ,
                out float maxX, out float maxY, out float maxZ);
            // Ped occupies feet..head — inside if any vertical overlap and XY inside.
            float pedTop = pz + PedHeight;
            bool xy = px >= minX && px <= maxX && py >= minY && py <= maxY;
            bool zOverlap = pedTop >= minZ && pz <= maxZ;
            return xy && zOverlap;
        }

        private static void DrawEdge(Graphics g, Pen pen, PointF[] c, int a, int b) =>
            g.DrawLine(pen, c[a], c[b]);

        private void DrawCircleXY(Graphics g, float ox, float oy, float oz, float radius, float cx, float cy, float scale, float midX, float midY, float midZ, Color color)
        {
            const int segs = 64;
            var pts = new PointF[segs];
            for (int i = 0; i < segs; i++)
            {
                var a = i / (float)segs * MathF.PI * 2f;
                pts[i] = Project(ox + MathF.Cos(a) * radius, oy + MathF.Sin(a) * radius, oz, cx, cy, scale, midX, midY, midZ);
            }
            using var pen = new Pen(color, 1.5f);
            g.DrawPolygon(pen, pts);
        }

        private void DrawLegend(Graphics g)
        {
            using var back = new SolidBrush(Color.FromArgb(170, 14, 16, 22));
            g.FillRectangle(back, 8, Height - 94, 250, 86);
            int x = 12, y = Height - 86;
            DrawLegendItem(g, x, y, AppTheme.DoorEdge, _mesh != null ? "YDR mesh (hinge = yellow)" : "Door placeholder");
            DrawLegendItem(g, x, y + 16, AppTheme.OffsetGizmo, "AutoOpen offset / radius / cosine");
            DrawLegendItem(g, x, y + 32, AppTheme.TriggerGizmo, "TriggerBoxMinMax");
            DrawLegendItem(g, x, y + 48, AppTheme.PedGizmo, "Reference ped 1.8 m (IN/OUT)");
            DrawLegendItem(g, x, y + 64, AppTheme.Faint, "Edits live-drive rate·angle·torque·latch");
        }

        private static void DrawLegendItem(Graphics g, int x, int y, Color color, string text)
        {
            using var brush = new SolidBrush(color);
            g.FillRectangle(brush, x, y + 3, 10, 10);
            using var textBrush = new SolidBrush(AppTheme.Faint);
            g.DrawString(text, AppTheme.SmallFont, textBrush, x + 16, y);
        }

        private void DrawHud(Graphics g)
        {
            var mode = _mode switch
            {
                PreviewViewMode.TopXY => "View: Top (X/Y)",
                PreviewViewMode.SideXZ => "View: Side (X/Z)",
                PreviewViewMode.FrontYZ => "View: Front (Y/Z)",
                _ => "View: Orbit  ·  drag rotate · scroll zoom"
            };
            using var brush = new SolidBrush(AppTheme.Faint);
            g.DrawString(mode, AppTheme.SmallFont, brush, Width - 160, 10);
            g.DrawString(_statusText, AppTheme.SmallFont, brush, 10, 10);

            if (_tuning == null || !UsesMotionPreview(_specialAttribute))
                return;

            float limit = _tuning.RotationLimitAngle;
            string ang = limit > 0f ? $"{limit:0.#}°" : "90° def";
            string phase = _motionPhase switch
            {
                0 => "closed",
                1 => "opening",
                2 => "open",
                _ => "closing"
            };
            string line =
                $"anim {phase} {_motionAmount:0%}  ·  rate {_tuning.AutoOpenRate:0.###}  ·  torque {_tuning.TorqueAngularVelocityLimit:0.##}  ·  {ang}  ·  mass×{_tuning.MassMultiplier:0.##}";
            if (_tuning.AutoOpenCloseRateTaper) line += "  ·  taper";
            if (_tuning.ShouldLatchShut) line += "  ·  latch";
            if (_tuning.BreakableByVehicle) line += $"  ·  break@{_tuning.BreakingImpulse:0.#}";
            g.DrawString(line, AppTheme.MonoFont, brush, 10, 26);
        }
    }
}
