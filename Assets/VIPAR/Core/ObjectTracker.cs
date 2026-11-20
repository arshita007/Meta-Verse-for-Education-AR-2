using System.Collections.Generic;
using UnityEngine;

namespace VipAr
{
    /// <summary>A detection that has been placed in world space using depth.</summary>
    public struct WorldObservation
    {
        public int ClassId;
        public float Score;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector2 Size;
    }

    public sealed class TrackedObject
    {
        public int Id;
        public int ClassId;
        public float Score;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector2 Size;
        public int Hits;
        public float LastSeen;
    }

    /// <summary>
    /// Turns per-image observations into stable objects: a new object must be seen more than
    /// once before it shows up (rejects one-frame false positives), updates are smoothed
    /// (reduces jitter), and an object survives brief misses before being dropped (reduces flicker).
    /// </summary>
    public sealed class ObjectTracker
    {
        public int HitsToConfirm = 2;
        public float SecondsToForget = 1.2f;
        public float MinMatchDistance = 0.2f;
        public float SizeMatchFactor = 0.6f;
        public float Smoothing = 0.4f;

        private readonly List<TrackedObject> m_tracks = new();
        private readonly HashSet<TrackedObject> m_matched = new();
        private int m_nextId = 1;

        /// <summary>Every live track, including ones not yet confirmed. Check <see cref="IsConfirmed"/>.</summary>
        public IReadOnlyList<TrackedObject> Tracks => m_tracks;

        public bool IsConfirmed(TrackedObject track) => track.Hits >= HitsToConfirm;

        public void Clear()
        {
            m_tracks.Clear();
            m_matched.Clear();
        }

        /// <summary>Feeds in one processed image. Call with an empty list when nothing was seen.</summary>
        public void Update(IReadOnlyList<WorldObservation> observations, float now)
        {
            m_matched.Clear();

            for (int i = 0; i < observations.Count; i++)
            {
                var observation = observations[i];
                var track = FindMatch(observation);
                if (track == null)
                {
                    track = new TrackedObject
                    {
                        Id = m_nextId++,
                        ClassId = observation.ClassId,
                        Position = observation.Position,
                        Rotation = observation.Rotation,
                        Size = observation.Size,
                    };
                    m_tracks.Add(track);
                }
                else
                {
                    track.Position = Vector3.Lerp(track.Position, observation.Position, Smoothing);
                    track.Rotation = Quaternion.Slerp(track.Rotation, observation.Rotation, Smoothing);
                    track.Size = Vector2.Lerp(track.Size, observation.Size, Smoothing);
                }

                track.Score = observation.Score;
                track.Hits++;
                track.LastSeen = now;
                m_matched.Add(track);
            }

            RemoveSupersededTracks();
            m_tracks.RemoveAll(track => now - track.LastSeen > SecondsToForget);
        }

        private TrackedObject FindMatch(in WorldObservation observation)
        {
            TrackedObject best = null;
            float bestDistance = float.MaxValue;

            foreach (var track in m_tracks)
            {
                if (track.ClassId != observation.ClassId || m_matched.Contains(track))
                {
                    continue;
                }

                float distance = Vector3.Distance(track.Position, observation.Position);
                if (distance < MatchRadius(track) && distance < bestDistance)
                {
                    best = track;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private float MatchRadius(TrackedObject track) =>
            Mathf.Max(MinMatchDistance, SizeMatchFactor * Mathf.Max(track.Size.x, track.Size.y));

        // When the model changes its mind about an object's class, the track for the old label
        // would otherwise linger on top of the new one until it expired.
        private void RemoveSupersededTracks()
        {
            m_tracks.RemoveAll(stale =>
            {
                if (m_matched.Contains(stale))
                {
                    return false;
                }

                foreach (var current in m_matched)
                {
                    if (current.ClassId != stale.ClassId &&
                        Vector3.Distance(current.Position, stale.Position) < 0.5f * MatchRadius(current))
                    {
                        return true;
                    }
                }

                return false;
            });
        }
    }
}
