using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace FishingKing
{
    /// <summary>
    /// Unified mouse / touch pointer (primary touch) built on the Input System.
    /// Also tells whether a press started on top of the UI so world gestures can ignore it.
    /// </summary>
    public static class PointerInput
    {
        static int frame = -1;
        static bool down, pressed, released, startedOverUI;
        static Vector2 pos;
        static readonly List<RaycastResult> hits = new List<RaycastResult>();

        /// <summary>Simulated pointer used by the test autopilot (-fkauto).</summary>
        public static bool SimActive, SimDown;
        public static Vector2 SimPos;
        /// <summary>
        /// &gt; 0: the simulated pointer is a touchscreen of this many dots per inch (its speeds are measured in cm/s like
        /// a finger's); 0: it moves like a mouse (measured in window heights).
        /// </summary>
        public static float SimDpi;

        /// <summary>What the current (or last) press was made with: flick speeds are measured differently.</summary>
        public enum Kind { Mouse, Touch }
        static Kind kind;

        /// <summary>One pointer position while it was down, with the frame's unscaled time.</summary>
        public struct Sample
        {
            public Vector2 pos;
            public float time;
        }

        // the positions of the current (or last) press, one per frame while down plus the lift position on the release
        // frame (real and simulated input alike); cleared on the next press, the oldest dropped after a few seconds
        static readonly List<Sample> samples = new List<Sample>(256);
        const int MaxSamples = 240;

        static void Poll()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            pressed = released = false;
            bool nowDown = false;
            Vector2 p = pos;
            Kind k = kind;

            var ts = Touchscreen.current;
            if (SimActive)
            {
                nowDown = SimDown;
                p = SimPos;
                k = SimDpi > 0f ? Kind.Touch : Kind.Mouse;
            }
            else if (ts != null && (ts.primaryTouch.press.isPressed || ts.primaryTouch.press.wasReleasedThisFrame))
            {
                nowDown = ts.primaryTouch.press.isPressed;
                p = ts.primaryTouch.position.ReadValue();
                k = Kind.Touch;
            }
            else if (Mouse.current != null)
            {
                nowDown = Mouse.current.leftButton.isPressed;
                p = Mouse.current.position.ReadValue();
                k = Kind.Mouse;
            }
            else if (Pointer.current != null)
            {
                nowDown = Pointer.current.press.isPressed;
                p = Pointer.current.position.ReadValue();
                k = Pointer.current is Pen ? Kind.Touch : Kind.Mouse;
            }

            pressed = nowDown && !down;
            released = !nowDown && down;
            down = nowDown;
            pos = p;
            if (pressed)
            {
                startedOverUI = IsOverUI(pos);
                kind = k;
            }
            if (nowDown)
            {
                if (pressed) samples.Clear();
                AddSample(p);
            }
            else if (released && (samples.Count == 0 || p.y >= samples[samples.Count - 1].pos.y))
            {
                // where it lifted: a flick usually lifts while still moving, so the last stretch is often its fastest part
                // and its true direction at the release. A lift-off wobble below the last position is left out (it must not
                // read as the finger coming back down and restart the stroke).
                AddSample(p);
            }
        }

        static void AddSample(Vector2 p)
        {
            if (samples.Count >= MaxSamples) samples.RemoveRange(0, MaxSamples / 4);
            samples.Add(new Sample { pos = p, time = Time.unscaledTime });
        }

        public static bool IsDown { get { Poll(); return down; } }
        public static bool Pressed { get { Poll(); return pressed; } }
        public static bool Released { get { Poll(); return released; } }
        public static Vector2 Position { get { Poll(); return pos; } }

        /// <summary>
        /// The current (or last) press's positions, oldest first, one per frame while it was down, then (once let go) the
        /// position it lifted at, unless that dipped below the last one. Read by the flick cast (<see cref="FlickCast"/>).
        /// </summary>
        public static IReadOnlyList<Sample> Samples { get { Poll(); return samples; } }

        /// <summary>The device of the current (or last) press.</summary>
        public static Kind PressKind { get { Poll(); return kind; } }

        /// <summary>True if the current (or last) press began over a UI element.</summary>
        public static bool StartedOverUI { get { Poll(); return startedOverUI; } }

        /// <summary>Pressed this frame and not on UI.</summary>
        public static bool WorldPressed => Pressed && !StartedOverUI;

        public static bool IsOverUI(Vector2 screen)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var ped = new PointerEventData(es) { position = screen };
            hits.Clear();
            es.RaycastAll(ped, hits);
            return hits.Count > 0;
        }

        public static bool KeyHeld(Key k) => Keyboard.current != null && Keyboard.current[k].isPressed;
        public static bool KeyPressed(Key k) => Keyboard.current != null && Keyboard.current[k].wasPressedThisFrame;

        /// <summary>Simulated walk keys used by the test autopilot.</summary>
        public static bool SimLeft, SimRight;

        /// <summary>
        /// Walking by keyboard: -1 (A / left arrow) .. 1 (D / right arrow), plus the autopilot's keys. The same keys hold
        /// the rod's sideways sweep while a rig is in the water or a fish is on (<see cref="SideSlide"/>): he only walks at the ready.
        /// </summary>
        public static float WalkKeys =>
            (SimRight || KeyHeld(Key.D) || KeyHeld(Key.RightArrow) ? 1f : 0f) - (SimLeft || KeyHeld(Key.A) || KeyHeld(Key.LeftArrow) ? 1f : 0f);
    }
}
