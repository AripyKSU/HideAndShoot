using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace HideAndShoot.World
{
    public sealed class SpawnPointRegistry : MonoBehaviour
    {
        private readonly List<WorldAnchor> playerSpawns = new List<WorldAnchor>();
        private readonly List<WorldAnchor> ammoCandidates = new List<WorldAnchor>();
        private Transform source;

        public int PlayerSpawnCount => playerSpawns.Count;
        public int AmmoCandidateCount => ammoCandidates.Count;

        private void Awake() => Refresh();

        public void UseSource(Transform anchors)
        {
            source = anchors;
            Refresh();
        }

        public void Refresh()
        {
            playerSpawns.Clear();
            ammoCandidates.Clear();
            foreach (var anchor in (source != null ? source : transform).GetComponentsInChildren<WorldAnchor>(true))
            {
                if (anchor.Kind == WorldAnchor.AnchorKind.PlayerSpawn)
                    playerSpawns.Add(anchor);
                else if (anchor.Kind == WorldAnchor.AnchorKind.AmmoCandidate)
                    ammoCandidates.Add(anchor);
            }
            playerSpawns.Sort((a,b) => a.Index.CompareTo(b.Index));
            ammoCandidates.Sort((a,b) => a.Index.CompareTo(b.Index));
        }

        /// <summary>
        /// Host draws without replacement: a unique randomly selected site for each player.
        /// Reset and reshuffle each round, not each client connection.
        /// </summary>
        public bool TryDrawPlayerPositions(int count, int roundSeed, out Vector3[] points)
            => TryDrawPlayerSpawns(count,roundSeed,out points,out _);

        /// <summary>
        /// Select unique spawn anchors and use their authored rotations.
        /// Facing directions are fixed in the scene; no runtime look-at calculation.
        /// </summary>
        public bool TryDrawPlayerSpawns(int count,int roundSeed,
            out Vector3[] points,out Quaternion[] rotations)
        {
            points = Array.Empty<Vector3>();
            rotations = Array.Empty<Quaternion>();
            if (count < 2 || count > 8 || playerSpawns.Count != 9)
                return false;
            if (playerSpawns.Any(x=>x==null)) return false;

            var pool = Enumerable.Range(0,playerSpawns.Count).ToArray();
            var random = new System.Random(roundSeed);
            for (int i=pool.Length-1; i>0; i--)
            {
                int j=random.Next(i+1);
                (pool[i],pool[j])=(pool[j],pool[i]);
            }

            points = new Vector3[count];
            rotations = new Quaternion[count];
            for (int i=0;i<count;i++)
            {
                var anchor = playerSpawns[pool[i]].transform;
                var start = anchor.position;
                // Position origins slightly above the floor for CharacterController.
                points[i]=start;
                rotations[i]=anchor.rotation;
            }
            return true;
        }

        public IReadOnlyList<Transform> GetAmmoCandidates() =>
            ammoCandidates.Select(a=>a.transform).ToArray();

        public bool AreGroundSpawnsOnNavMesh(float maxDistance, out string problem)
        {
            problem = string.Empty;
            if(playerSpawns.Count != 9)
            {
                problem = "Expected nine player spawn anchors; found " + playerSpawns.Count;
                return false;
            }
            foreach (var anchor in playerSpawns)
            {
                if(!NavMesh.SamplePosition(anchor.transform.position, out var hit,
                    maxDistance, NavMesh.AllAreas))
                {
                    problem = "Spawn outside walkable NavMesh: " + anchor.name;
                    return false;
                }
                if (Mathf.Abs(hit.position.y - anchor.transform.position.y) > 1.4f)
                {
                    problem = "Spawn snaps too far vertically: " + anchor.name;
                    return false;
                }
            }
            return true;
        }
    }
}
